using System.Collections.Generic;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry.Tests
{
    /// <summary>
    /// A test registry kind with tag-shaped rows. Rows are named by their path; the runtime database is a
    /// <see cref="TestDatabase"/> holding the rows' names.
    /// </summary>
    public abstract class TreeKindBase<TAsset> : RegistryKindEditor<TreeRow, TAsset> where TAsset : RegistryAsset<TreeRow>
    {
        private readonly string id;
        private readonly string extension;

        protected TreeKindBase(string _Id, string _Extension)
        {
            id = _Id;
            extension = _Extension;
        }

        public override string Id => id;
        public override string DisplayName => $"Test {id}";
        public override string FileExtension => extension;
        public override string RootFolder => "Assets/__RegistryCoreTests";
        protected override string MainRegistryFileName => $"Main_{id}";

        public override string FormatLine => TreeFormat.Instance.FormatLine;
        public override string RowNoun => TreeFormat.Instance.RowNoun;
        public override string EditorName => "Test Registry Editor";
        public override IReadOnlyList<RegistryColumn> Columns => TreeFormat.Instance.Columns;

        public override bool TryParseRow(string[] _Cells, out TreeRow _Row, out string _Error)
            => TreeFormat.Instance.TryParseRow(_Cells, out _Row, out _Error);

        public override string[] FormatRow(in TreeRow _Row) => TreeFormat.Instance.FormatRow(_Row);

        public override void Validate(RegistryData<TreeRow> _Data, List<string> _Errors) => TreeFormat.Instance.Validate(_Data, _Errors);

        public override IReadOnlyDictionary<RegistryGuid, string> GetNames(RegistryData<TreeRow> _Data)
        {
            var rows = new Dictionary<RegistryGuid, TreeRow>();
            foreach (var row in _Data.rows)
                rows[row.id] = row.data;

            var names = new Dictionary<RegistryGuid, string>();
            foreach (var id in rows.Keys)
            {
                var segments = new List<string>();
                var current = id;
                for (int steps = 0; !current.IsZero; steps++)
                {
                    if (steps > rows.Count || !rows.TryGetValue(current, out var row))
                    {
                        segments = null;
                        break;
                    }

                    segments.Add(row.segment);
                    current = row.parent;
                }

                if (segments == null) continue;

                segments.Reverse();
                names[id] = string.Join(".", segments);
            }

            return names;
        }

        protected override RegistryDatabase BuildDatabase(RegistrySnapshot<TreeRow> _Snapshot)
        {
            var builder = TestDatabase.NewBuilder();
            foreach (var registry in _Snapshot.Registries)
            {
                var rows = new List<RegistryDatabaseBuilder<TestRow>.Row>();
                foreach (var id in registry.Ids)
                {
                    string name = registry.GetName(id);
                    if (name != null)
                        rows.Add(new RegistryDatabaseBuilder<TestRow>.Row(id, name, new TestRow(0)));
                }

                builder.AddRegistry(registry.Id, registry.Name, registry.Origin, rows);
            }

            return builder.Build<TestDatabase>();
        }

        protected override void PublishDatabase(RegistryDatabase _Database, RegistryDatabase _Previous)
            => RegistryEditorDatabase.Publish<TestDatabase, TestRow>(_Database, _Previous);

        /// <summary>
        /// The kind's generated code style. None by default: tests that check code generation set one.
        /// </summary>
        public RegistryCodeStyle code;

        public override RegistryCodeStyle CodeStyle => code;

        protected override string GetCodeDescription(in TreeRow _Row) => _Row.description;

        /// <summary>
        /// How many times generated files would have been written.
        /// </summary>
        public int savedCount;

        protected override void OnSaved() => savedCount++;



        #region Packages
        /// <summary>
        /// Add a path like gameplay tags do: missing parents become implicit rows, and an implicit row at the path
        /// becomes explicit and keeps its ID.
        /// </summary>
        public override RegistryGuid AddRowForOrphan(RegistryData<TreeRow> _Data, string _Name, RegistryGuid _PreferredId, string _Description, out string _Warning)
        {
            _Warning = null;
            if (!IsValidPath(_Name))
            {
                _Warning = $"Tag {_PreferredId} has no valid name (\"{_Name}\"), so it can't be added.";
                return RegistryGuid.Zero;
            }

            string[] segments = _Name.Split('.');
            var parent = RegistryGuid.Zero;

            for (int i = 0; i < segments.Length; i++)
            {
                bool isLast = i == segments.Length - 1;
                int index = _Data.rows.FindIndex(e => e.data.parent == parent && e.data.segment == segments[i]);

                if (index >= 0)
                {
                    var row = _Data.rows[index];
                    if (isLast)
                    {
                        if (!row.data.isImplicit) return row.id;

                        row.data.isImplicit = false;
                        if (_Description != null) row.data.description = _Description;
                        _Data.rows[index] = row;
                    }

                    parent = row.id;
                    continue;
                }

                var id = isLast && !_PreferredId.IsZero && _Data.IndexOf(_PreferredId) < 0 ? _PreferredId : RegistryGuid.NewGuid();
                _Data.rows.Add(new RegistryRow<TreeRow>(id, new TreeRow(parent, segments[i], !isLast, isLast ? _Description : null)));
                parent = id;
            }

            return parent;
        }

        protected override int OrphanOrder(string _Name) => string.IsNullOrEmpty(_Name) ? 0 : _Name.Split('.').Length;

        protected override void ExpandKeptRows(RegistryData<TreeRow> _Data, HashSet<RegistryGuid> _Keep)
        {
            var parents = new Dictionary<RegistryGuid, RegistryGuid>();
            foreach (var row in _Data.rows)
                parents[row.id] = row.data.parent;

            foreach (var id in new List<RegistryGuid>(_Keep))
                for (var parent = parents.GetValueOrDefault(id); !parent.IsZero && _Keep.Add(parent); parent = parents.GetValueOrDefault(parent)) { }
        }

        private static bool IsValidPath(string _Path)
        {
            if (string.IsNullOrEmpty(_Path)) return false;

            foreach (string segment in _Path.Split('.'))
            {
                if (segment.Length == 0) return false;
                foreach (char c in segment)
                    if (!char.IsLetterOrDigit(c) && c != '_')
                        return false;
            }

            return true;
        }
        #endregion
    }



    public sealed class TreeKind : TreeKindBase<TreeRegistryAsset>
    {
        public TreeKind() : base("tree", TreeFormat.EXTENSION) { }

        public override System.Type ReferenceType => typeof(TreeRef);
    }



    public sealed class TreeKindB : TreeKindBase<TreeRegistryAssetB>
    {
        public const string EXTENSION = "treetestregistryb";

        public TreeKindB() : base("treeb", EXTENSION) { }

        public override System.Type ReferenceType => typeof(TreeRefB);
    }
}
