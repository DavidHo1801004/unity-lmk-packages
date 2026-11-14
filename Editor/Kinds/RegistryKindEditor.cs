using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using UnityEditor;
using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// What a domain package writes to add a registry kind: its row format and rules, how rows are named, and
    /// how its runtime database is built. The core does the rest: loading every registry file of the kind,
    /// snapshots and the editor database, staged editing with Undo, locking, saving and markers.
    /// </summary>
    /// <typeparam name="TRow"> The kind's file row: every column after <c>guid</c>. Keep it value-like. </typeparam>
    /// <typeparam name="TAsset"> The kind's imported registry asset. </typeparam>
    /// <remarks>
    /// Edits go through <see cref="Apply"/> (or <see cref="RegistryEdit.Apply{TRow, TAsset}"/>): a staged
    /// transaction that checks the lock, changes a copy, validates it, records Undo and republishes. Only the
    /// project's main registry is editable; package and generated registries are read-only here. <br/>
    /// Undo and redo only change the staged state. If the main registry changes on disk while edits are staged
    /// (a pull, a revert), the edits are kept and saving is refused until they are discarded, so staged content
    /// never overwrites newer content. Without staged edits, the state follows the file and its undo history is
    /// dropped.
    /// </remarks>
    public abstract class RegistryKindEditor<TRow, TAsset> : RegistryKind, IRegistryFormat<TRow>
        where TRow : struct
        where TAsset : RegistryAsset<TRow>
    {
        private static readonly string[] SEARCH_FOLDERS = { "Assets", "Packages" };

        #region Format
        public abstract string FormatLine { get; }
        public abstract IReadOnlyList<RegistryColumn> Columns { get; }
        public abstract bool TryParseRow(string[] _Cells, out TRow _Row, out string _Error);
        public abstract string[] FormatRow(in TRow _Row);
        public abstract void Validate(RegistryData<TRow> _Data, List<string> _Errors);

        /// <summary>
        /// The name of every row that has one, e.g. a tag's full path. Unique within a registry.
        /// </summary>
        public abstract IReadOnlyDictionary<RegistryGuid, string> GetNames(RegistryData<TRow> _Data);
        #endregion



        #region Database
        /// <summary>
        /// Build the kind's runtime database from a snapshot: one registry per loaded registry, with its rows.
        /// </summary>
        protected abstract RegistryDatabase BuildDatabase(RegistrySnapshot<TRow> _Snapshot);

        /// <summary>
        /// Make a freshly built editor database active, in place of the previous one. Implement it with
        /// <see cref="RegistryEditorDatabase.Publish{TDatabase, TRow}"/>.
        /// </summary>
        protected abstract void PublishDatabase(RegistryDatabase _Database, RegistryDatabase _Previous);

        /// <summary>
        /// Build a runtime database from the saved registries only, for builds. The caller owns it.
        /// </summary>
        public override RegistryDatabase BuildSavedDatabase() => BuildDatabase(SavedSnapshot);
        #endregion



        #region Loading
        private RegistrySnapshot<TRow> snapshot;
        private RegistrySnapshot<TRow> savedSnapshot;
        private List<RegistrySnapshot<TRow>.Source> savedSources;
        private RegistryDatabase editorDatabase;
        private bool reloading;

        // Last registry content that loaded, per file, used while a file is rejected.
        private readonly Dictionary<string, RegistrySnapshot<TRow>.Source> lastGood = new();

        /// <summary>
        /// The current snapshot, with the main registry's staged edits. Loads the registries on first access.
        /// </summary>
        public RegistrySnapshot<TRow> Snapshot
        {
            get
            {
                EnsureLoaded();
                return Volatile.Read(ref snapshot) ?? RegistrySnapshot<TRow>.Empty(GetNames);
            }
        }

        /// <summary>
        /// The registries as saved on disk, without staged edits. The same as <see cref="Snapshot"/> when nothing
        /// is staged. Builds and code generation use it.
        /// </summary>
        public RegistrySnapshot<TRow> SavedSnapshot
        {
            get
            {
                var current = Snapshot;
                if (savedSources == null) return current;

                return savedSnapshot ??= RegistrySnapshot<TRow>.Build(savedSources, GetNames, MainRegistryPath);
            }
        }

        /// <summary>
        /// Why generated registries are not loaded: one message per registry whose package isn't installed.
        /// </summary>
        public IReadOnlyList<string> InactiveRegistries { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// IDs of the generated registries that are not loaded because their package isn't installed.
        /// </summary>
        public IReadOnlyCollection<RegistryGuid> InactiveRegistryIds { get; private set; } = Array.Empty<RegistryGuid>();

        /// <summary>
        /// The editor database built from the current snapshot.
        /// </summary>
        public RegistryDatabase EditorDatabase
        {
            get
            {
                EnsureLoaded();
                return editorDatabase;
            }
        }

        public void EnsureLoaded()
        {
            if (Volatile.Read(ref snapshot) == null)
                Reload();
        }

        public override void Reload()
        {
            if (reloading) return;
            reloading = true;

            try
            {
                var sources = new List<RegistrySnapshot<TRow>.Source>();
                var paths = new HashSet<string>();

                foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(TAsset).Name}", SEARCH_FOLDERS))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!IsRegistryPath(path) || !paths.Add(path)) continue;

                    var asset = AssetDatabase.LoadAssetAtPath<TAsset>(path);
                    if (asset == null) continue;

                    var source = CreateSource(path, asset);
                    if (source != null)
                        sources.Add(source);
                }

                // Forget files that no longer exist.
                foreach (string path in lastGood.Keys.Where(e => !paths.Contains(e)).ToList())
                    lastGood.Remove(path);

                // Generated registries load only while their package is installed.
                var installed = RegistryPackages.GetInstalled();
                var inactive = sources.Where(e => !RegistryPackages.IsActive(e.Data.origin, e.Data.package, installed)).ToList();
                sources.RemoveAll(inactive.Contains);
                InactiveRegistries = inactive
                    .Select(e => $"\"{e.AssetPath}\" is not loaded: its package \"{e.Data.package}\" isn't installed.")
                    .ToList();
                InactiveRegistryIds = new HashSet<RegistryGuid>(inactive.Select(e => e.Data.registryId));

                var published = WithPendingMain(sources);
                savedSources = published == sources ? null : sources;
                savedSnapshot = null;

                Publish(RegistrySnapshot<TRow>.Build(published, GetNames, MainRegistryPath));
            }
            finally
            {
                reloading = false;
            }
        }

        /// <summary>
        /// The sources with the main registry's content replaced by its staged edits, if there are any.
        /// While a player is being built, only saved content is published.
        /// </summary>
        private List<RegistrySnapshot<TRow>.Source> WithPendingMain(List<RegistrySnapshot<TRow>.Source> _Sources)
        {
            if (BuildPipeline.isBuildingPlayer || !TryGetPendingData(out var data)) return _Sources;

            int index = _Sources.FindIndex(e => e.AssetPath == MainRegistryPath);
            if (index < 0) return _Sources;

            var source = _Sources[index];
            return new List<RegistrySnapshot<TRow>.Source>(_Sources)
            {
                [index] = new RegistrySnapshot<TRow>.Source
                {
                    AssetPath = source.AssetPath,
                    Data = data.Clone(),
                    FileHash = source.FileHash,
                    ChecksumMismatch = source.ChecksumMismatch,
                    UsingLastGood = source.UsingLastGood,
                    Errors = source.Errors,
                    Warnings = source.Warnings,
                },
            };
        }

        private RegistrySnapshot<TRow>.Source CreateSource(string _Path, TAsset _Asset)
        {
            if (_Asset.IsValid)
            {
                var source = new RegistrySnapshot<TRow>.Source
                {
                    AssetPath = _Path,
                    Data = _Asset.Data.Clone(),
                    FileHash = _Asset.FileHash,
                    ChecksumMismatch = _Asset.ChecksumMismatch,
                    Errors = Array.Empty<string>(),
                    Warnings = new List<string>(_Asset.Warnings),
                };

                lastGood[_Path] = source;
                return source;
            }

            // Rejected: keep the last version that loaded, if any.
            if (!lastGood.TryGetValue(_Path, out var previous)) return null;

            Debug.LogWarning($"{DisplayName} registry \"{_Path}\" is invalid. The last version that loaded is used until it is fixed.");

            return new RegistrySnapshot<TRow>.Source
            {
                AssetPath = _Path,
                Data = previous.Data,
                FileHash = previous.FileHash,
                ChecksumMismatch = previous.ChecksumMismatch,
                UsingLastGood = true,
                Errors = new List<string>(_Asset.Errors),
                Warnings = new List<string>(_Asset.Warnings),
            };
        }

        private void Publish(RegistrySnapshot<TRow> _Snapshot)
        {
            var database = BuildDatabase(_Snapshot);
            if (database != null)
                database.hideFlags = HideFlags.HideAndDontSave;

            var previousDatabase = editorDatabase;
            editorDatabase = database;
            Volatile.Write(ref snapshot, _Snapshot);

            PublishDatabase(database, previousDatabase);

            // The first load, e.g. on entering Play Mode after a domain reload, changes nothing a handle was built from.
            if (EditorApplication.isPlaying && previousDatabase != null)
                Debug.LogWarning($"{DisplayName} registries changed during Play Mode. Cached handles are rebuilt.");

            RaiseSnapshotChanged();
        }
        #endregion



        #region Staging
        private const string NO_MAIN_REGISTRY = "There is no main registry yet.";

        // State revision the registries were last reloaded with. Not kept across domain reloads: an undo then
        // reloads once more than needed, which is harmless.
        private int appliedRevision = -1;

        // Cache of the pending checks and of the parsed staged content, valid for one state revision.
        private int pendingCacheRevision = -1;
        private bool pendingData;
        private bool pendingMarkers;
        private int stagedCacheRevision = -1;
        private RegistryData<TRow> stagedCache;

        // The markers saved on disk, as last loaded or saved, and the file they belong to.
        private List<RegistryMarker> savedMarkers;
        private string savedMarkersPath;

        private string SessionKey(string _Name) => $"LMK.Registry.{Id}.{_Name}:{MainRegistryPath}";

        // Hash of the file content the staged state is based on. Kept outside Undo, and across domain reloads.
        private string DiskHash
        {
            get => SessionState.GetString(SessionKey("DiskHash"), string.Empty);
            set => SessionState.SetString(SessionKey("DiskHash"), value);
        }

        // Hash of the written form of the registry content the staged state is based on.
        private string DataHash
        {
            get => SessionState.GetString(SessionKey("DataHash"), string.Empty);
            set => SessionState.SetString(SessionKey("DataHash"), value);
        }

        private List<RegistryMarker> SavedMarkers
        {
            get
            {
                if (savedMarkers == null || savedMarkersPath != MarkersPath)
                {
                    savedMarkers = RegistryMarkerStore.Load(MarkersPath);
                    savedMarkersPath = MarkersPath;
                }

                return savedMarkers;
            }
        }

        /// <summary>
        /// The state, if it belongs to the current main registry path.
        /// </summary>
        private RegistryStagingState CurrentState
        {
            get
            {
                var state = RegistryStagingState.Find(Id);
                return state != null && state.path == MainRegistryPath ? state : null;
            }
        }

        internal override bool HasPendingData
        {
            get
            {
                var state = CurrentState;
                if (state == null) return false;

                UpdatePendingCache(state);
                return pendingData;
            }
        }

        internal override bool HasPendingMarkers
        {
            get
            {
                var state = CurrentState;
                if (state == null) return false;

                UpdatePendingCache(state);
                return pendingMarkers;
            }
        }

        public override bool DiskChangedWhilePending => HasPendingData && ReadDiskHash() != DiskHash;

        /// <summary>
        /// The staged main registry content, if it differs from the saved content. Never modify it.
        /// </summary>
        public bool TryGetPendingData(out RegistryData<TRow> _Data)
        {
            _Data = null;

            var state = CurrentState;
            if (state == null) return false;

            UpdatePendingCache(state);
            if (!pendingData) return false;

            _Data = StagedData(state);
            return true;
        }

        private void UpdatePendingCache(RegistryStagingState _State)
        {
            if (pendingCacheRevision == _State.revision) return;

            pendingCacheRevision = _State.revision;
            pendingData = RegistryFile.HashText(_State.text) != DataHash;
            pendingMarkers = !RegistryMarkerStore.AreEqual(_State.markers, SavedMarkers);
        }

        private void InvalidatePendingCache() => pendingCacheRevision = -1;

        private RegistryData<TRow> StagedData(RegistryStagingState _State)
        {
            if (stagedCacheRevision == _State.revision && stagedCache != null) return stagedCache;

            stagedCache = string.IsNullOrEmpty(_State.text)
                ? new RegistryData<TRow>()
                : RegistryFile.Parse(_State.text, this).Data ?? new RegistryData<TRow>();
            stagedCacheRevision = _State.revision;

            return stagedCache;
        }

        private string ReadDiskHash()
            => File.Exists(MainRegistryPath) ? RegistryFile.HashText(File.ReadAllText(MainRegistryPath)) : string.Empty;

        /// <summary>
        /// Stage one operation on the main registry: lock, copy, change, validate, record Undo, republish.
        /// </summary>
        /// <param name="_UndoName"> Name of the Undo step, e.g. "Add Gameplay Tag". </param>
        /// <param name="_Change"> Changes the copy it is given. A failed result discards the copy. </param>
        public RegistryResult Apply(string _UndoName, Func<RegistryData<TRow>, RegistryResult> _Change)
        {
            if (_Change == null) throw new ArgumentNullException(nameof(_Change));

            if (!TryBegin(out var state, out string error))
                return RegistryResult.Fail(error);

            var working = StagedData(state).Clone();
            var result = _Change(working);
            if (!result.Success) return result;

            var errors = new List<string>();
            RegistryFile.Validate(working, this, errors);
            if (errors.Count > 0)
                return RegistryResult.Fail(string.Join("\n", errors));

            string text = RegistryFile.Write(working, this);

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(_UndoName);
            Undo.RecordObject(state, _UndoName);

            state.text = text;
            state.revision = RegistrySession.NextRevision();
            Undo.FlushUndoRecordObjects();

            ReloadStaged(state);
            RaiseStateChanged();
            RegistrySession.RaiseStateChanged();
            return result;
        }

        /// <summary>
        /// Create an empty main registry with a new registry ID. Written immediately, since there is no file to
        /// stage against. Not undoable.
        /// </summary>
        public RegistryResult CreateMainRegistry(string _Name = "Main")
        {
            if (File.Exists(MainRegistryPath))
                return RegistryResult.Fail($"A main registry already exists at \"{MainRegistryPath}\".");

            if (!RegistryLockProvider.Current.TryAcquire(LockedPaths, out string lockError))
                return RegistryResult.Fail(RegistryLockProvider.RefusedMessage(lockError));

            RegistryAssets.EnsureFolder(Path.GetDirectoryName(MainRegistryPath)?.Replace('\\', '/'));

            var state = RegistryStagingState.FindOrCreate(Id);
            bool keepMarkers = state.path == MainRegistryPath;

            Undo.ClearUndo(state);
            state.path = MainRegistryPath;
            var data = new RegistryData<TRow>
            {
                registryId = RegistryGuid.NewGuid(),
                name = _Name,
                origin = RegistryOrigin.Main,
            };
            state.text = RegistryFile.Write(data, this);
            if (!keepMarkers)
                state.markers = new List<RegistryMarker>(SavedMarkers);
            state.revision = RegistrySession.NextRevision();

            RegistryFile.WriteAtomic(MainRegistryPath, state.text);
            DiskHash = DataHash = RegistryFile.HashText(state.text);
            InvalidatePendingCache();

            AssetDatabase.ImportAsset(MainRegistryPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ReloadStaged(state);
            InvokeOnMainRegistryCreated();

            RaiseStateChanged();
            RegistrySession.RaiseStateChanged();
            return RegistryResult.Ok(data.registryId);
        }

        private bool TryBegin(out RegistryStagingState _State, out string _Error)
        {
            _State = null;

            if (!File.Exists(MainRegistryPath))
            {
                _Error = $"{NO_MAIN_REGISTRY} Create it in the {EditorName}.";
                return false;
            }

            var parsed = RegistryFile.Parse(File.ReadAllText(MainRegistryPath), this);
            if (!parsed.IsValid)
            {
                _Error = $"The main registry has errors. Fix them before editing:\n{string.Join("\n", parsed.Errors)}";
                return false;
            }

            if (parsed.Data.origin != RegistryOrigin.Main)
            {
                _Error = $"\"{MainRegistryPath}\" is not a main registry.";
                return false;
            }

            if (!RegistryLockProvider.Current.TryAcquire(LockedPaths, out string lockError))
            {
                _Error = RegistryLockProvider.RefusedMessage(lockError);
                return false;
            }

            _State = SyncState();
            _Error = null;
            return true;
        }

        /// <summary>
        /// Make the state belong to the current main registry. Without staged registry edits, it follows the
        /// file: if the file changed since it was loaded or saved, the registry content is loaded again and the
        /// undo history dropped. Staged markers are kept.
        /// </summary>
        private RegistryStagingState SyncState()
        {
            var state = RegistryStagingState.FindOrCreate(Id);

            if (state.path != MainRegistryPath)
            {
                LoadFromDisk(state, false);
                return state;
            }

            UpdatePendingCache(state);
            if (!pendingData && ReadDiskHash() != DiskHash)
                LoadFromDisk(state, true);

            return state;
        }

        /// <summary>
        /// Load the registry content from disk, and clear the kind's undo history.
        /// </summary>
        /// <param name="_KeepMarkers"> Keep the staged markers, or load the saved ones. </param>
        private void LoadFromDisk(RegistryStagingState _State, bool _KeepMarkers)
        {
            string text = File.Exists(MainRegistryPath) ? File.ReadAllText(MainRegistryPath) : null;
            var parsed = text != null ? RegistryFile.Parse(text, this) : null;
            bool samePath = _State.path == MainRegistryPath;

            Undo.ClearUndo(_State);
            _State.path = MainRegistryPath;

            if (parsed != null && parsed.IsValid)
                _State.text = RegistryFile.Write(parsed.Data, this);
            else if (parsed == null || !samePath)
                _State.text = string.Empty;
            // An invalid file keeps the content loaded before, like the loading service.

            if (!_KeepMarkers || !samePath)
                _State.markers = new List<RegistryMarker>(SavedMarkers);

            _State.revision = RegistrySession.NextRevision();

            DiskHash = text != null ? RegistryFile.HashText(text) : string.Empty;
            DataHash = RegistryFile.HashText(_State.text);
            InvalidatePendingCache();
        }

        /// <summary>
        /// Publish the staged state to every view of the kind.
        /// </summary>
        private void ReloadStaged(RegistryStagingState _State)
        {
            appliedRevision = _State.revision;
            Reload();
        }
        #endregion



        #region Session Hooks
        internal override string CheckSave()
        {
            var state = CurrentState;
            if (state == null || !HasPendingData) return null;

            if (!File.Exists(MainRegistryPath))
                return $"The main registry \"{MainRegistryPath}\" no longer exists. Discard the changes.";

            if (ReadDiskHash() != DiskHash)
                return $"\"{MainRegistryPath}\" changed on disk since you began editing (a pull or a revert?). " +
                       "Saving would overwrite that change. Discard your changes to load it.";

            var errors = new List<string>();
            RegistryFile.Validate(StagedData(state), this, errors);
            if (errors.Count > 0)
                return $"The staged {DisplayName} registry is invalid:\n{string.Join("\n", errors)}";

            return null;
        }

        internal override string WriteStaged()
        {
            var state = CurrentState;
            RegistryFile.WriteAtomic(MainRegistryPath, state.text);

            // Before the import, so the postprocessor recognizes our own write.
            DiskHash = DataHash = RegistryFile.HashText(state.text);
            InvalidatePendingCache();
            return MainRegistryPath;
        }

        internal override void SaveMarkers()
        {
            var state = CurrentState;
            if (state == null) return;

            RegistryMarkerStore.Save(MarkersPath, state.markers);
            savedMarkers = new List<RegistryMarker>(state.markers);
            savedMarkersPath = MarkersPath;
            InvalidatePendingCache();
        }

        internal override void AfterSave()
        {
            var state = CurrentState;
            if (state != null)
                ReloadStaged(state);

            RaiseStateChanged();
            RaiseMarkersChanged();
        }

        internal override void DiscardStaged()
        {
            var state = RegistryStagingState.Find(Id);
            if (state == null) return;

            savedMarkers = null;
            LoadFromDisk(state, false);
            ReloadStaged(state);

            RaiseStateChanged();
            RaiseMarkersChanged();
        }

        internal override void OnUndoRedo()
        {
            var state = CurrentState;
            if (state == null || state.revision == appliedRevision) return;

            InvalidatePendingCache();
            ReloadStaged(state);

            RaiseStateChanged();
            RaiseMarkersChanged();
        }

        internal override void OnMainRegistryImported()
        {
            var state = CurrentState;
            if (state == null || !File.Exists(MainRegistryPath)) return;
            if (ReadDiskHash() == DiskHash) return;

            UpdatePendingCache(state);
            if (!pendingData)
            {
                LoadFromDisk(state, true);
                appliedRevision = state.revision;
            }

            RaiseStateChanged();
        }
        #endregion



        #region Editor UI
        public override IEnumerable<(RegistryGuid Registry, string RegistryName, RegistryGuid Id, string Name)> EnumerateRows()
        {
            foreach (var registry in Snapshot.Registries)
                foreach (var row in registry.Data.rows)
                {
                    string name = registry.GetName(row.id);
                    if (name != null)
                        yield return (registry.Id, registry.Name, row.id, name);
                }
        }
        #endregion



        #region Code Generation
        /// <summary>
        /// The description written in the summary of a row's generated constant, or <see langword="null"/>.
        /// </summary>
        protected virtual string GetCodeDescription(in TRow _Row) => null;

        internal override List<RegistryCodeGen.Registry> CollectCodeRegistries()
        {
            var snapshot = SavedSnapshot;
            var result = new List<RegistryCodeGen.Registry>();

            foreach (var registry in snapshot.Registries)
            {
                bool isMain = registry == snapshot.Main;
                if (!isMain && registry.Origin == RegistryOrigin.Main) continue;

                var entry = new RegistryCodeGen.Registry { Id = registry.Id, Name = registry.Name, IsMain = isMain };
                foreach (var row in registry.Data.rows)
                {
                    string name = registry.GetName(row.id);
                    if (name != null)
                        entry.Rows.Add(new RegistryCodeGen.Row(row.id, name, GetCodeDescription(row.data)));
                }

                result.Add(entry);
            }

            return result;
        }
        #endregion



        #region References
        public override bool KnowsRegistry(RegistryGuid _Registry)
            => Snapshot.Find(_Registry) != null || InactiveRegistryIds.Contains(_Registry);

        public override bool HasRowAnywhere(RegistryGuid _Row) => FindRegistryOf(Snapshot, _Row) != RegistryGuid.Zero;

        public override bool IsMissing(RegistryGuid _Registry, RegistryGuid _Id)
        {
            var status = GetStatus(_Registry, _Id);
            return status == RegistryStatus.Deleted || status == RegistryStatus.MissingRegistry;
        }

        public override RegistryStatus GetStatus(RegistryGuid _Registry, RegistryGuid _Id)
        {
            if (_Id.IsZero) return RegistryStatus.None;

            var snapshot = Snapshot;
            if (_Registry.IsZero)
                return FindRegistryOf(snapshot, _Id).IsZero ? RegistryStatus.Deleted : RegistryStatus.Live;

            var registry = snapshot.Find(_Registry);
            if (registry == null) return RegistryStatus.MissingRegistry;

            return registry.Contains(_Id) ? RegistryStatus.Live : RegistryStatus.Deleted;
        }

        public override List<string> CheckFile(string _Text)
        {
            var parsed = RegistryFile.Parse(_Text, this);
            var problems = new List<string>(parsed.Errors);

            if (parsed.ChecksumMismatch)
                problems.Add($"the checksum doesn't match its rows. It was edited outside the {EditorName}.");

            return problems;
        }

        public override string GetRowName(RegistryGuid _Registry, RegistryGuid _Id) => Snapshot.Find(_Registry)?.GetName(_Id);

        public override bool TryFindRelinkTarget(RegistryGuid _Registry, string _Name, out RegistryGuid _TargetRegistry, out RegistryGuid _TargetId)
        {
            _TargetRegistry = _TargetId = RegistryGuid.Zero;
            if (string.IsNullOrEmpty(_Name)) return false;

            var snapshot = Snapshot;
            var sameRegistry = snapshot.Find(_Registry);
            if (sameRegistry != null && sameRegistry.TryFind(_Name, out _TargetId))
            {
                _TargetRegistry = sameRegistry.Id;
                return true;
            }

            var main = snapshot.Main;
            if (main != null && main != sameRegistry && main.TryFind(_Name, out _TargetId))
            {
                _TargetRegistry = main.Id;
                return true;
            }

            return false;
        }

        public override RegistryGuid MainRegistryId => Snapshot.Main?.Id ?? RegistryGuid.Zero;

        public override bool HasEditableMain
        {
            get
            {
                var main = Snapshot.Main;
                return main != null && !main.IsReadOnly && main.AssetPath == MainRegistryPath;
            }
        }

        public override bool TryFindInMain(string _Name, out RegistryGuid _Id)
        {
            _Id = RegistryGuid.Zero;
            var main = Snapshot.Main;
            return main != null && main.TryFind(_Name, out _Id);
        }

        internal override RegistryResult AddImportedRows(IReadOnlyList<(string Name, RegistryGuid Id)> _Rows, Dictionary<string, RegistryGuid> _Ids, List<string> _Warnings)
        {
            return Apply($"Add Imported {DisplayName}", data =>
            {
                foreach (var (name, id) in _Rows.OrderBy(e => OrphanOrder(e.Name)).ThenBy(e => e.Name, StringComparer.Ordinal))
                {
                    var added = AddRowForOrphan(data, name, id, null, out string warning);
                    if (added.IsZero)
                    {
                        _Warnings.Add(warning ?? $"\"{name}\" could not be added to the {DisplayName} main registry.");
                        continue;
                    }

                    _Ids[name] = added;
                }

                return RegistryResult.Ok();
            });
        }

        private static RegistryGuid FindRegistryOf(RegistrySnapshot<TRow> _Snapshot, RegistryGuid _Row)
        {
            foreach (var registry in _Snapshot.Registries)
                if (registry.Contains(_Row))
                    return registry.Id;

            return RegistryGuid.Zero;
        }
        #endregion



        #region Packages
        private const int MAX_LISTED_PACK_PROBLEMS = 20;

        /// <summary>
        /// Add a row that references from a package need, to a registry generated for that package. <br/>
        /// The row is named <paramref name="_Name"/> and gets the ID <paramref name="_PreferredId"/> if the registry
        /// doesn't use it yet; a zero ID asks for a new one. The kind may add rows it needs with it, such as a tag's
        /// parents, and may reuse a row already named <paramref name="_Name"/>.
        /// </summary>
        /// <param name="_Description"> Description of a created row. <see langword="null"/> keeps the current one. </param>
        /// <param name="_Warning"> Why no row could be added, e.g. an invalid name. </param>
        /// <returns> The ID of the row now named <paramref name="_Name"/>, or zero if none could be added. </returns>
        public abstract RegistryGuid AddRowForOrphan(RegistryData<TRow> _Data, string _Name, RegistryGuid _PreferredId, string _Description, out string _Warning);

        /// <summary>
        /// Order in which orphaned references are added: lower first. Tags add shorter paths first, so referenced
        /// parents keep their IDs.
        /// </summary>
        protected virtual int OrphanOrder(string _Name) => 0;

        /// <summary>
        /// Rows a package declares besides its references, e.g. the tags of its tag files. They are added with new IDs.
        /// </summary>
        protected virtual IReadOnlyList<(string Name, string Description)> GetPackageRows(RegistryPackage _Package, List<string> _Warnings)
            => Array.Empty<(string, string)>();

        /// <summary>
        /// Add to <paramref name="_Keep"/> the rows that kept rows need, e.g. a tag's parents, before unreferenced
        /// rows are removed.
        /// </summary>
        protected virtual void ExpandKeptRows(RegistryData<TRow> _Data, HashSet<RegistryGuid> _Keep) { }

        internal override List<(string Path, string Text, int Rows)> PlanPackageRegistries(RegistryPackage _Package, List<RegistryReference> _References,
                                                                                           bool _RemoveMissing, List<string> _Warnings, out string _Error)
        {
            _Error = null;
            var snapshot = Snapshot;

            // This package's generated registries: regenerated in place. Every other registry is known.
            var existing = snapshot.Registries
                .Where(e => e.Origin == RegistryOrigin.Generated && e.Data.package == _Package.Name)
                .ToDictionary(e => e.Id);

            var known = new HashSet<RegistryGuid>(snapshot.Registries.Select(e => e.Id).Where(e => !existing.ContainsKey(e)));
            known.UnionWith(InactiveRegistryIds);

            // References, by registry.
            var groups = new Dictionary<RegistryGuid, List<RegistryReference>>();
            int unplaced = 0;

            foreach (var reference in _References)
            {
                var registry = reference.HasRegistry ? reference.Registry : FindRegistryOf(snapshot, reference.Id);
                if (registry.IsZero)
                {
                    unplaced++;
                    continue;
                }

                if (known.Contains(registry)) continue;

                if (!groups.TryGetValue(registry, out var list))
                    groups.Add(registry, list = new List<RegistryReference>());

                list.Add(reference);
            }

            if (unplaced > 0)
                _Warnings.Add($"{unplaced} prefab override(s) changed only a {RowNoun} ID that no registry has, so their registry is unknown. They were skipped.");

            // Existing generated registries stay, even if nothing references them anymore.
            foreach (var id in existing.Keys)
                if (!groups.ContainsKey(id))
                    groups.Add(id, new List<RegistryReference>());

            var packageRows = GetPackageRows(_Package, _Warnings);
            var files = new List<(string Path, string Text, int Rows)>();

            if (groups.Count == 0 && packageRows.Count == 0)
                return files;

            // Declared rows go to the registry with the most references, or to a new one.
            var declaredRegistry = groups.Count > 0
                ? groups.OrderByDescending(e => e.Value.Count).ThenBy(e => e.Key).First().Key
                : RegistryGuid.NewGuid();
            if (!groups.ContainsKey(declaredRegistry))
                groups.Add(declaredRegistry, new List<RegistryReference>());

            // Build every registry before writing any.
            foreach (var (registryId, references) in groups.OrderBy(e => e.Key))
            {
                existing.TryGetValue(registryId, out var current);

                var data = current?.Data.Clone() ?? new RegistryData<TRow>
                {
                    registryId = registryId,
                    name = _Package.DisplayName,
                    origin = RegistryOrigin.Generated,
                };
                data.package = _Package.Name;
                data.version = _Package.Version ?? string.Empty;

                var keep = new HashSet<RegistryGuid>();
                AddReferencedRows(data, references, keep, _Warnings);

                if (registryId == declaredRegistry)
                {
                    foreach (var (name, description) in packageRows)
                    {
                        var id = AddRowForOrphan(data, name, RegistryGuid.Zero, description, out string warning);
                        if (warning != null) _Warnings.Add(warning);
                        if (!id.IsZero) keep.Add(id);
                    }
                }

                if (_RemoveMissing)
                {
                    ExpandKeptRows(data, keep);
                    data.rows.RemoveAll(e => !keep.Contains(e.id));
                }

                var errors = new List<string>();
                RegistryFile.Validate(data, this, errors);
                if (errors.Count > 0)
                {
                    _Error = $"The {DisplayName} registry generated for {registryId} is invalid:\n{string.Join("\n", errors)}";
                    return null;
                }

                string path = current?.AssetPath ?? GeneratedPath(_Package.Name, registryId, groups.Count > 1, files.Select(e => e.Path));
                files.Add((path, RegistryFile.Write(data, this), data.rows.Count));
            }

            return files;
        }

        /// <summary>
        /// A row per referenced ID, with that ID, named after what most references call it.
        /// </summary>
        private void AddReferencedRows(RegistryData<TRow> _Data, List<RegistryReference> _References, HashSet<RegistryGuid> _Keep, List<string> _Warnings)
        {
            var named = _References
                .GroupBy(e => e.Id)
                .Select(group =>
                {
                    var names = group.Select(e => e.Name).Where(e => !string.IsNullOrEmpty(e))
                        .GroupBy(e => e).OrderByDescending(e => e.Count()).ThenBy(e => e.Key, StringComparer.Ordinal)
                        .Select(e => e.Key).ToList();

                    if (names.Count > 1)
                        _Warnings.Add($"{Capitalized(RowNoun)} {group.Key} is referenced as {string.Join(", ", names.Select(e => $"\"{e}\""))}. \"{names[0]}\" is used.");

                    return (Id: group.Key, Name: names.FirstOrDefault());
                })
                .ToList();

            foreach (var (id, name) in named.OrderBy(e => OrphanOrder(e.Name)).ThenBy(e => e.Name, StringComparer.Ordinal))
            {
                // Already a row: kept where it is.
                if (_Data.IndexOf(id) >= 0)
                {
                    _Keep.Add(id);
                    continue;
                }

                var added = AddRowForOrphan(_Data, name, id, null, out string warning);
                if (added.IsZero)
                {
                    _Warnings.Add(warning ?? $"{Capitalized(RowNoun)} {id} (\"{name}\") can't be added. Its references stay unresolved.");
                    continue;
                }

                _Keep.Add(added);
                if (added != id)
                    _Warnings.Add($"\"{name}\" is already {RowNoun} {added}, so references to {RowNoun} {id} with that name stay unresolved.");
            }
        }

        private string GeneratedPath(string _PackageName, RegistryGuid _RegistryId, bool _Several, IEnumerable<string> _Taken)
        {
            string folder = GeneratedRegistryFolder.TrimEnd('/');
            string path = _Several
                ? $"{folder}/{_PackageName}.{_RegistryId.ToString()[..8]}.{FileExtension}"
                : $"{folder}/{_PackageName}.{FileExtension}";

            // Another registry already uses that file name.
            if (_Taken.Contains(path) || File.Exists(path))
                path = $"{folder}/{_PackageName}.{_RegistryId}.{FileExtension}";

            return path;
        }

        public override List<(string Package, string AssetPath, string GeneratedVersion, string InstalledVersion)> FindOutdatedRegistries()
        {
            var installed = RegistryPackages.GetInstalled();
            var result = new List<(string, string, string, string)>();

            foreach (var registry in Snapshot.Registries)
            {
                if (registry.Origin != RegistryOrigin.Generated || string.IsNullOrEmpty(registry.Data.package)) continue;
                if (!installed.TryGetValue(registry.Data.package, out string version)) continue;

                if (!string.IsNullOrEmpty(registry.Data.version) && registry.Data.version != version)
                    result.Add((registry.Data.package, registry.AssetPath, registry.Data.version, version));
            }

            return result;
        }

        internal override RegistryGuid ValidatePack(RegistryPackage _Package, List<(string Path, RegistryReference Reference)> _References,
                                                   RegistryGuid _PublishedRegistryId, List<string> _Errors)
        {
            var registryFiles = _Package.GetAssets("." + FileExtension);

            // A package that uses no rows of this kind has nothing to check.
            if (_References.Count == 0 && registryFiles.Count == 0)
                return RegistryGuid.Zero;

            // Rule 1: one valid package registry.
            var own = ReadOwnRegistry(_Package, registryFiles, _Errors);

            // Rule 5: same registry ID as before.
            if (own != null && !_PublishedRegistryId.IsZero && own.registryId != _PublishedRegistryId)
                _Errors.Add($"[{DisplayName}] The registry ID changed from {_PublishedRegistryId} to {own.registryId} since the last pack. " +
                            "Every reference made with the old ID would stop resolving. Restore the old ID.");

            // The registries references may point to, with their row IDs.
            var snapshot = Snapshot;
            var allowed = new Dictionary<RegistryGuid, (string Name, HashSet<RegistryGuid> Ids)>();
            if (own != null)
                allowed[own.registryId] = ($"its own registry \"{own.name}\"", new HashSet<RegistryGuid>(own.rows.Select(e => e.id)));

            foreach (string dependency in _Package.Dependencies)
            {
                string prefix = $"Packages/{dependency}/";
                var registries = snapshot.Registries.Where(e =>
                    (e.Origin == RegistryOrigin.Package && e.AssetPath.StartsWith(prefix, StringComparison.Ordinal))
                    || (e.Origin == RegistryOrigin.Generated && e.Data.package == dependency));

                foreach (var registry in registries)
                    allowed.TryAdd(registry.Id, ($"\"{dependency}\"'s registry", new HashSet<RegistryGuid>(registry.Ids)));
            }

            var mainId = snapshot.Main?.Id ?? RegistryGuid.Zero;

            // The kind's own rules on its rows, e.g. references between rows.
            if (own != null)
            {
                var rowProblems = new List<string>();
                ValidatePackRows(own, allowed.Keys.ToList(), mainId, rowProblems);
                foreach (string problem in rowProblems.Distinct())
                    _Errors.Add($"[{DisplayName}] Registry \"{own.name}\": {problem}");
            }

            // Rules 2 to 4: every reference.
            var problems = new List<string>();
            foreach (var (path, reference) in _References)
            {
                string problem = CheckPackReference(reference, allowed, mainId);
                if (problem != null)
                    problems.Add($"[{DisplayName}] \"{path}\": {Describe(reference)} {problem}");
            }

            var distinct = problems.Distinct().ToList();
            _Errors.AddRange(distinct.Take(MAX_LISTED_PACK_PROBLEMS));
            if (distinct.Count > MAX_LISTED_PACK_PROBLEMS)
                _Errors.Add($"[{DisplayName}] (and {distinct.Count - MAX_LISTED_PACK_PROBLEMS} more problems with references)");

            return own?.registryId ?? RegistryGuid.Zero;
        }

        /// <summary>
        /// Check the kind's own pack rules on a package's registry, after the core's: e.g. rows that reference other rows
        /// may only name rows of registries other projects will have too. Called only when the package has exactly one
        /// valid registry of the kind.
        /// </summary>
        /// <param name="_Own"> The package's registry, as read from its file. </param>
        /// <param name="_AllowedRegistries"> The package's own registry and the registries of its declared dependencies. </param>
        /// <param name="_MainRegistryId"> The project's main registry, which other projects don't have, or zero. </param>
        /// <param name="_Problems"> Receives one message per problem, without the kind's name. </param>
        protected virtual void ValidatePackRows(RegistryData<TRow> _Own, IReadOnlyCollection<RegistryGuid> _AllowedRegistries,
                                                RegistryGuid _MainRegistryId, List<string> _Problems) { }

        private string CheckPackReference(in RegistryReference _Reference, Dictionary<RegistryGuid, (string Name, HashSet<RegistryGuid> Ids)> _Allowed, RegistryGuid _MainId)
        {
            var id = _Reference.Id;

            // A prefab override that changed only the row ID: its registry is the one that has the ID.
            if (!_Reference.HasRegistry)
                return _Allowed.Values.Any(e => e.Ids.Contains(id))
                    ? null
                    : $"is not a {RowNoun} of the package's registry or of a dependency's registry.";

            if (!_MainId.IsZero && _Reference.Registry == _MainId)
                return $"references this project's main registry, which other projects don't have. Move the {RowNoun} to the package's registry.";

            if (!_Allowed.TryGetValue(_Reference.Registry, out var registry))
                return $"references registry {_Reference.Registry}, which is neither the package's own registry nor the registry of a package listed in its dependencies.";

            return registry.Ids.Contains(id) ? null : $"is not in {registry.Name}: its {RowNoun} ID {id} doesn't exist there.";
        }

        private string Describe(in RegistryReference _Reference)
            => string.IsNullOrEmpty(_Reference.Name) ? $"{RowNoun} {_Reference.Id}" : $"{RowNoun} \"{_Reference.Name}\"";

        /// <summary>
        /// The package's own registry, read from its file. Reports every problem with rule 1.
        /// </summary>
        private RegistryData<TRow> ReadOwnRegistry(RegistryPackage _Package, List<string> _Files, List<string> _Errors)
        {
            RegistryData<TRow> own = null;
            int count = 0;

            foreach (string path in _Files)
            {
                var parsed = RegistryFile.Parse(File.ReadAllText(path), this);
                if (!parsed.IsValid)
                {
                    _Errors.Add($"[{DisplayName}] \"{path}\" is invalid: {string.Join(" ", parsed.Errors)}");
                    continue;
                }

                if (parsed.Data.origin != RegistryOrigin.Package)
                {
                    _Errors.Add($"[{DisplayName}] \"{path}\" is a {RegistryFile.OriginToText(parsed.Data.origin)} registry. A package ships only a registry of kind \"package\".");
                    continue;
                }

                if (parsed.ChecksumMismatch)
                    _Errors.Add($"[{DisplayName}] \"{path}\" has no valid checksum: it was edited outside the {EditorName}.");

                count++;
                own ??= parsed.Data;
            }

            if (count == 0)
                _Errors.Add($"[{DisplayName}] \"{_Package.Root}\" contains no registry of kind \"package\". A package that uses {DisplayName} needs exactly one.");
            else if (count > 1)
                _Errors.Add($"[{DisplayName}] \"{_Package.Root}\" contains {count} registries of kind \"package\". A package needs exactly one.");

            return count == 1 ? own : null;
        }

        private static string Capitalized(string _Text)
            => string.IsNullOrEmpty(_Text) ? _Text : char.ToUpperInvariant(_Text[0]) + _Text[1..];
        #endregion



        #region Markers
        /// <summary>
        /// The markers to show: staged ones, or the saved ones before anything was staged.
        /// </summary>
        public override IReadOnlyList<RegistryMarker> Markers => CurrentState?.markers ?? SavedMarkers;

        /// <summary>
        /// The marker colour of a row, if it has one.
        /// </summary>
        public override bool TryGetMarker(RegistryGuid _Registry, RegistryGuid _Row, out Color _Color)
        {
            foreach (var marker in Markers)
            {
                if (!marker.Is(_Registry, _Row)) continue;

                _Color = marker.color;
                return true;
            }

            _Color = Color.clear;
            return false;
        }

        /// <summary>
        /// Stage a row's marker colour, for a row of any registry of the kind. A fully transparent colour removes
        /// the marker. Markers are per user, so no lock is needed. Consecutive changes within one editor event
        /// (dragging in the colour picker) share one undo step.
        /// </summary>
        public override void SetMarker(RegistryGuid _Registry, RegistryGuid _Row, Color _Color)
        {
            // Stored as saved, so a saved marker compares equal to the staged one.
            _Color = (Color32)_Color;

            var state = SyncState();
            int index = state.markers.FindIndex(e => e.Is(_Registry, _Row));
            bool remove = _Color.a <= 0f;

            if (remove ? index < 0 : index >= 0 && state.markers[index].color == _Color) return;

            string undoName = $"Set {DisplayName} Marker";
            Undo.RecordObject(state, undoName);
            Undo.SetCurrentGroupName(undoName);

            if (remove) state.markers.RemoveAt(index);
            else if (index >= 0) state.markers[index] = new RegistryMarker(_Registry, _Row, _Color);
            else state.markers.Add(new RegistryMarker(_Registry, _Row, _Color));

            // The registries didn't change: keep the parsed content, which only depends on the text.
            bool cacheValid = stagedCacheRevision == state.revision;
            state.revision = RegistrySession.NextRevision();
            if (cacheValid) stagedCacheRevision = state.revision;
            Undo.FlushUndoRecordObjects();

            // The registries didn't change, so they are already up to date with this revision.
            appliedRevision = state.revision;

            RaiseStateChanged();
            RaiseMarkersChanged();
            RegistrySession.RaiseStateChanged();
        }
        #endregion
    }
}
