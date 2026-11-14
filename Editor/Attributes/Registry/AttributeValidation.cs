using System;
using System.Collections.Generic;
using System.Linq;

using LMK.Editor.Registry;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// What is wrong with an attribute registry. Any of them refuses Save and rejects the file.
    /// </summary>
    internal enum AttributeIssueKind
    {
        /// <summary> A name that isn't segments of letters, digits and '_' separated by '.'. </summary>
        InvalidName,

        /// <summary> Two attributes of one registry share a name. </summary>
        DuplicateName,

        /// <summary> A value that isn't a finite number. </summary>
        InvalidValue,

        /// <summary> A bound names an attribute of the same registry that doesn't exist, or names no registry. </summary>
        DanglingBound,

        /// <summary> Bounds form a cycle, e.g. <c>A.max = B</c> and <c>B.max = A</c>. </summary>
        ClampCycle,

        /// <summary> A constant <c>min</c> above a constant <c>max</c>, or a default outside constant bounds. </summary>
        RangeError,

        /// <summary> A meta attribute with bounds, or used as a bound. </summary>
        MetaMisuse,
    }



    /// <summary>
    /// One problem with an attribute registry, with the names of the attributes involved.
    /// </summary>
    internal readonly struct AttributeIssue
    {
        public readonly AttributeIssueKind Kind;
        public readonly IReadOnlyList<string> Attributes;
        public readonly string Message;

        public AttributeIssue(AttributeIssueKind _Kind, string _Message, params string[] _Attributes)
        {
            Kind = _Kind;
            Message = _Message;
            Attributes = _Attributes;
        }

        public override string ToString() => Message;
    }



    /// <summary>
    /// The registry rules that one registry can check on its own: valid and unique names, finite values,
    /// existing bounds within the registry, no clamp cycle, constant ranges, and meta attributes without bounds.
    /// </summary>
    /// <remarks>
    /// Bounds that name another registry's attribute are checked when they are set (<see cref="AttributeOperations"/>),
    /// since the other registry may not be loaded when a file is imported. A cycle through several registries can't
    /// reach a build either: baking the database refuses it.
    /// </remarks>
    internal static class AttributeValidation
    {
        public static List<AttributeIssue> Check(RegistryData<AttributeRegistryRow> _Data)
        {
            var issues = new List<AttributeIssue>();
            var rows = new Dictionary<RegistryGuid, AttributeRegistryRow>(_Data.rows.Count);
            foreach (var row in _Data.rows)
                if (!row.id.IsZero)
                    rows.TryAdd(row.id, row.data);

            string NameOf(RegistryGuid _Id) => rows.TryGetValue(_Id, out var row) ? row.name : _Id.ToString();

            CheckNames(rows, issues);

            foreach (var (id, row) in rows)
            {
                CheckValues(row, issues);
                CheckBound(_Data.registryId, row, row.min, "min", rows, issues);
                CheckBound(_Data.registryId, row, row.max, "max", rows, issues);
                CheckMeta(_Data.registryId, row, rows, issues);
            }

            foreach (var cycle in FindCycles(_Data.registryId, rows))
            {
                var names = cycle.Select(NameOf).ToArray();
                issues.Add(new AttributeIssue(AttributeIssueKind.ClampCycle,
                    $"Clamp cycle: {string.Join(" → ", names.Append(names[0]).Select(e => $"\"{e}\""))}. A bound can't depend on itself.",
                    names));
            }

            return issues;
        }

        /// <summary>
        /// Whether <paramref name="_Issues"/> has an issue of <paramref name="_Kind"/> that involves at least the named
        /// attributes.
        /// </summary>
        public static bool HasError(this IEnumerable<AttributeIssue> _Issues, AttributeIssueKind _Kind, params string[] _Involving)
            => _Issues.Any(e => e.Kind == _Kind && _Involving.All(name => e.Attributes.Contains(name)));



        private static void CheckNames(Dictionary<RegistryGuid, AttributeRegistryRow> _Rows, List<AttributeIssue> _Issues)
        {
            foreach (var (id, row) in _Rows)
                if (!AttributeName.IsValid(row.name, out string error))
                    _Issues.Add(new AttributeIssue(AttributeIssueKind.InvalidName, $"Attribute {id}: {error}", row.name ?? string.Empty));

            foreach (var group in _Rows.Values.GroupBy(e => e.name ?? string.Empty, StringComparer.Ordinal).Where(e => e.Count() > 1))
                _Issues.Add(new AttributeIssue(AttributeIssueKind.DuplicateName, $"Duplicate name: {group.Count()} attributes are named \"{group.Key}\".", group.Key));
        }

        private static void CheckValues(in AttributeRegistryRow _Row, List<AttributeIssue> _Issues)
        {
            if (!float.IsFinite(_Row.defaultValue))
                _Issues.Add(new AttributeIssue(AttributeIssueKind.InvalidValue, $"\"{_Row.name}\": the default is not a finite number.", _Row.name));

            foreach (var bound in new[] { _Row.min, _Row.max })
                if (bound.IsConstant && !float.IsFinite(bound.ConstantValue))
                    _Issues.Add(new AttributeIssue(AttributeIssueKind.InvalidValue, $"\"{_Row.name}\": a bound is not a finite number.", _Row.name));

            var min = _Row.min;
            var max = _Row.max;
            if (min.IsConstant && max.IsConstant && min.ConstantValue > max.ConstantValue)
                _Issues.Add(new AttributeIssue(AttributeIssueKind.RangeError,
                    $"Range error: \"{_Row.name}\" has min {min} above max {max}.", _Row.name));

            if ((min.IsConstant && _Row.defaultValue < min.ConstantValue) || (max.IsConstant && _Row.defaultValue > max.ConstantValue))
                _Issues.Add(new AttributeIssue(AttributeIssueKind.RangeError,
                    $"Range error: the default {Format(_Row.defaultValue)} of \"{_Row.name}\" is outside its bounds [{min}, {max}].", _Row.name));
        }

        private static void CheckBound(RegistryGuid _Registry, in AttributeRegistryRow _Row, in AttributeBound _Bound, string _Which,
                                       Dictionary<RegistryGuid, AttributeRegistryRow> _Rows, List<AttributeIssue> _Issues)
        {
            if (!_Bound.IsAttribute) return;

            if (_Bound.RegistryId.IsZero)
            {
                _Issues.Add(new AttributeIssue(AttributeIssueKind.DanglingBound,
                    $"Dangling bound: the {_Which} of \"{_Row.name}\" names no registry.", _Row.name));
                return;
            }

            if (_Bound.RegistryId == _Registry && !_Rows.ContainsKey(_Bound.AttributeId))
                _Issues.Add(new AttributeIssue(AttributeIssueKind.DanglingBound,
                    $"Dangling bound: the {_Which} of \"{_Row.name}\" is attribute {_Bound.AttributeId}, which isn't in this registry.", _Row.name));
        }

        private static void CheckMeta(RegistryGuid _Registry, in AttributeRegistryRow _Row, Dictionary<RegistryGuid, AttributeRegistryRow> _Rows,
                                      List<AttributeIssue> _Issues)
        {
            if (_Row.IsMeta && (!_Row.min.IsNone || !_Row.max.IsNone))
                _Issues.Add(new AttributeIssue(AttributeIssueKind.MetaMisuse,
                    $"Meta misuse: \"{_Row.name}\" is a meta attribute, so it can't have bounds.", _Row.name));

            foreach (var bound in new[] { _Row.min, _Row.max })
            {
                if (!bound.IsAttribute || bound.RegistryId != _Registry) continue;
                if (!_Rows.TryGetValue(bound.AttributeId, out var target) || !target.IsMeta) continue;

                _Issues.Add(new AttributeIssue(AttributeIssueKind.MetaMisuse,
                    $"Meta misuse: \"{_Row.name}\" is bounded by the meta attribute \"{target.name}\", which only holds a scratch value.",
                    _Row.name, target.name));
            }
        }

        /// <summary>
        /// Every cycle of bounds within the registry, as the strongly connected components that have one, each starting
        /// at its lowest ID. Bounds to other registries can't close a cycle here.
        /// </summary>
        private static List<List<RegistryGuid>> FindCycles(RegistryGuid _Registry, Dictionary<RegistryGuid, AttributeRegistryRow> _Rows)
        {
            var edges = new Dictionary<RegistryGuid, List<RegistryGuid>>();
            foreach (var (id, row) in _Rows)
            {
                var targets = new List<RegistryGuid>();
                foreach (var bound in new[] { row.min, row.max })
                    if (bound.IsAttribute && bound.RegistryId == _Registry && _Rows.ContainsKey(bound.AttributeId) && !targets.Contains(bound.AttributeId))
                        targets.Add(bound.AttributeId);

                edges[id] = targets;
            }

            // Tarjan's algorithm. Registries are small, so recursion depth is no concern.
            int counter = 0;
            var index = new Dictionary<RegistryGuid, int>();
            var low = new Dictionary<RegistryGuid, int>();
            var stack = new Stack<RegistryGuid>();
            var onStack = new HashSet<RegistryGuid>();
            var cycles = new List<List<RegistryGuid>>();

            void Visit(RegistryGuid _Id)
            {
                index[_Id] = low[_Id] = counter++;
                stack.Push(_Id);
                onStack.Add(_Id);

                foreach (var target in edges[_Id])
                {
                    if (!index.ContainsKey(target))
                    {
                        Visit(target);
                        low[_Id] = Math.Min(low[_Id], low[target]);
                    }
                    else if (onStack.Contains(target))
                    {
                        low[_Id] = Math.Min(low[_Id], index[target]);
                    }
                }

                if (low[_Id] != index[_Id]) return;

                var component = new List<RegistryGuid>();
                RegistryGuid member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    component.Add(member);
                }
                while (member != _Id);

                if (component.Count > 1 || edges[_Id].Contains(_Id))
                    cycles.Add(OrderCycle(component, edges));
            }

            foreach (var id in _Rows.Keys.OrderBy(e => e))
                if (!index.ContainsKey(id))
                    Visit(id);

            return cycles;
        }

        /// <summary>
        /// The members of a cycle in bound order, from the lowest ID, so messages read "A → B → A".
        /// </summary>
        private static List<RegistryGuid> OrderCycle(List<RegistryGuid> _Component, Dictionary<RegistryGuid, List<RegistryGuid>> _Edges)
        {
            var members = new HashSet<RegistryGuid>(_Component);
            var start = _Component.Min();
            var ordered = new List<RegistryGuid> { start };

            for (var current = start; ordered.Count < members.Count;)
            {
                var next = _Edges[current].Where(e => members.Contains(e) && !ordered.Contains(e)).DefaultIfEmpty(RegistryGuid.Zero).Min();
                if (next.IsZero)
                {
                    // Not a simple cycle: list the rest by ID.
                    ordered.AddRange(members.Where(e => !ordered.Contains(e)).OrderBy(e => e));
                    break;
                }

                ordered.Add(next);
                current = next;
            }

            return ordered;
        }

        private static string Format(float _Value) => _Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }
}
