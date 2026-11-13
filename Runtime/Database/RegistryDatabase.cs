using System;
using System.Collections.Generic;

using UnityEngine;

namespace LMK.Runtime.Registry
{
    /// <summary>
    /// Read-only lookup data for every row of every loaded registry of one kind. <br/>
    /// Resolves a reference (registry ID, row ID) to a row index, with no name fallback.
    /// </summary>
    /// <remarks>
    /// This base holds what every kind shares: registry records, and each row's ID, name and registry.
    /// A kind's database derives from <see cref="RegistryDatabase{TSelf, TRow}"/>, which adds the kind's own
    /// row data and the active database of the kind. <br/>
    /// Row indices are valid for one database only and must never be serialized. <br/>
    /// Use <see cref="RegistryDatabaseBuilder{TRow}"/> to create one.
    /// </remarks>
    public abstract class RegistryDatabase : ScriptableObject, ISerializationCallbackReceiver
    {
        [Serializable]
        internal struct RegistryRecord
        {
            public RegistryGuid id;
            public string name;
            public RegistryOrigin origin;
            public int firstRow;
            public int rowCount;
        }

        private readonly struct RowKey : IEquatable<RowKey>
        {
            private readonly RegistryGuid registry;
            private readonly RegistryGuid id;

            public RowKey(RegistryGuid _Registry, RegistryGuid _Id)
            {
                registry = _Registry;
                id = _Id;
            }

            public bool Equals(RowKey _Other) => registry.Equals(_Other.registry) && id.Equals(_Other.id);

            public override bool Equals(object _Obj) => _Obj is RowKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(registry, id);
        }



        [SerializeField]
        private RegistryRecord[] registries = Array.Empty<RegistryRecord>();

        [SerializeField]
        private RegistryGuid[] rowIds = Array.Empty<RegistryGuid>();

        [SerializeField]
        private int[] rowRegistries = Array.Empty<int>();

        [SerializeField]
        private string[] rowNames = Array.Empty<string>();

        [SerializeField]
        private string contentHash = string.Empty;

        // Built on first lookup, never during deserialization.
        [NonSerialized] private Dictionary<RowKey, int> rowLookup;
        [NonSerialized] private Dictionary<RegistryGuid, int> registryLookup;
        [NonSerialized] private Dictionary<string, int>[] nameLookups;
        [NonSerialized] private volatile bool lookupsReady;
        [NonSerialized] private bool buildingLookups;
        [NonSerialized] private readonly object lookupLock = new();

        /// <summary>
        /// Hash of the database content. Equal hashes mean equal registries and rows.
        /// </summary>
        public string ContentHash => contentHash ?? string.Empty;

        /// <summary>
        /// Number of rows across all registries. Row indices range from 0 to <c>RowCount - 1</c>.
        /// </summary>
        public int RowCount => rowIds.Length;

        public int RegistryCount => registries.Length;

        /// <summary>
        /// ID of the main registry, or <see cref="RegistryGuid.Zero"/> if none is loaded.
        /// </summary>
        public RegistryGuid MainRegistryId
        {
            get
            {
                foreach (var registry in registries)
                    if (registry.origin == RegistryOrigin.Main)
                        return registry.id;

                return RegistryGuid.Zero;
            }
        }



        /// <summary>
        /// Replace the core content. The kind's row data is set by the derived class, under the same lock.
        /// </summary>
        internal void InitializeCore(RegistryRecord[] _Registries, RegistryGuid[] _RowIds, int[] _RowRegistries,
                                     string[] _RowNames, string _ContentHash, Action _SetKindRows)
        {
            if (_RowIds.Length != _RowRegistries.Length || _RowIds.Length != _RowNames.Length)
                throw new ArgumentException("Row columns must have the same length.");

            lock (lookupLock)
            {
                registries = _Registries ?? Array.Empty<RegistryRecord>();
                rowIds = _RowIds;
                rowRegistries = _RowRegistries;
                rowNames = _RowNames;
                contentHash = _ContentHash ?? string.Empty;
                _SetKindRows?.Invoke();

                ClearLookups();
            }
        }

        private void ClearLookups()
        {
            lookupsReady = false;
            rowLookup = null;
            registryLookup = null;
            nameLookups = null;
        }



        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        // The serialized content may have been replaced (asset reload, CopySerialized): rebuild lookups on next use.
        // Only clears fields, so it is safe on a loading thread.
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            ClearLookups();
        }



        #region Resolution
        /// <summary>
        /// Resolve a reference. Exact: no name check and no fallback.
        /// </summary>
        public RegistryResolution Resolve(RegistryGuid _Registry, RegistryGuid _Id)
        {
            if (_Id.IsZero) return RegistryResolution.None;

            EnsureLookups();

            if (!registryLookup.ContainsKey(_Registry)) return RegistryResolution.MissingRegistry;
            if (!rowLookup.TryGetValue(new RowKey(_Registry, _Id), out int row)) return RegistryResolution.Deleted;

            return RegistryResolution.Live(row);
        }

        /// <summary>
        /// Index of the row named <paramref name="_Name"/> in a registry, or <see cref="RegistryResolution.NO_ROW"/>. <br/>
        /// <b>Debug and console use only:</b> name lookups do not survive renames.
        /// </summary>
        public int FindByName(RegistryGuid _Registry, string _Name)
        {
            if (string.IsNullOrEmpty(_Name)) return RegistryResolution.NO_ROW;

            EnsureLookups();

            if (!registryLookup.TryGetValue(_Registry, out int registry)) return RegistryResolution.NO_ROW;

            return nameLookups[registry].TryGetValue(_Name, out int row) ? row : RegistryResolution.NO_ROW;
        }

        /// <summary>
        /// Build the lookups, and the kind's tables through <see cref="OnLookupsBuilt"/>, if this content has none yet.
        /// Safe to call from any thread. A kind's accessors for its derived tables call it first.
        /// </summary>
        protected void EnsureLookups()
        {
            if (lookupsReady) return;

            lock (lookupLock)
            {
                // The building thread may re-enter through OnLookupsBuilt: the core lookups are already set then.
                if (lookupsReady || buildingLookups) return;

                buildingLookups = true;
                try
                {
                    BuildLookups();
                    OnLookupsBuilt();
                    lookupsReady = true; // set last: other threads only read once everything is built
                }
                finally
                {
                    buildingLookups = false;
                }
            }
        }

        private void BuildLookups()
        {
            var newRegistryLookup = new Dictionary<RegistryGuid, int>(registries.Length);
            var newNameLookups = new Dictionary<string, int>[registries.Length];
            for (int i = 0; i < registries.Length; i++)
            {
                newRegistryLookup[registries[i].id] = i;
                newNameLookups[i] = new Dictionary<string, int>(registries[i].rowCount, StringComparer.Ordinal);
            }

            var newRowLookup = new Dictionary<RowKey, int>(rowIds.Length);
            for (int i = 0; i < rowIds.Length; i++)
            {
                int registry = rowRegistries[i];
                newRowLookup[new RowKey(registries[registry].id, rowIds[i])] = i;
                newNameLookups[registry][rowNames[i] ?? string.Empty] = i;
            }

            registryLookup = newRegistryLookup;
            nameLookups = newNameLookups;
            rowLookup = newRowLookup;
        }

        /// <summary>
        /// Called once per content, under the lookup lock, before any lookup returns. A kind builds its own
        /// derived, non-serialized tables here, and may resolve rows while doing so. <br/>
        /// It may run on a loading thread: no Unity API calls.
        /// </summary>
        protected virtual void OnLookupsBuilt() { }
        #endregion



        #region Rows
        public bool IsRowInRange(int _Row) => _Row >= 0 && _Row < rowIds.Length;

        public RegistryGuid GetRowId(int _Row) => IsRowInRange(_Row) ? rowIds[_Row] : RegistryGuid.Zero;

        /// <summary>
        /// The row's name, unique within its registry, e.g. a tag's path.
        /// </summary>
        public string GetRowName(int _Row) => IsRowInRange(_Row) ? rowNames[_Row] ?? string.Empty : string.Empty;

        /// <summary>
        /// Index of the row's registry, from 0 to <c>RegistryCount - 1</c>, or -1 if out of range.
        /// </summary>
        public int GetRowRegistryIndex(int _Row) => IsRowInRange(_Row) ? rowRegistries[_Row] : -1;

        public RegistryGuid GetRowRegistryId(int _Row) => IsRowInRange(_Row) ? registries[rowRegistries[_Row]].id : RegistryGuid.Zero;
        #endregion



        #region Registries
        public RegistryInfo GetRegistry(int _Index)
        {
            var record = registries[_Index];
            return new RegistryInfo(record.id, record.name, record.origin);
        }

        /// <summary>
        /// First row index and row count of a registry: its rows are contiguous.
        /// </summary>
        public (int firstRow, int rowCount) GetRegistryRows(int _Index)
        {
            var record = registries[_Index];
            return (record.firstRow, record.rowCount);
        }

        /// <summary>
        /// Index of a loaded registry, or -1.
        /// </summary>
        public int FindRegistry(RegistryGuid _Registry)
        {
            EnsureLookups();
            return registryLookup.TryGetValue(_Registry, out int index) ? index : -1;
        }

        /// <summary>
        /// Whether a registry with this ID is loaded.
        /// </summary>
        public bool HasRegistry(RegistryGuid _Registry) => FindRegistry(_Registry) >= 0;

        /// <summary>
        /// Display name of a loaded registry, or empty.
        /// </summary>
        public string GetRegistryName(RegistryGuid _Registry)
        {
            int index = FindRegistry(_Registry);
            return index >= 0 ? registries[index].name : string.Empty;
        }
        #endregion
    }
}
