using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using UnityEditor;

using LMK.Editor.Registry;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Gameplay attributes as a registry kind: the row format of <c>*.attrregistry</c> files, the attribute rules,
    /// attribute names as row names, and the <see cref="GameplayAttributeDatabase"/>. The registry core
    /// loads, stages, saves, locks, indexes, builds and generates code for the registries.
    /// </summary>
    /// <remarks>
    /// <code>
    /// # gameplayattribute-registry v1 — written by the Attribute Manager. Do not edit by hand.
    /// registry  7c1e9a…
    /// name      Main
    /// kind      main
    /// checksum  sha256:…
    /// # guid | name          | default | min | max                  | onMaxChange | flags | description
    /// 0a3f…  | Vitals.Health | 100     | 0   | @7c1e9a…:5b1c…       | clamp       | -     | Current hit points
    /// </code>
    /// A bound is <c>-</c>, a constant, or <c>@&lt;registry ID&gt;:&lt;attribute ID&gt;</c>. Numbers use the invariant
    /// culture and round-trip exactly.
    /// </remarks>
    [InitializeOnLoad]
    internal sealed class AttributeKind : RegistryKindEditor<AttributeRegistryRow, AttributeRegistryAsset>
    {
        public const string ID = "gameplayattributes";

        public const string EXTENSION = "attrregistry";

        public const string ROOT_FOLDER = "Assets/GameplayAttributes";

        public const string FORMAT_LINE = "# gameplayattribute-registry v1 — written by the Attribute Manager. Do not edit by hand.";

        public const string MAX_CHANGE_CLAMP = "clamp";
        public const string MAX_CHANGE_KEEP_RATIO = "keepRatio";
        public const string FLAG_META = "meta";
        public const string FLAG_INTEGER = "integer";

        private const char BOUND_PREFIX = '@';
        private const char BOUND_SEPARATOR = ':';

        private static readonly RegistryColumn[] COLUMNS =
        {
            RegistryColumn.FitContent("name"),
            RegistryColumn.FitContent("default"),
            RegistryColumn.FitContent("min"),
            RegistryColumn.FitContent("max"),
            RegistryColumn.Fixed("onMaxChange", MAX_CHANGE_KEEP_RATIO.Length),
            RegistryColumn.FitContent("flags"),
            RegistryColumn.Last("description"),
        };

        public static readonly AttributeKind Instance = new();

        static AttributeKind()
        {
            RegistryKinds.Register(Instance);
            RegistryEditorDatabase.Hook<GameplayAttributeDatabase, AttributeRow>(Instance.EnsureLoaded);
        }

        private AttributeKind() { }



        #region Identity
        public override string Id => ID;
        public override string DisplayName => "Gameplay Attribute";
        public override string FileExtension => EXTENSION;
        public override string RootFolder => ROOT_FOLDER;
        public override string RowNoun => "attribute";
        public override string EditorName => "Attribute Manager";
        public override Type ReferenceType => typeof(GameplayAttribute);

        public override bool CanOpenRegistry => true;

        public override void OpenRegistry(RegistryGuid _RegistryId) => AttributeManagerWindow.OpenRegistry(_RegistryId);

        public override void ShowRowPicker(UnityEngine.Rect _Anchor, Action<RegistryGuid, RegistryGuid> _OnPicked)
            => AttributePicker.Show(_Anchor, GameplayAttribute.None, false, e => _OnPicked(e.RegistryId, e.Id));
        #endregion



        #region Format
        public override string FormatLine => FORMAT_LINE;

        public override IReadOnlyList<RegistryColumn> Columns => COLUMNS;

        public override bool TryParseRow(string[] _Cells, out AttributeRegistryRow _Row, out string _Error)
        {
            _Row = default;

            if (!TryParseNumber(_Cells[1], out float defaultValue))
            {
                _Error = $"invalid default \"{_Cells[1]}\".";
                return false;
            }

            if (!TryParseBound(_Cells[2], out var min, out _Error) || !TryParseBound(_Cells[3], out var max, out _Error))
                return false;

            AttributeMaxChange onMaxChange;
            if (_Cells[4] == MAX_CHANGE_KEEP_RATIO) onMaxChange = AttributeMaxChange.KeepRatio;
            else if (_Cells[4] == MAX_CHANGE_CLAMP || _Cells[4] == RegistryFile.NONE || _Cells[4].Length == 0) onMaxChange = AttributeMaxChange.Clamp;
            else
            {
                _Error = $"unknown onMaxChange \"{_Cells[4]}\". Use \"{MAX_CHANGE_CLAMP}\" or \"{MAX_CHANGE_KEEP_RATIO}\".";
                return false;
            }

            if (!TryParseFlags(_Cells[5], out var flags, out _Error))
                return false;

            _Row = new AttributeRegistryRow(_Cells[0], defaultValue, min, max, onMaxChange, flags, _Cells[6]);
            _Error = null;
            return true;
        }

        public override string[] FormatRow(in AttributeRegistryRow _Row) => new[]
        {
            _Row.name ?? string.Empty,
            FormatNumber(_Row.defaultValue),
            FormatBound(_Row.min),
            FormatBound(_Row.max),
            _Row.onMaxChange == AttributeMaxChange.KeepRatio ? MAX_CHANGE_KEEP_RATIO : MAX_CHANGE_CLAMP,
            FormatFlags(_Row.flags),
            _Row.description ?? string.Empty,
        };

        public override void Validate(RegistryData<AttributeRegistryRow> _Data, List<string> _Errors)
        {
            foreach (var issue in AttributeValidation.Check(_Data))
                _Errors.Add(issue.Message);
        }

        public override IReadOnlyDictionary<RegistryGuid, string> GetNames(RegistryData<AttributeRegistryRow> _Data)
        {
            var names = new Dictionary<RegistryGuid, string>(_Data.rows.Count);
            foreach (var row in _Data.rows)
                if (!row.id.IsZero && !string.IsNullOrEmpty(row.data.name))
                    names[row.id] = row.data.name;

            return names;
        }

        internal static string FormatNumber(float _Value) => _Value.ToString("R", CultureInfo.InvariantCulture);

        internal static bool TryParseNumber(string _Text, out float _Value)
            => float.TryParse(_Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _Value) && float.IsFinite(_Value);

        internal static string FormatBound(in AttributeBound _Bound) => _Bound.Kind switch
        {
            AttributeBoundKind.Constant => FormatNumber(_Bound.ConstantValue),
            AttributeBoundKind.Attribute => $"{BOUND_PREFIX}{_Bound.RegistryId}{BOUND_SEPARATOR}{_Bound.AttributeId}",
            _ => RegistryFile.NONE,
        };

        internal static bool TryParseBound(string _Text, out AttributeBound _Bound, out string _Error)
        {
            _Bound = AttributeBound.None;
            _Error = null;

            if (string.IsNullOrEmpty(_Text) || _Text == RegistryFile.NONE) return true;

            if (_Text[0] == BOUND_PREFIX)
            {
                int separator = _Text.IndexOf(BOUND_SEPARATOR);
                if (separator > 0
                    && RegistryGuid.TryParse(_Text[1..separator], out var registry) && !registry.IsZero
                    && RegistryGuid.TryParse(_Text[(separator + 1)..], out var id) && !id.IsZero)
                {
                    _Bound = AttributeBound.FromAttribute(registry, id);
                    return true;
                }

                _Error = $"invalid attribute bound \"{_Text}\". Use \"{BOUND_PREFIX}<registry ID>{BOUND_SEPARATOR}<attribute ID>\".";
                return false;
            }

            if (TryParseNumber(_Text, out float value))
            {
                _Bound = AttributeBound.FromConstant(value);
                return true;
            }

            _Error = $"invalid bound \"{_Text}\". Use \"-\", a number, or \"{BOUND_PREFIX}<registry ID>{BOUND_SEPARATOR}<attribute ID>\".";
            return false;
        }

        private static string FormatFlags(AttributeFlags _Flags)
        {
            var parts = new List<string>(2);
            if ((_Flags & AttributeFlags.Meta) != 0) parts.Add(FLAG_META);
            if ((_Flags & AttributeFlags.Integer) != 0) parts.Add(FLAG_INTEGER);
            return parts.Count > 0 ? string.Join(",", parts) : RegistryFile.NONE;
        }

        private static bool TryParseFlags(string _Text, out AttributeFlags _Flags, out string _Error)
        {
            _Flags = AttributeFlags.None;
            _Error = null;
            if (string.IsNullOrEmpty(_Text) || _Text == RegistryFile.NONE) return true;

            foreach (string part in _Text.Split(','))
            {
                switch (part.Trim())
                {
                    case FLAG_META: _Flags |= AttributeFlags.Meta; break;
                    case FLAG_INTEGER: _Flags |= AttributeFlags.Integer; break;
                    default:
                        _Error = $"unknown flag \"{part.Trim()}\". Use \"{FLAG_META}\" or \"{FLAG_INTEGER}\".";
                        return false;
                }
            }

            return true;
        }
        #endregion



        #region Database
        protected override RegistryDatabase BuildDatabase(RegistrySnapshot<AttributeRegistryRow> _Snapshot) => BuildAttributeDatabase(_Snapshot, false);

        /// <summary>
        /// The database of the saved registries, for builds. Bounds that form a cycle, even across registries, fail it.
        /// </summary>
        /// <exception cref="AttributeClampCycleException"> If clamp bounds form a cycle. </exception>
        public override RegistryDatabase BuildSavedDatabase() => BuildAttributeDatabase(SavedSnapshot, true);

        /// <param name="_Strict"> Refuse a clamp cycle. The editor database ignores the bounds that close it instead. </param>
        internal static GameplayAttributeDatabase BuildAttributeDatabase(RegistrySnapshot<AttributeRegistryRow> _Snapshot, bool _Strict)
        {
            var builder = new GameplayAttributeDatabaseBuilder();
            foreach (var registry in _Snapshot.Registries)
            {
                var rows = new List<GameplayAttributeDatabaseBuilder.Row>(registry.Count);
                foreach (var id in registry.Ids)
                    if (registry.TryGetRow(id, out var row))
                        rows.Add(new GameplayAttributeDatabaseBuilder.Row(id, registry.GetName(id) ?? row.name, row.ToRuntime()));

                builder.AddRegistry(registry.Id, registry.Name, registry.Origin, rows);
            }

            return _Strict ? builder.Build() : builder.BuildLenient();
        }

        protected override void PublishDatabase(RegistryDatabase _Database, RegistryDatabase _Previous)
            => RegistryEditorDatabase.Publish<GameplayAttributeDatabase, AttributeRow>(_Database, _Previous);
        #endregion



        #region Code Generation
        /// <summary>
        /// The <c>Attributes</c> class: one nested class per registry, one constant per attribute.
        /// </summary>
        public override RegistryCodeStyle CodeStyle => AttributeCodeGen.STYLE;

        protected override string GetCodeDescription(in AttributeRegistryRow _Row) => _Row.description;
        #endregion



        #region Packages
        /// <summary>
        /// Add an attribute for a reference from a package: the row named <paramref name="_Name"/> if there is one,
        /// otherwise a new row with default 0 and no bounds.
        /// </summary>
        public override RegistryGuid AddRowForOrphan(RegistryData<AttributeRegistryRow> _Data, string _Name, RegistryGuid _PreferredId,
                                                     string _Description, out string _Warning)
        {
            _Warning = null;
            if (!AttributeName.IsValid(_Name, out string error))
            {
                _Warning = $"Attribute {_PreferredId} has no valid name (\"{_Name}\"): {error}";
                return RegistryGuid.Zero;
            }

            foreach (var row in _Data.rows)
                if (row.data.name == _Name)
                    return row.id;

            var id = !_PreferredId.IsZero && _Data.IndexOf(_PreferredId) < 0 ? _PreferredId : AttributeOperations.NewUniqueId(_Data);
            _Data.rows.Add(AttributeRegistryRow.Create(id, _Name, _Description: _Description));
            return id;
        }

        /// <summary>
        /// A package's bounds may only name attributes of its own registry and its dependencies' registries: never a
        /// main registry, which other projects don't have.
        /// </summary>
        protected override void ValidatePackRows(RegistryData<AttributeRegistryRow> _Own, IReadOnlyCollection<RegistryGuid> _AllowedRegistries,
                                                 RegistryGuid _MainRegistryId, List<string> _Problems)
        {
            foreach (var row in _Own.rows)
            {
                foreach (var (bound, which) in new[] { (row.data.min, "min"), (row.data.max, "max") })
                {
                    if (!bound.IsAttribute || _AllowedRegistries.Contains(bound.RegistryId)) continue;

                    _Problems.Add(!_MainRegistryId.IsZero && bound.RegistryId == _MainRegistryId
                        ? $"the {which} of \"{row.data.name}\" names an attribute of this project's main registry, which other projects don't have. " +
                          "Bound it by an attribute of the package's registry instead."
                        : $"the {which} of \"{row.data.name}\" names an attribute of registry {bound.RegistryId}, which is neither the package's own " +
                          "registry nor the registry of a package listed in its dependencies.");
                }
            }
        }
        #endregion
    }
}
