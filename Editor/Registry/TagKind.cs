using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEngine;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Gameplay tags as a registry kind: the row format of <c>*.tagregistry</c> files, the tree rules, tag paths
    /// as row names, and the <see cref="GameplayTagDatabase"/>. The registry core loads, stages, saves and locks
    /// the registries.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class TagKind : RegistryKindEditor<TagRegistryRow, TagRegistryAsset>
    {
        public const string ID = "gameplaytags";

        public const string EXTENSION = "tagregistry";

        public const string ROOT_FOLDER = "Assets/GameplayTags";

        public const string FORMAT_LINE = "# gameplaytag-registry v3 — written by the Gameplay Tag Manager. Do not edit by hand.";

        private const string FLAG_IMPLICIT = "implicit";
        private const int FLAGS_WIDTH = 8;

        private static readonly RegistryColumn[] COLUMNS =
        {
            RegistryColumn.Fixed("parent", RegistryGuid.TEXT_LENGTH),
            RegistryColumn.FitContent("segment"),
            RegistryColumn.Fixed("flags", FLAGS_WIDTH),
            RegistryColumn.Last("description"),
        };

        public static readonly TagKind Instance = new();

        static TagKind()
        {
            RegistryKinds.Register(Instance);
            RegistryEditorDatabase.Hook<GameplayTagDatabase, TagRow>(Instance.EnsureLoaded);
        }

        private TagKind() { }



        #region Identity
        public override string Id => ID;
        public override string DisplayName => "Gameplay Tag";
        public override string FileExtension => EXTENSION;
        public override string RootFolder => ROOT_FOLDER;
        public override string RowNoun => "tag";
        public override string EditorName => "Gameplay Tag Manager";
        public override Type ReferenceType => typeof(GameplayTag);

        public override bool CanOpenRegistry => true;

        public override void OpenRegistry(RegistryGuid _RegistryId) => GameplayTagManagerWindow.OpenRegistry(_RegistryId);
        #endregion



        #region Format
        public override string FormatLine => FORMAT_LINE;

        public override IReadOnlyList<RegistryColumn> Columns => COLUMNS;

        public override bool TryParseRow(string[] _Cells, out TagRegistryRow _Row, out string _Error)
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

            _Row = new TagRegistryRow(parent, _Cells[1], isImplicit, _Cells[3]);
            return true;
        }

        public override string[] FormatRow(in TagRegistryRow _Row) => new[]
        {
            _Row.parent.IsZero ? RegistryFile.NONE : _Row.parent.ToString(),
            _Row.segment ?? string.Empty,
            _Row.isImplicit ? FLAG_IMPLICIT : RegistryFile.NONE,
            _Row.description ?? string.Empty,
        };

        /// <summary>
        /// Valid segments, existing parents, no cycles, unique segments among siblings. The core has already checked
        /// that IDs are non-zero and unique.
        /// </summary>
        public override void Validate(RegistryData<TagRegistryRow> _Data, List<string> _Errors)
        {
            var byId = new Dictionary<RegistryGuid, TagRegistryRow>(_Data.rows.Count);
            foreach (var row in _Data.rows)
            {
                if (!row.id.IsZero)
                    byId.TryAdd(row.id, row.data);

                if (!TagPath.IsValidSegment(row.data.segment, out string error))
                    _Errors.Add($"Row {row.id}: {error}");
            }

            var siblings = new HashSet<(RegistryGuid, string)>();
            foreach (var (id, row) in byId)
            {
                if (!row.parent.IsZero && !byId.ContainsKey(row.parent))
                    _Errors.Add($"Row {id} (\"{row.segment}\"): parent {row.parent} does not exist.");

                if (!siblings.Add((row.parent, row.segment)))
                    _Errors.Add($"Two tags named \"{row.segment}\" share the parent {(row.parent.IsZero ? "(root)" : row.parent.ToString())}.");
            }

            // Cycles: walking up from any row must reach a root within row count steps.
            foreach (var (id, row) in byId)
            {
                var current = row;
                for (int steps = 0; !current.parent.IsZero; steps++)
                {
                    if (steps > byId.Count || !byId.TryGetValue(current.parent, out current))
                    {
                        if (steps > byId.Count)
                            _Errors.Add($"Row {id} (\"{row.segment}\") is part of a parent cycle.");
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// The full path of every row that reaches a root.
        /// </summary>
        public override IReadOnlyDictionary<RegistryGuid, string> GetNames(RegistryData<TagRegistryRow> _Data)
        {
            var rows = new Dictionary<RegistryGuid, TagRegistryRow>(_Data.rows.Count);
            foreach (var row in _Data.rows)
                rows[row.id] = row.data;

            var paths = new Dictionary<RegistryGuid, string>(rows.Count);
            var segments = new List<string>();

            foreach (var id in rows.Keys)
            {
                segments.Clear();
                var current = id;
                bool valid = true;

                // Validated registries have no cycles, but never loop forever.
                for (int steps = 0; !current.IsZero; steps++)
                {
                    if (steps > rows.Count || !rows.TryGetValue(current, out var row))
                    {
                        valid = false;
                        break;
                    }

                    segments.Add(row.segment);
                    current = row.parent;
                }

                if (!valid) continue;

                segments.Reverse();
                paths[id] = string.Join(TagPath.SEPARATOR.ToString(), segments);
            }

            return paths;
        }
        #endregion



        #region Database
        protected override RegistryDatabase BuildDatabase(RegistrySnapshot<TagRegistryRow> _Snapshot) => BuildTagDatabase(_Snapshot);

        internal GameplayTagDatabase BuildTagDatabase(RegistrySnapshot<TagRegistryRow> _Snapshot)
        {
            var builder = new GameplayTagDatabaseBuilder();
            foreach (var registry in _Snapshot.Registries)
            {
                var rows = new List<GameplayTagDatabaseBuilder.Row>(registry.Count);
                foreach (var id in registry.Ids)
                    if (registry.TryGetRow(id, out var row))
                        rows.Add(new GameplayTagDatabaseBuilder.Row(id, row.parent, row.segment, row.isImplicit));

                builder.AddRegistry(registry.Id, registry.Name, (TagRegistryKind)registry.Origin, rows);
            }

            return builder.Build();
        }

        protected override void PublishDatabase(RegistryDatabase _Database, RegistryDatabase _Previous)
            => RegistryEditorDatabase.Publish<GameplayTagDatabase, TagRow>(_Database, _Previous);
        #endregion



        #region Code Generation
        /// <summary>
        /// The <c>Tags</c> class: the layout of gameplay tags 1.0.0. The registry core writes it.
        /// </summary>
        public override RegistryCodeStyle CodeStyle => TagCodeGen.STYLE;

        protected override string GetCodeDescription(in TagRegistryRow _Row) => _Row.description;
        #endregion



        #region Packages
        /// <summary>
        /// Add a tag path: missing parents become implicit tags, and an implicit tag at the path becomes explicit
        /// and keeps its ID.
        /// </summary>
        public override RegistryGuid AddRowForOrphan(RegistryData<TagRegistryRow> _Data, string _Name, RegistryGuid _PreferredId, string _Description, out string _Warning)
        {
            _Warning = null;
            if (!TagPath.IsValidPath(_Name, out string error))
            {
                _Warning = $"Tag {_PreferredId} has no valid path (\"{_Name}\"): {error}";
                return RegistryGuid.Zero;
            }

            return TagOperations.AddPath(_Data, _Name, _Description, out _, _PreferredId);
        }

        /// <summary>
        /// Shorter paths first, so referenced parents keep their IDs.
        /// </summary>
        protected override int OrphanOrder(string _Name)
        {
            if (string.IsNullOrEmpty(_Name)) return 0;

            int depth = 0;
            foreach (char c in _Name)
                if (c == TagPath.SEPARATOR)
                    depth++;

            return depth;
        }

        /// <summary>
        /// The tags a package declares in its tag files, parents first.
        /// </summary>
        protected override IReadOnlyList<(string Name, string Description)> GetPackageRows(RegistryPackage _Package, List<string> _Warnings)
        {
            var files = RegistryPackage.AssetPathsUnder(_Package.Root)
                .Where(e => e.EndsWith("." + TagFile.EXTENSION, StringComparison.OrdinalIgnoreCase))
                .Select(e => (e, File.ReadAllText(e)))
                .ToList();

            if (files.Count == 0) return Array.Empty<(string, string)>();

            var parsed = TagFile.Parse(files);
            _Warnings.AddRange(parsed.Errors);

            return parsed.Entries
                .OrderBy(e => OrphanOrder(e.Path)).ThenBy(e => e.Path, StringComparer.Ordinal)
                .Select(e => (e.Path, e.Description))
                .ToList();
        }

        /// <summary>
        /// A kept tag keeps its parents.
        /// </summary>
        protected override void ExpandKeptRows(RegistryData<TagRegistryRow> _Data, HashSet<RegistryGuid> _Keep)
        {
            var parents = new Dictionary<RegistryGuid, RegistryGuid>(_Data.rows.Count);
            foreach (var row in _Data.rows)
                parents[row.id] = row.data.parent;

            foreach (var id in new List<RegistryGuid>(_Keep))
                for (var parent = parents.GetValueOrDefault(id); !parent.IsZero && _Keep.Add(parent); parent = parents.GetValueOrDefault(parent)) { }
        }
        #endregion
    }
}
