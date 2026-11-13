using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;

namespace LMK.Runtime.Registry
{
    /// <summary>
    /// Builds a kind's database from registry rows, deterministically. <br/>
    /// Registries are ordered by ID and rows by name (ordinal), whatever the input order. Rows with a zero ID,
    /// and repeats of an ID already added to the same registry, are skipped: registry files are validated before
    /// they reach the builder, so this only guards against bad input.
    /// </summary>
    /// <typeparam name="TRow"> The kind's row data. </typeparam>
    public sealed class RegistryDatabaseBuilder<TRow> where TRow : struct
    {
        /// <summary>
        /// One row of a registry: its ID, its name (unique in the registry) and the kind's data.
        /// </summary>
        public readonly struct Row
        {
            public readonly RegistryGuid Id;
            public readonly string Name;
            public readonly TRow Data;

            public Row(RegistryGuid _Id, string _Name, TRow _Data)
            {
                Id = _Id;
                Name = _Name ?? string.Empty;
                Data = _Data;
            }
        }

        /// <summary>
        /// What a <see cref="Linker"/> sees of the row it rewrites, once every row has its final index.
        /// </summary>
        public readonly struct LinkContext
        {
            private readonly Dictionary<RegistryGuid, int> indexById;

            /// <summary>
            /// The row's final index in the database.
            /// </summary>
            public readonly int RowIndex;

            /// <summary>
            /// The row's registry, as an index into the database's registries.
            /// </summary>
            public readonly int RegistryIndex;

            public readonly RegistryGuid RowId;

            internal LinkContext(int _RowIndex, int _RegistryIndex, RegistryGuid _RowId, Dictionary<RegistryGuid, int> _IndexById)
            {
                RowIndex = _RowIndex;
                RegistryIndex = _RegistryIndex;
                RowId = _RowId;
                indexById = _IndexById;
            }

            /// <summary>
            /// Final index of a row of the same registry, or <see cref="RegistryResolution.NO_ROW"/>.
            /// </summary>
            public int IndexOf(RegistryGuid _RowId)
                => !_RowId.IsZero && indexById.TryGetValue(_RowId, out int index) ? index : RegistryResolution.NO_ROW;
        }

        /// <summary>
        /// Rewrites a row's data once indices are final, e.g. to turn a parent's ID into its index.
        /// </summary>
        public delegate TRow Linker(TRow _Data, in LinkContext _Context);

        private sealed class RegistryInput
        {
            public RegistryGuid id;
            public string name;
            public RegistryOrigin origin;
            public List<Row> rows;
        }

        private readonly List<RegistryInput> registries = new();
        private readonly Action<TRow, StringBuilder> appendHash;

        /// <summary>
        /// Optional rewrite of every row's data once indices are final.
        /// </summary>
        public Linker Link { get; set; }



        /// <param name="_AppendHash">
        /// Writes the parts of a row's data that define the content, for <see cref="RegistryDatabase.ContentHash"/>.
        /// The core already hashes registries, row IDs, names and order. Format numbers with the invariant
        /// culture, so the hash is the same on every machine.
        /// </param>
        public RegistryDatabaseBuilder(Action<TRow, StringBuilder> _AppendHash)
        {
            appendHash = _AppendHash ?? throw new ArgumentNullException(nameof(_AppendHash));
        }

        /// <summary>
        /// Add a registry and its rows.
        /// </summary>
        /// <exception cref="ArgumentException"> If the registry ID is zero or already added. </exception>
        public RegistryDatabaseBuilder<TRow> AddRegistry(RegistryGuid _Id, string _Name, RegistryOrigin _Origin, IEnumerable<Row> _Rows)
        {
            if (_Id.IsZero) throw new ArgumentException("Registry ID can not be zero.", nameof(_Id));
            if (registries.Exists(e => e.id.Equals(_Id))) throw new ArgumentException($"Registry {_Id} is already added.", nameof(_Id));

            registries.Add(new RegistryInput
            {
                id = _Id,
                name = _Name ?? string.Empty,
                origin = _Origin,
                rows = _Rows != null ? new List<Row>(_Rows) : new List<Row>(),
            });

            return this;
        }

        /// <summary>
        /// Create a new database instance, named after its type.
        /// </summary>
        public TDatabase Build<TDatabase>() where TDatabase : RegistryDatabase<TDatabase, TRow>
        {
            var database = ScriptableObject.CreateInstance<TDatabase>();
            database.name = RegistryDatabase<TDatabase, TRow>.ResourceName;
            BuildInto(database);

            return database;
        }

        /// <summary>
        /// Replace the content of an existing database instance.
        /// </summary>
        public void BuildInto<TDatabase>(TDatabase _Database) where TDatabase : RegistryDatabase<TDatabase, TRow>
        {
            if (_Database == null) throw new ArgumentNullException(nameof(_Database));

            var sortedRegistries = new List<RegistryInput>(registries);
            sortedRegistries.Sort((a, b) => a.id.CompareTo(b.id));

            var registryRecords = new RegistryDatabase.RegistryRecord[sortedRegistries.Count];
            var ids = new List<RegistryGuid>();
            var registryOfRow = new List<int>();
            var names = new List<string>();
            var data = new List<TRow>();

            for (int registryIndex = 0; registryIndex < sortedRegistries.Count; registryIndex++)
            {
                var registry = sortedRegistries[registryIndex];

                var seen = new HashSet<RegistryGuid>();
                var validRows = new List<Row>(registry.rows.Count);
                foreach (var row in registry.rows)
                    if (!row.Id.IsZero && seen.Add(row.Id))
                        validRows.Add(row);

                // Ties on name are ordered by ID, so the order never depends on the input order.
                validRows.Sort((a, b) =>
                {
                    int compare = string.CompareOrdinal(a.Name, b.Name);
                    return compare != 0 ? compare : a.Id.CompareTo(b.Id);
                });

                int firstRow = ids.Count;
                var indexById = new Dictionary<RegistryGuid, int>(validRows.Count);
                for (int i = 0; i < validRows.Count; i++)
                    indexById[validRows[i].Id] = firstRow + i;

                foreach (var row in validRows)
                {
                    int index = ids.Count;
                    var rowData = Link != null ? Link(row.Data, new LinkContext(index, registryIndex, row.Id, indexById)) : row.Data;

                    ids.Add(row.Id);
                    registryOfRow.Add(registryIndex);
                    names.Add(row.Name);
                    data.Add(rowData);
                }

                registryRecords[registryIndex] = new RegistryDatabase.RegistryRecord
                {
                    id = registry.id,
                    name = registry.name,
                    origin = registry.origin,
                    firstRow = firstRow,
                    rowCount = ids.Count - firstRow,
                };
            }

            var idArray = ids.ToArray();
            var nameArray = names.ToArray();
            var dataArray = data.ToArray();
            string hash = ComputeHash(registryRecords, idArray, registryOfRow, nameArray, dataArray);

            _Database.Initialize(registryRecords, idArray, registryOfRow.ToArray(), nameArray, dataArray, hash);
        }



        private string ComputeHash(RegistryDatabase.RegistryRecord[] _Registries, RegistryGuid[] _Ids, List<int> _RegistryOfRow,
                                   string[] _Names, TRow[] _Data)
        {
            var builder = new StringBuilder();
            foreach (var registry in _Registries)
                builder.Append('R').Append(registry.id).Append('|').Append(registry.name).Append('|').Append((int)registry.origin).Append('\n');

            for (int i = 0; i < _Ids.Length; i++)
            {
                builder.Append(_Ids[i]).Append('|').Append(_RegistryOfRow[i]).Append('|').Append(_Names[i]).Append('|');
                appendHash(_Data[i], builder);
                builder.Append('\n');
            }

            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));

            var hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                hex.Append(b.ToString("x2"));

            return hex.ToString();
        }
    }
}
