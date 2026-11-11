using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;

namespace LMK.Runtime.GameplayTags
{
    /// <summary>
    /// Builds a <see cref="GameplayTagDatabase"/> from registry rows. <br/>
    /// Rows whose parent is missing, or that are part of a parent cycle, are skipped with their descendants.
    /// Registry files are validated before they reach the builder, so this only guards against bad input.
    /// </summary>
    public sealed class GameplayTagDatabaseBuilder
    {
        /// <summary>
        /// One tag row of a registry.
        /// </summary>
        public readonly struct Row
        {
            public readonly TagGuid Id;

            /// <summary>
            /// Parent row ID, or <see cref="TagGuid.Zero"/> for a top level tag.
            /// </summary>
            public readonly TagGuid Parent;

            public readonly string Segment;

            public readonly bool IsImplicit;

            public Row(TagGuid _Id, TagGuid _Parent, string _Segment, bool _IsImplicit = false)
            {
                Id = _Id;
                Parent = _Parent;
                Segment = _Segment;
                IsImplicit = _IsImplicit;
            }
        }

        private sealed class RegistryInput
        {
            public TagGuid id;
            public string name;
            public TagRegistryKind kind;
            public List<Row> rows;
        }

        private readonly List<RegistryInput> registries = new();



        /// <summary>
        /// Add a registry and its rows.
        /// </summary>
        /// <exception cref="ArgumentException"> If the registry ID is zero or already added. </exception>
        public GameplayTagDatabaseBuilder AddRegistry(TagGuid _Id, string _Name, TagRegistryKind _Kind, IEnumerable<Row> _Rows)
        {
            if (_Id.IsZero) throw new ArgumentException("Registry ID can not be zero.", nameof(_Id));
            if (registries.Exists(e => e.id.Equals(_Id))) throw new ArgumentException($"Registry {_Id} is already added.", nameof(_Id));

            registries.Add(new RegistryInput
            {
                id = _Id,
                name = _Name ?? string.Empty,
                kind = _Kind,
                rows = _Rows != null ? new List<Row>(_Rows) : new List<Row>(),
            });

            return this;
        }

        /// <summary>
        /// Create a new database instance.
        /// </summary>
        public GameplayTagDatabase Build()
        {
            var database = ScriptableObject.CreateInstance<GameplayTagDatabase>();
            database.name = GameplayTagDatabase.RESOURCE_NAME;
            BuildInto(database);

            return database;
        }

        /// <summary>
        /// Replace the content of an existing database instance.
        /// </summary>
        public void BuildInto(GameplayTagDatabase _Database)
        {
            if (_Database == null) throw new ArgumentNullException(nameof(_Database));

            // Deterministic order: registries by ID, rows by path. A path sorts before its descendants,
            // so every parent gets a lower index than its children.
            var sortedRegistries = new List<RegistryInput>(registries);
            sortedRegistries.Sort((a, b) => a.id.CompareTo(b.id));

            var registryRecords = new GameplayTagDatabase.RegistryRecord[sortedRegistries.Count];
            var rowRecords = new List<GameplayTagDatabase.RowRecord>();

            for (int registryIndex = 0; registryIndex < sortedRegistries.Count; registryIndex++)
            {
                var registry = sortedRegistries[registryIndex];
                var paths = ComputePaths(registry.rows);

                var validRows = new List<(Row row, string path)>(paths.Count);
                foreach (var row in registry.rows)
                    if (paths.TryGetValue(row.Id, out string path))
                        validRows.Add((row, path));

                validRows.Sort((a, b) => string.CompareOrdinal(a.path, b.path));

                int firstRow = rowRecords.Count;
                var indexById = new Dictionary<TagGuid, int>(validRows.Count);

                foreach (var (row, path) in validRows)
                {
                    int index = rowRecords.Count;
                    indexById[row.Id] = index;

                    int depth = 0;
                    foreach (char c in path)
                        if (c == TagPath.SEPARATOR)
                            depth++;

                    rowRecords.Add(new GameplayTagDatabase.RowRecord
                    {
                        guid = row.Id,
                        registry = registryIndex,
                        parent = row.Parent.IsZero ? -1 : indexById[row.Parent],
                        depth = depth,
                        segment = row.Segment,
                        path = path,
                        isImplicit = row.IsImplicit,
                    });
                }

                registryRecords[registryIndex] = new GameplayTagDatabase.RegistryRecord
                {
                    id = registry.id,
                    name = registry.name,
                    kind = registry.kind,
                    firstRow = firstRow,
                    rowCount = rowRecords.Count - firstRow,
                };
            }

            var rowArray = rowRecords.ToArray();
            _Database.Initialize(registryRecords, rowArray, ComputeHash(registryRecords, rowArray));
        }



        /// <summary>
        /// Full path of every row that reaches a root through existing parents, without cycles.
        /// </summary>
        private static Dictionary<TagGuid, string> ComputePaths(List<Row> _Rows)
        {
            var byId = new Dictionary<TagGuid, Row>(_Rows.Count);
            foreach (var row in _Rows)
                if (!row.Id.IsZero && TagPath.IsValidSegment(row.Segment, out _))
                    byId[row.Id] = row; // duplicates: the last one wins, the file parser rejects them first

            var paths = new Dictionary<TagGuid, string>(byId.Count);
            var invalid = new HashSet<TagGuid>();
            var chain = new List<TagGuid>();

            foreach (var start in byId.Keys)
            {
                if (paths.ContainsKey(start) || invalid.Contains(start)) continue;

                // Walk up until a known path, a root, a missing parent or a cycle.
                chain.Clear();
                var visiting = new HashSet<TagGuid>();
                TagGuid current = start;
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

        private static string ComputeHash(GameplayTagDatabase.RegistryRecord[] _Registries, GameplayTagDatabase.RowRecord[] _Rows)
        {
            var builder = new StringBuilder();
            foreach (var registry in _Registries)
                builder.Append('R').Append(registry.id).Append('|').Append(registry.name).Append('|').Append((int)registry.kind).Append('\n');

            foreach (var row in _Rows)
                builder.Append(row.guid).Append('|').Append(row.registry).Append('|').Append(row.parent).Append('|')
                       .Append(row.path).Append('|').Append(row.isImplicit ? '1' : '0').Append('\n');

            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));

            var hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                hex.Append(b.ToString("x2"));

            return hex.ToString();
        }
    }
}
