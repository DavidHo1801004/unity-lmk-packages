using System.Collections.Generic;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry.Tests
{
    /// <summary>
    /// File row of the tree test kind: the columns of gameplay tag registries (format v3).
    /// </summary>
    [System.Serializable]
    public struct TreeRow
    {
        public RegistryGuid parent;
        public string segment;
        public bool isImplicit;
        public string description;

        public TreeRow(RegistryGuid _Parent, string _Segment, bool _IsImplicit = false, string _Description = "")
        {
            parent = _Parent;
            segment = _Segment;
            isImplicit = _IsImplicit;
            description = _Description ?? string.Empty;
        }
    }



    /// <summary>
    /// A registry format laid out like gameplay tag registries, so tests can check that the core writes
    /// such files byte for byte.
    /// </summary>
    public sealed class TreeFormat : IRegistryFormat<TreeRow>
    {
        public const string EXTENSION = "treetestregistry";

        private const string FLAG_IMPLICIT = "implicit";

        public static readonly TreeFormat Instance = new();

        private static readonly RegistryColumn[] columns =
        {
            RegistryColumn.Fixed("parent", RegistryGuid.TEXT_LENGTH),
            RegistryColumn.FitContent("segment"),
            RegistryColumn.Fixed("flags", 8),
            RegistryColumn.Last("description"),
        };

        public string FormatLine => "# gameplaytag-registry v3 — written by the Gameplay Tag Manager. Do not edit by hand.";

        public string RowNoun => "tag";

        public string EditorName => "Gameplay Tag Manager";

        public IReadOnlyList<RegistryColumn> Columns => columns;

        public bool TryParseRow(string[] _Cells, out TreeRow _Row, out string _Error)
        {
            _Row = default;
            _Error = null;

            var parent = RegistryGuid.Zero;
            if (_Cells[0] != RegistryFile.NONE && (!RegistryGuid.TryParse(_Cells[0], out parent) || parent.IsZero))
            {
                _Error = $"invalid parent ID \"{_Cells[0]}\".";
                return false;
            }

            bool isImplicit;
            if (_Cells[2] == FLAG_IMPLICIT) isImplicit = true;
            else if (_Cells[2] == RegistryFile.NONE || _Cells[2].Length == 0) isImplicit = false;
            else
            {
                _Error = $"unknown flags \"{_Cells[2]}\".";
                return false;
            }

            _Row = new TreeRow(parent, _Cells[1], isImplicit, _Cells[3]);
            return true;
        }

        public string[] FormatRow(in TreeRow _Row) => new[]
        {
            _Row.parent.IsZero ? RegistryFile.NONE : _Row.parent.ToString(),
            _Row.segment,
            _Row.isImplicit ? FLAG_IMPLICIT : RegistryFile.NONE,
            _Row.description,
        };

        public void Validate(RegistryData<TreeRow> _Data, List<string> _Errors)
        {
            var ids = new HashSet<RegistryGuid>();
            foreach (var row in _Data.rows)
                ids.Add(row.id);

            foreach (var row in _Data.rows)
                if (!row.data.parent.IsZero && !ids.Contains(row.data.parent))
                    _Errors.Add($"Row {row.id} (\"{row.data.segment}\"): parent {row.data.parent} does not exist.");
        }
    }
}
