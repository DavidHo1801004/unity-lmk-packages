using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Read-only data of every attribute of every loaded attribute registry: identity, defaults, clamp bounds and
    /// flags, and the clamp graph. Resolves a reference (registry ID, attribute ID) to an <see cref="AttributeHandle"/>,
    /// with no name fallback.
    /// </summary>
    /// <remarks>
    /// The registry database of the attribute kind: a row is an attribute, and a row's name is the attribute's name,
    /// e.g. "Vitals.Health". <br/>
    /// A bound that names another attribute is resolved when the lookups are built, across registries. A bound that
    /// doesn't resolve is ignored until its registry is loaded again. <br/>
    /// In a player, the database is baked at build time, preloaded, and found through <see cref="Resources"/> as a
    /// fallback. In the editor, it is built from the registry files and kept up to date by the editor tooling. <br/>
    /// Use <see cref="GameplayAttributeDatabaseBuilder"/> to create one.
    /// </remarks>
    public sealed class GameplayAttributeDatabase : RegistryDatabase<GameplayAttributeDatabase, AttributeRow>
    {
        /// <summary>
        /// Name of the baked database asset inside a <c>Resources</c> folder.
        /// </summary>
        public const string RESOURCE_NAME = nameof(GameplayAttributeDatabase);

        private static readonly AttributeHandle[] NO_HANDLES = Array.Empty<AttributeHandle>();

        // Built with the lookups, never serialized.
        [NonSerialized] private AttributeLimit[] minLimits;
        [NonSerialized] private AttributeLimit[] maxLimits;
        [NonSerialized] private AttributeHandle[][] dependents;
        [NonSerialized] private AttributeHandle[] initializationOrder;
        [NonSerialized] private AttributeHandle[] clampCycle;



        #region Active Database
#if UNITY_EDITOR
        /// <summary>
        /// Set by the editor tooling: makes sure an editor database is active.
        /// </summary>
        internal new static Action EditorEnsureActive
        {
            get => RegistryDatabase<GameplayAttributeDatabase, AttributeRow>.EditorEnsureActive;
            set => RegistryDatabase<GameplayAttributeDatabase, AttributeRow>.EditorEnsureActive = value;
        }
#endif

        /// <summary>
        /// Make <paramref name="_Database"/> the active database and start a new generation.
        /// </summary>
        internal new static void SetActive(GameplayAttributeDatabase _Database, bool _DestroyPrevious = false)
            => RegistryDatabase<GameplayAttributeDatabase, AttributeRow>.SetActive(_Database, _DestroyPrevious);

        /// <summary>
        /// Resolve a reference against <see cref="RegistryDatabase{TSelf, TRow}.Active"/>. Without an active
        /// database, any assigned reference is <see cref="AttributeStatus.MissingRegistry"/>.
        /// </summary>
        public new static AttributeResolution ResolveActive(RegistryGuid _Registry, RegistryGuid _Id)
            => AttributeResolution.From(RegistryDatabase<GameplayAttributeDatabase, AttributeRow>.ResolveActive(_Registry, _Id));
        #endregion



        /// <summary>
        /// Number of live attributes across all registries. Handles range from 0 to <c>AttributeCount - 1</c>.
        /// </summary>
        public int AttributeCount => RowCount;



        #region Resolution
        /// <summary>
        /// Resolve a reference. Exact: no name check and no fallback.
        /// </summary>
        public new AttributeResolution Resolve(RegistryGuid _Registry, RegistryGuid _Id)
            => AttributeResolution.From(base.Resolve(_Registry, _Id));

        /// <summary>
        /// Find the live attribute named <paramref name="_Name"/> in a registry. Name lookups do not survive renames:
        /// use them for debugging and console commands only.
        /// </summary>
        public AttributeHandle FindAttribute(RegistryGuid _Registry, string _Name)
            => new(FindByName(_Registry, _Name));
        #endregion



        #region Attribute Data
        public bool IsValid(AttributeHandle _Handle) => IsRowInRange(_Handle.Index);

        /// <summary>
        /// Full name of a live attribute, e.g. "Vitals.Health".
        /// </summary>
        public string GetName(AttributeHandle _Handle) => GetRowName(_Handle.Index);

        public RegistryGuid GetId(AttributeHandle _Handle) => GetRowId(_Handle.Index);

        public RegistryGuid GetRegistryId(AttributeHandle _Handle) => GetRowRegistryId(_Handle.Index);

        /// <summary>
        /// A reference to a live attribute, as an assignment would write it.
        /// </summary>
        public GameplayAttribute GetAttribute(AttributeHandle _Handle)
        {
            if (!IsRowInRange(_Handle.Index)) return GameplayAttribute.None;

            return GameplayAttribute.FromId(GetRowRegistryId(_Handle.Index), GetRowId(_Handle.Index), GetRowName(_Handle.Index));
        }

        /// <summary>
        /// The attribute's registry data, as stored: bounds name attributes by ID.
        /// </summary>
        public AttributeRow GetRow(AttributeHandle _Handle) => GetRowData(_Handle.Index);

        /// <summary>
        /// The base value a unit starts with when its set definition gives none.
        /// </summary>
        public float GetDefault(AttributeHandle _Handle) => IsRowInRange(_Handle.Index) ? Rows[_Handle.Index].defaultValue : 0f;

        public AttributeFlags GetFlags(AttributeHandle _Handle) => IsRowInRange(_Handle.Index) ? Rows[_Handle.Index].flags : AttributeFlags.None;

        /// <summary>
        /// Whether the attribute is a scratch value: no bounds, no change events, reset after each execution.
        /// </summary>
        public bool IsMeta(AttributeHandle _Handle) => (GetFlags(_Handle) & AttributeFlags.Meta) != 0;

        /// <summary>
        /// Whether the attribute is rounded to the nearest integer after evaluation.
        /// </summary>
        public bool IsInteger(AttributeHandle _Handle) => (GetFlags(_Handle) & AttributeFlags.Integer) != 0;

        public AttributeMaxChange GetOnMaxChange(AttributeHandle _Handle)
            => IsRowInRange(_Handle.Index) ? Rows[_Handle.Index].onMaxChange : AttributeMaxChange.Clamp;

        /// <summary>
        /// The lower bound: none, a constant, or a live attribute. A bound that doesn't resolve reads as none.
        /// </summary>
        public AttributeLimit GetMin(AttributeHandle _Handle)
        {
            EnsureLookups();
            return IsRowInRange(_Handle.Index) ? minLimits[_Handle.Index] : AttributeLimit.None;
        }

        /// <summary>
        /// The upper bound: none, a constant, or a live attribute. A bound that doesn't resolve reads as none.
        /// </summary>
        public AttributeLimit GetMax(AttributeHandle _Handle)
        {
            EnsureLookups();
            return IsRowInRange(_Handle.Index) ? maxLimits[_Handle.Index] : AttributeLimit.None;
        }

        /// <summary>
        /// The attributes that use this one as a bound. They are clamped again when its value changes.
        /// </summary>
        public IReadOnlyList<AttributeHandle> GetDependents(AttributeHandle _Handle)
        {
            EnsureLookups();
            return IsRowInRange(_Handle.Index) ? dependents[_Handle.Index] : NO_HANDLES;
        }

        /// <summary>
        /// Every attribute, each after the attributes it uses as bounds: the order in which a unit initialises them.
        /// Deterministic for the same content.
        /// </summary>
        public IReadOnlyList<AttributeHandle> InitializationOrder
        {
            get
            {
                EnsureLookups();
                return initializationOrder;
            }
        }

        /// <summary>
        /// Whether bounds form a cycle, e.g. <c>A.max = B</c> and <c>B.max = A</c>. Registry validation refuses it and
        /// baking for a build fails, so only hand-edited content gets here. The bounds that close the cycle are
        /// ignored.
        /// </summary>
        public bool HasClampCycle => ClampCycle.Count > 0;

        /// <summary>
        /// The attributes on a clamp cycle, or none.
        /// </summary>
        public IReadOnlyList<AttributeHandle> ClampCycle
        {
            get
            {
                EnsureLookups();
                return clampCycle;
            }
        }
        #endregion



        #region Clamp Graph
        protected override void OnLookupsBuilt()
        {
            int count = RowCount;
            var rows = Rows;

            var min = new AttributeLimit[count];
            var max = new AttributeLimit[count];
            for (int i = 0; i < count; i++)
            {
                min[i] = Link(rows[i].min);
                max[i] = Link(rows[i].max);
            }

            // Rows Kahn's algorithm can't order are on a cycle or after one. Peeling the ones that lead nowhere
            // inside that set leaves the rows on a cycle.
            Order(min, max, out var order, out var unordered);
            var cycle = OnCycle(min, max, unordered);

            // Drop the bounds that close a cycle, so evaluation always terminates.
            if (cycle.Count > 0)
            {
                foreach (int i in cycle)
                {
                    if (min[i].Kind == AttributeBoundKind.Attribute && cycle.Contains(min[i].Handle.Index)) min[i] = AttributeLimit.None;
                    if (max[i].Kind == AttributeBoundKind.Attribute && cycle.Contains(max[i].Handle.Index)) max[i] = AttributeLimit.None;
                }

                Order(min, max, out order, out _);
            }

            minLimits = min;
            maxLimits = max;
            dependents = BuildDependents(min, max);
            initializationOrder = order.ConvertAll(e => new AttributeHandle(e)).ToArray();

            var cycleHandles = new List<int>(cycle);
            cycleHandles.Sort();
            clampCycle = cycleHandles.ConvertAll(e => new AttributeHandle(e)).ToArray();
        }

        // Resolving here is allowed: the core lookups are ready.
        private AttributeLimit Link(in AttributeBound _Bound)
        {
            switch (_Bound.Kind)
            {
                case AttributeBoundKind.Constant:
                    return AttributeLimit.FromConstant(_Bound.ConstantValue);

                case AttributeBoundKind.Attribute:
                    var resolution = base.Resolve(_Bound.RegistryId, _Bound.AttributeId);
                    return resolution.IsLive ? AttributeLimit.FromAttribute(new AttributeHandle(resolution.Row)) : AttributeLimit.None;

                default:
                    return AttributeLimit.None;
            }
        }

        private static IEnumerable<int> BoundsOf(AttributeLimit[] _Min, AttributeLimit[] _Max, int _Row)
        {
            int minBound = _Min[_Row].Kind == AttributeBoundKind.Attribute ? _Min[_Row].Handle.Index : -1;
            int maxBound = _Max[_Row].Kind == AttributeBoundKind.Attribute ? _Max[_Row].Handle.Index : -1;

            if (minBound >= 0) yield return minBound;
            if (maxBound >= 0 && maxBound != minBound) yield return maxBound;
        }

        private static AttributeHandle[][] BuildDependents(AttributeLimit[] _Min, AttributeLimit[] _Max)
        {
            var lists = new List<AttributeHandle>[_Min.Length];
            for (int row = 0; row < _Min.Length; row++)
                foreach (int bound in BoundsOf(_Min, _Max, row))
                    (lists[bound] ??= new List<AttributeHandle>()).Add(new AttributeHandle(row));

            var result = new AttributeHandle[_Min.Length][];
            for (int i = 0; i < result.Length; i++)
                result[i] = lists[i]?.ToArray() ?? NO_HANDLES;

            return result;
        }

        /// <summary>
        /// Kahn's algorithm, always taking the lowest ready row, so the order only depends on the content.
        /// </summary>
        private static void Order(AttributeLimit[] _Min, AttributeLimit[] _Max, out List<int> _Order, out HashSet<int> _Unordered)
        {
            int count = _Min.Length;
            var pending = new int[count];
            var dependents = new List<int>[count];

            for (int row = 0; row < count; row++)
            {
                foreach (int bound in BoundsOf(_Min, _Max, row))
                {
                    pending[row]++;
                    (dependents[bound] ??= new List<int>()).Add(row);
                }
            }

            var ready = new SortedSet<int>();
            for (int row = 0; row < count; row++)
                if (pending[row] == 0)
                    ready.Add(row);

            _Order = new List<int>(count);
            while (ready.Count > 0)
            {
                int row = ready.Min;
                ready.Remove(row);
                _Order.Add(row);

                if (dependents[row] == null) continue;

                foreach (int dependent in dependents[row])
                    if (--pending[dependent] == 0)
                        ready.Add(dependent);
            }

            _Unordered = new HashSet<int>();
            for (int row = 0; row < count; row++)
                if (pending[row] > 0)
                    _Unordered.Add(row);
        }

        /// <summary>
        /// The rows of <paramref name="_Unordered"/> that lie on a cycle. Kahn's algorithm leaves the cycles and every
        /// row bounded through them; rows that no row of the set uses as a bound only follow a cycle, so they are
        /// removed until none is left to remove.
        /// </summary>
        private static HashSet<int> OnCycle(AttributeLimit[] _Min, AttributeLimit[] _Max, HashSet<int> _Unordered)
        {
            var remaining = new HashSet<int>(_Unordered);
            if (remaining.Count == 0) return remaining;

            bool removed = true;
            while (removed)
            {
                removed = false;

                var bounding = new HashSet<int>();
                foreach (int row in remaining)
                    foreach (int bound in BoundsOf(_Min, _Max, row))
                        if (remaining.Contains(bound))
                            bounding.Add(bound);

                foreach (int row in new List<int>(remaining))
                {
                    if (bounding.Contains(row)) continue;

                    remaining.Remove(row);
                    removed = true;
                }
            }

            return remaining;
        }
        #endregion



        #region Diagnostics
        /// <summary>
        /// Log one warning per unresolved reference, in Play Mode and in development builds.
        /// </summary>
        internal static void ReportUnresolved(in GameplayAttribute _Attribute, AttributeStatus _Status)
        {
            var attribute = _Attribute;
            ReportUnresolved(attribute.RegistryId, attribute.Id, () =>
                $"Gameplay attribute \"{attribute.LastKnownName}\" does not resolve ({_Status}, registry {attribute.RegistryId}, " +
                $"id {attribute.Id}). Operations on it do nothing.");
        }
        #endregion
    }
}
