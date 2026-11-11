using System;
using System.Collections.Generic;

using UnityEngine;

namespace LMK.Runtime.GameplayTags
{
    /// <summary>
    /// Kind of a tag registry.
    /// </summary>
    public enum TagRegistryKind
    {
        /// <summary>
        /// The project's main registry. One per project.
        /// </summary>
        Main,

        /// <summary>
        /// A registry shipped inside a package. Read-only.
        /// </summary>
        Package,

        /// <summary>
        /// A registry generated in the project for a package that ships none.
        /// </summary>
        Generated,
    }



    /// <summary>
    /// Identity and display data of one registry in a <see cref="GameplayTagDatabase"/>.
    /// </summary>
    public readonly struct TagRegistryInfo
    {
        public readonly TagGuid Id;
        public readonly string Name;
        public readonly TagRegistryKind Kind;

        public TagRegistryInfo(TagGuid _Id, string _Name, TagRegistryKind _Kind)
        {
            Id = _Id;
            Name = _Name;
            Kind = _Kind;
        }
    }



    /// <summary>
    /// Read-only lookup and hierarchy data for every live tag of every loaded registry. <br/>
    /// Resolves a reference (registry ID, tag ID) to a <see cref="TagHandle"/>, with no name fallback.
    /// </summary>
    /// <remarks>
    /// In a player, the database is baked at build time, preloaded, and found through
    /// <see cref="Resources"/> as a fallback. In the editor, it is built from the registry files and kept
    /// up to date by the editor tooling. <br/>
    /// Use <see cref="GameplayTagDatabaseBuilder"/> to create one.
    /// </remarks>
    public sealed class GameplayTagDatabase : ScriptableObject, ISerializationCallbackReceiver
    {
        /// <summary>
        /// Name of the baked database asset inside a <c>Resources</c> folder.
        /// </summary>
        public const string RESOURCE_NAME = "GameplayTagDatabase";

        [Serializable]
        internal struct RegistryRecord
        {
            public TagGuid id;
            public string name;
            public TagRegistryKind kind;
            public int firstRow;
            public int rowCount;
        }

        [Serializable]
        internal struct RowRecord
        {
            public TagGuid guid;
            public int registry;
            public int parent;
            public int depth;
            public string segment;
            public string path;
            public bool isImplicit;
        }

        private readonly struct RowKey : IEquatable<RowKey>
        {
            private readonly TagGuid registry;
            private readonly TagGuid id;

            public RowKey(TagGuid _Registry, TagGuid _Id)
            {
                registry = _Registry;
                id = _Id;
            }

            public bool Equals(RowKey _Other) => registry.Equals(_Other.registry) && id.Equals(_Other.id);

            public override bool Equals(object _Obj) => _Obj is RowKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(registry, id);
        }



        #region Active Database
        private static GameplayTagDatabase active;
        private static int generation;
        private static int generationCounter;

        /// <summary>
        /// Called after <see cref="Active"/> changed.
        /// </summary>
        public static event Action ActiveChanged;

#if UNITY_EDITOR
        /// <summary>
        /// Set by the editor tooling: makes sure an editor database is active.
        /// </summary>
        internal static Action EditorEnsureActive;
#endif

        /// <summary>
        /// The database every <see cref="GameplayTag"/> resolves against. <see langword="null"/> until one is loaded.
        /// </summary>
        public static GameplayTagDatabase Active
        {
            get
            {
                if (active == null)
                    LoadDefault();

                return active;
            }
        }

        /// <summary>
        /// Changes every time <see cref="Active"/> changes. 0 means no database is active yet,
        /// and nothing may be cached under it.
        /// </summary>
        public static int Generation
            => Active != null ? generation : 0;

        /// <summary>
        /// Make <paramref name="_Database"/> the active database and start a new generation.
        /// </summary>
        internal static void SetActive(GameplayTagDatabase _Database)
        {
            active = _Database;

            if (_Database == null)
            {
                generation = 0;
            }
            else
            {
                generationCounter = generationCounter == int.MaxValue ? 1 : generationCounter + 1;
                generation = generationCounter;
            }

            ActiveChanged?.Invoke();
        }

        private static void LoadDefault()
        {
#if UNITY_EDITOR
            EditorEnsureActive?.Invoke();
#else
            var database = Resources.Load<GameplayTagDatabase>(RESOURCE_NAME);
            if (database != null)
                SetActive(database);
#endif
        }

        /// <summary>
        /// Resolve a reference against <see cref="Active"/>. Without an active database, any assigned
        /// reference is <see cref="TagStatus.MissingRegistry"/>.
        /// </summary>
        public static TagResolution ResolveActive(TagGuid _Registry, TagGuid _Id)
        {
            if (_Id.IsZero) return TagResolution.None;

            var database = Active;
            return database != null ? database.Resolve(_Registry, _Id) : TagResolution.MissingRegistry;
        }

#if !UNITY_EDITOR
        // The baked database is preloaded, so it becomes active as soon as the player starts.
        private void OnEnable()
        {
            if (active == null)
                SetActive(this);
        }
#endif
        #endregion



        [SerializeField]
        private RegistryRecord[] registries = Array.Empty<RegistryRecord>();

        [SerializeField]
        private RowRecord[] rows = Array.Empty<RowRecord>();

        [SerializeField]
        private string contentHash = string.Empty;

        // Built on first lookup, never during deserialization.
        [NonSerialized] private Dictionary<RowKey, int> rowLookup;
        [NonSerialized] private Dictionary<TagGuid, int> registryLookup;
        [NonSerialized] private Dictionary<string, int>[] pathLookups;
        [NonSerialized] private readonly object lookupLock = new();

        /// <summary>
        /// Hash of the database content. Equal hashes mean equal registries, rows and paths.
        /// </summary>
        public string ContentHash => contentHash ?? string.Empty;

        /// <summary>
        /// Number of live tags across all registries. Handles range from 0 to <c>TagCount - 1</c>.
        /// </summary>
        public int TagCount => rows.Length;

        public int RegistryCount => registries.Length;

        /// <summary>
        /// ID of the main registry, or <see cref="TagGuid.Zero"/> if none is loaded.
        /// </summary>
        public TagGuid MainRegistryId
        {
            get
            {
                foreach (var registry in registries)
                    if (registry.kind == TagRegistryKind.Main)
                        return registry.id;

                return TagGuid.Zero;
            }
        }



        internal void Initialize(RegistryRecord[] _Registries, RowRecord[] _Rows, string _ContentHash)
        {
            lock (lookupLock)
            {
                registries = _Registries ?? Array.Empty<RegistryRecord>();
                rows = _Rows ?? Array.Empty<RowRecord>();
                contentHash = _ContentHash ?? string.Empty;

                rowLookup = null;
                registryLookup = null;
                pathLookups = null;
            }
        }



        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        // The serialized content may have been replaced (asset reload, CopySerialized): rebuild lookups on next use.
        // Only clears fields, so it is safe on a loading thread.
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            rowLookup = null;
            registryLookup = null;
            pathLookups = null;
        }



        #region Resolution
        /// <summary>
        /// Resolve a reference. Exact: no name check and no fallback.
        /// </summary>
        public TagResolution Resolve(TagGuid _Registry, TagGuid _Id)
        {
            if (_Id.IsZero) return TagResolution.None;

            EnsureLookup();

            if (!registryLookup.ContainsKey(_Registry)) return TagResolution.MissingRegistry;
            if (!rowLookup.TryGetValue(new RowKey(_Registry, _Id), out int index)) return TagResolution.Deleted;

            return TagResolution.Live(new TagHandle(index));
        }

        /// <summary>
        /// Find the live tag at <paramref name="_Path"/> in a registry. Path lookups do not survive renames:
        /// use them for debugging and console commands only.
        /// </summary>
        public TagHandle FindByPath(TagGuid _Registry, string _Path)
        {
            if (string.IsNullOrEmpty(_Path)) return TagHandle.None;

            EnsureLookup();

            if (!registryLookup.TryGetValue(_Registry, out int registry)) return TagHandle.None;

            return pathLookups[registry].TryGetValue(_Path, out int index) ? new TagHandle(index) : TagHandle.None;
        }

        private void EnsureLookup()
        {
            if (rowLookup != null) return;

            lock (lookupLock)
            {
                if (rowLookup != null) return;

                var newRegistryLookup = new Dictionary<TagGuid, int>(registries.Length);
                var newPathLookups = new Dictionary<string, int>[registries.Length];
                for (int i = 0; i < registries.Length; i++)
                {
                    newRegistryLookup[registries[i].id] = i;
                    newPathLookups[i] = new Dictionary<string, int>(registries[i].rowCount, StringComparer.Ordinal);
                }

                var newRowLookup = new Dictionary<RowKey, int>(rows.Length);
                for (int i = 0; i < rows.Length; i++)
                {
                    var row = rows[i];
                    newRowLookup[new RowKey(registries[row.registry].id, row.guid)] = i;
                    newPathLookups[row.registry][row.path] = i;
                }

                registryLookup = newRegistryLookup;
                pathLookups = newPathLookups;
                rowLookup = newRowLookup; // assigned last: it marks the lookup as ready
            }
        }
        #endregion



        #region Tag Data
        /// <summary>
        /// Full path of a live tag, e.g. "Status.Debuff.Stun".
        /// </summary>
        public string GetPath(TagHandle _Handle) => IsInRange(_Handle) ? rows[_Handle.Index].path : string.Empty;

        /// <summary>
        /// Last segment of a live tag's path, e.g. "Stun".
        /// </summary>
        public string GetSegment(TagHandle _Handle) => IsInRange(_Handle) ? rows[_Handle.Index].segment : string.Empty;

        /// <summary>
        /// Parent of a live tag, or <see cref="TagHandle.None"/> for a top level tag.
        /// </summary>
        public TagHandle GetParent(TagHandle _Handle) => IsInRange(_Handle) ? new TagHandle(rows[_Handle.Index].parent) : TagHandle.None;

        /// <summary>
        /// Number of ancestors: 0 for a top level tag.
        /// </summary>
        public int GetDepth(TagHandle _Handle) => IsInRange(_Handle) ? rows[_Handle.Index].depth : -1;

        /// <summary>
        /// Whether the tag exists only as the parent of other tags.
        /// </summary>
        public bool IsImplicit(TagHandle _Handle) => IsInRange(_Handle) && rows[_Handle.Index].isImplicit;

        public TagGuid GetId(TagHandle _Handle) => IsInRange(_Handle) ? rows[_Handle.Index].guid : TagGuid.Zero;

        public TagGuid GetRegistryId(TagHandle _Handle) => IsInRange(_Handle) ? registries[rows[_Handle.Index].registry].id : TagGuid.Zero;

        /// <summary>
        /// A reference to a live tag, as an assignment would write it.
        /// </summary>
        public GameplayTag GetTag(TagHandle _Handle)
        {
            if (!IsInRange(_Handle)) return GameplayTag.None;

            var row = rows[_Handle.Index];
            return GameplayTag.FromId(registries[row.registry].id, row.guid, row.path);
        }

        /// <summary>
        /// Whether <paramref name="_Handle"/> is <paramref name="_Ancestor"/> or one of its descendants.
        /// Never true across registries.
        /// </summary>
        public bool IsSameOrDescendantOf(TagHandle _Handle, TagHandle _Ancestor)
        {
            if (!IsInRange(_Handle) || !IsInRange(_Ancestor)) return false;

            int ancestorDepth = rows[_Ancestor.Index].depth;
            for (int index = _Handle.Index; index >= 0 && rows[index].depth >= ancestorDepth; index = rows[index].parent)
                if (index == _Ancestor.Index)
                    return true;

            return false;
        }

        /// <summary>
        /// Add the direct children of a live tag to <paramref name="_Result"/>.
        /// </summary>
        public void GetChildren(TagHandle _Handle, List<TagHandle> _Result)
        {
            if (!IsInRange(_Handle) || _Result == null) return;

            var registry = registries[rows[_Handle.Index].registry];
            for (int i = _Handle.Index + 1; i < registry.firstRow + registry.rowCount; i++)
                if (rows[i].parent == _Handle.Index)
                    _Result.Add(new TagHandle(i));
        }

        private bool IsInRange(TagHandle _Handle) => _Handle.Index >= 0 && _Handle.Index < rows.Length;
        #endregion



        #region Registries
        public TagRegistryInfo GetRegistry(int _Index)
        {
            var record = registries[_Index];
            return new TagRegistryInfo(record.id, record.name, record.kind);
        }

        /// <summary>
        /// Whether a registry with this ID is loaded.
        /// </summary>
        public bool HasRegistry(TagGuid _Registry)
        {
            EnsureLookup();
            return registryLookup.ContainsKey(_Registry);
        }

        /// <summary>
        /// Display name of a loaded registry, or empty.
        /// </summary>
        public string GetRegistryName(TagGuid _Registry)
        {
            EnsureLookup();
            return registryLookup.TryGetValue(_Registry, out int index) ? registries[index].name : string.Empty;
        }
        #endregion



        #region Diagnostics
        private static readonly HashSet<(TagGuid, TagGuid)> reportedUnresolved = new();

        /// <summary>
        /// Log one warning per unresolved reference, in Play Mode and in development builds.
        /// </summary>
        internal static void ReportUnresolved(in GameplayTag _Tag, TagStatus _Status)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Application.isPlaying) return;
            if (!reportedUnresolved.Add((_Tag.RegistryId, _Tag.Id))) return;

            Debug.LogWarning(
                $"Gameplay tag \"{_Tag.LastKnownName}\" does not resolve ({_Status}, registry {_Tag.RegistryId}, id {_Tag.Id}). " +
                "It matches nothing.");
#endif
        }
        #endregion
    }
}
