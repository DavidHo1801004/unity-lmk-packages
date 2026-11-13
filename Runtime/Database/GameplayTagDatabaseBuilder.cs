using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using LMK.Runtime.Registry;

namespace LMK.Runtime.GameplayTags
{
    /// <summary>
    /// Builds a <see cref="GameplayTagDatabase"/> from registry rows. <br/>
    /// Rows whose parent is missing, or that are part of a parent cycle, are skipped with their descendants.
    /// Registry files are validated before they reach the builder, so this only guards against bad input.
    /// </summary>
    /// <remarks>
    /// Turns tag rows into registry rows named by their path, and builds them with a
    /// <see cref="RegistryDatabaseBuilder{TRow}"/>: registries are ordered by ID and rows by path. A path sorts
    /// before its descendants, so every parent gets a lower index than its children.
    /// </remarks>
    public sealed class GameplayTagDatabaseBuilder
    {
        /// <summary>
        /// One tag row of a registry.
        /// </summary>
        public readonly struct Row
        {
            public readonly RegistryGuid Id;

            /// <summary>
            /// Parent row ID, or <see cref="RegistryGuid.Zero"/> for a top level tag.
            /// </summary>
            public readonly RegistryGuid Parent;

            public readonly string Segment;

            public readonly bool IsImplicit;

            public Row(RegistryGuid _Id, RegistryGuid _Parent, string _Segment, bool _IsImplicit = false)
            {
                Id = _Id;
                Parent = _Parent;
                Segment = _Segment;
                IsImplicit = _IsImplicit;
            }
        }

        private readonly RegistryDatabaseBuilder<TagRow> builder = new(AppendHash) { Link = LinkParent };



        /// <summary>
        /// Add a registry and its rows.
        /// </summary>
        /// <exception cref="ArgumentException"> If the registry ID is zero or already added. </exception>
        public GameplayTagDatabaseBuilder AddRegistry(RegistryGuid _Id, string _Name, TagRegistryKind _Kind, IEnumerable<Row> _Rows)
        {
            var rows = _Rows != null ? new List<Row>(_Rows) : new List<Row>();
            builder.AddRegistry(_Id, _Name, (RegistryOrigin)_Kind, ToRegistryRows(rows));

            return this;
        }

        /// <summary>
        /// Create a new database instance.
        /// </summary>
        public GameplayTagDatabase Build() => builder.Build<GameplayTagDatabase>();

        /// <summary>
        /// Replace the content of an existing database instance.
        /// </summary>
        public void BuildInto(GameplayTagDatabase _Database) => builder.BuildInto(_Database);



        private static List<RegistryDatabaseBuilder<TagRow>.Row> ToRegistryRows(List<Row> _Rows)
        {
            var byId = ValidRowsById(_Rows);
            var paths = ComputePaths(byId);
            var result = new List<RegistryDatabaseBuilder<TagRow>.Row>(paths.Count);

            foreach (var (id, path) in paths)
            {
                var row = byId[id];

                int depth = 0;
                foreach (char c in path)
                    if (c == TagPath.SEPARATOR)
                        depth++;

                result.Add(new RegistryDatabaseBuilder<TagRow>.Row(id, path, new TagRow
                {
                    parentId = row.Parent,
                    depth = depth,
                    segment = row.Segment,
                    isImplicit = row.IsImplicit,
                }));
            }

            return result;
        }

        private static Dictionary<RegistryGuid, Row> ValidRowsById(List<Row> _Rows)
        {
            var byId = new Dictionary<RegistryGuid, Row>(_Rows.Count);
            foreach (var row in _Rows)
                if (!row.Id.IsZero && TagPath.IsValidSegment(row.Segment, out _))
                    byId[row.Id] = row; // duplicates: the last one wins, the file parser rejects them first

            return byId;
        }

        private static TagRow LinkParent(TagRow _Row, in RegistryDatabaseBuilder<TagRow>.LinkContext _Context)
        {
            _Row.parent = _Row.parentId.IsZero ? -1 : _Context.IndexOf(_Row.parentId);
            _Row.parentId = RegistryGuid.Zero;
            return _Row;
        }

        // The core hashes registries, IDs, registry indices and paths.
        private static void AppendHash(TagRow _Row, StringBuilder _Builder)
            => _Builder.Append(_Row.parent.ToString(CultureInfo.InvariantCulture)).Append('|').Append(_Row.isImplicit ? '1' : '0');

        /// <summary>
        /// Full path of every row that reaches a root through existing parents, without cycles.
        /// </summary>
        private static Dictionary<RegistryGuid, string> ComputePaths(Dictionary<RegistryGuid, Row> _ById)
        {
            var byId = _ById;
            var paths = new Dictionary<RegistryGuid, string>(byId.Count);
            var invalid = new HashSet<RegistryGuid>();
            var chain = new List<RegistryGuid>();

            foreach (var start in byId.Keys)
            {
                if (paths.ContainsKey(start) || invalid.Contains(start)) continue;

                // Walk up until a known path, a root, a missing parent or a cycle.
                chain.Clear();
                var visiting = new HashSet<RegistryGuid>();
                RegistryGuid current = start;
                string basePath = null;
                bool valid = true;

                while (true)
                {
                    if (paths.TryGetValue(current, out basePath)) break;
                    if (invalid.Contains(current) || !byId.TryGetValue(current, out var row) || !visiting.Add(current))
                    {
                        valid = false;
                        break;
                    }

                    chain.Add(current);
                    if (row.Parent.IsZero)
                    {
                        basePath = string.Empty;
                        break;
                    }

                    current = row.Parent;
                }

                if (!valid)
                {
                    invalid.UnionWith(chain);
                    continue;
                }

                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    basePath = TagPath.Combine(basePath, byId[chain[i]].Segment);
                    paths[chain[i]] = basePath;
                }
            }

            return paths;
        }
    }
}
