using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using UnityEditor;
using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Outcome of a <see cref="TagOperations"/> call.
    /// </summary>
    internal readonly struct TagOperationResult
    {
        public readonly bool Success;

        /// <summary>
        /// Why the operation was refused. <see langword="null"/> on success.
        /// </summary>
        public readonly string Error;

        /// <summary>
        /// The tag the operation created or changed, if any.
        /// </summary>
        public readonly TagGuid Id;

        private TagOperationResult(bool _Success, string _Error, TagGuid _Id)
        {
            Success = _Success;
            Error = _Error;
            Id = _Id;
        }

        public static TagOperationResult Ok(TagGuid _Id = default) => new(true, null, _Id);

        public static TagOperationResult Fail(string _Error) => new(false, _Error, TagGuid.Zero);
    }



    /// <summary>
    /// The only code that changes the main registry: Add, Rename, Delete and Edit description, and the
    /// user's tag markers. <br/>
    /// Every edit is staged: it checks the lock, validates a copy, records Undo and changes only the in-memory
    /// <see cref="TagRegistryState"/>. The registries are reloaded with the staged content, so every tag view
    /// shows it. <see cref="Save"/> writes it to disk, and <see cref="Discard"/> drops it. No other asset is opened
    /// or modified. Package and generated registries are read-only here.
    /// </summary>
    /// <remarks>
    /// Undo and redo only change the staged state. If the file changes on disk while edits are staged (a pull, a
    /// revert), the edits are kept and <see cref="Save"/> is refused until they are discarded, so staged content
    /// never overwrites newer content. Without staged edits, the state follows the file and its undo history
    /// is dropped.
    /// </remarks>
    [InitializeOnLoad]
    internal static class TagOperations
    {
        private const string SESSION_DISK_HASH = "LMK.GameplayTags.DiskHash:";
        private const string SESSION_DATA_HASH = "LMK.GameplayTags.DataHash:";
        private const string SESSION_COUNTER = "LMK.GameplayTags.RevisionCounter";

        private const string NOT_IN_MAIN = "This tag is not in the main registry. Package and generated registries are read-only.";

        /// <summary>
        /// Called after the staged state changed: an edit, an undo or redo, a save, a discard, or a reload from disk.
        /// </summary>
        public static event Action StateChanged;

        /// <summary>
        /// Called after a marker changed. Marker edits don't reload the registries.
        /// </summary>
        public static event Action MarkersChanged;

        // State revision the registries were last reloaded with. Not kept across domain reloads: an undo then
        // reloads once more than needed, which is harmless.
        private static int appliedRevision = -1;

        // Cache of the pending checks, valid for one state revision.
        private static int pendingCacheRevision = -1;
        private static bool pendingData;
        private static bool pendingMarkers;

        // The markers saved on disk, as last loaded or saved, and the file they belong to.
        private static List<TagMarker> savedMarkers;
        private static string savedMarkersPath;



        static TagOperations()
        {
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorApplication.wantsToQuit += OnWantsToQuit;
        }

        private static string MainPath => TagRegistryService.MainRegistryPath;

        internal static IReadOnlyList<string> LockedPaths => new[] { MainPath, TagCodeGen.OutputPath };

        // Hash of the file content the staged state is based on. Kept outside Undo, and across domain reloads.
        private static string DiskHash
        {
            get => SessionState.GetString(SESSION_DISK_HASH + MainPath, string.Empty);
            set => SessionState.SetString(SESSION_DISK_HASH + MainPath, value);
        }

        // Hash of the written form of the registry content the staged state is based on.
        private static string DataHash
        {
            get => SessionState.GetString(SESSION_DATA_HASH + MainPath, string.Empty);
            set => SessionState.SetString(SESSION_DATA_HASH + MainPath, value);
        }

        private static List<TagMarker> SavedMarkers
        {
            get
            {
                if (savedMarkers == null || savedMarkersPath != TagMarkerStore.FilePath)
                {
                    savedMarkers = TagMarkerStore.Load();
                    savedMarkersPath = TagMarkerStore.FilePath;
                }

                return savedMarkers;
            }
        }

        private static int NextRevision()
        {
            int revision = SessionState.GetInt(SESSION_COUNTER, 0) + 1;
            SessionState.SetInt(SESSION_COUNTER, revision);

            return revision;
        }



        #region Pending Changes
        /// <summary>
        /// Whether there are staged edits that are not saved: registry content or markers.
        /// </summary>
        public static bool HasPendingChanges
        {
            get
            {
                var state = CurrentState;
                if (state == null) return false;

                UpdatePendingCache(state);
                return pendingData || pendingMarkers;
            }
        }

        /// <summary>
        /// Whether the main registry file changed on disk since the staged registry content was loaded or saved,
        /// while that content has unsaved edits. <see cref="Save"/> is refused then.
        /// </summary>
        public static bool DiskChangedWhilePending
        {
            get
            {
                var state = CurrentState;
                if (state == null) return false;

                UpdatePendingCache(state);
                return pendingData && ReadDiskHash() != DiskHash;
            }
        }

        /// <summary>
        /// The staged main registry content, if it differs from the saved content.
        /// </summary>
        internal static bool TryGetPendingData(out TagRegistryData _Data)
        {
            _Data = null;

            var state = CurrentState;
            if (state == null) return false;

            UpdatePendingCache(state);
            if (!pendingData) return false;

            _Data = state.data;
            return true;
        }

        /// <summary>
        /// The state, if it belongs to the current main registry path.
        /// </summary>
        private static TagRegistryState CurrentState
        {
            get
            {
                var state = TagRegistryState.Find();
                return state != null && state.path == MainPath ? state : null;
            }
        }

        private static void UpdatePendingCache(TagRegistryState _State)
        {
            if (pendingCacheRevision == _State.revision) return;

            pendingCacheRevision = _State.revision;
            pendingData = TagRegistryFile.HashText(TagRegistryFile.Write(_State.data)) != DataHash;
            pendingMarkers = !TagMarkerStore.AreEqual(_State.markers, SavedMarkers);
        }

        private static void InvalidatePendingCache() => pendingCacheRevision = -1;

        private static string ReadDiskHash()
            => File.Exists(MainPath) ? TagRegistryFile.HashText(File.ReadAllText(MainPath)) : string.Empty;
        #endregion



        #region Save And Discard
        /// <summary>
        /// Write the staged edits: the main registry file, if its content changed, and the markers. Then the
        /// generated code is written. Refused if the file changed on disk since the edits began, or without
        /// the lock.
        /// </summary>
        public static TagOperationResult Save()
        {
            var state = CurrentState;
            if (state == null || !HasPendingChanges) return TagOperationResult.Ok();

            bool writeData = pendingData;
            if (writeData)
            {
                if (!File.Exists(MainPath))
                    return TagOperationResult.Fail($"The main registry \"{MainPath}\" no longer exists. Discard the changes.");

                if (ReadDiskHash() != DiskHash)
                    return TagOperationResult.Fail(
                        $"\"{MainPath}\" changed on disk since you began editing (a pull or a revert?). " +
                        "Saving would overwrite that change. Discard your changes to load it.");

                if (!TagLockProvider.Current.TryAcquire(LockedPaths, out string lockError))
                    return TagOperationResult.Fail(LockMessage(lockError));

                var errors = new List<string>();
                TagRegistryFile.Validate(state.data, errors);
                if (errors.Count > 0)
                    return TagOperationResult.Fail($"The staged registry is invalid:\n{string.Join("\n", errors)}");

                string text = TagRegistryFile.Write(state.data);
                WriteAtomic(MainPath, text);

                // Before the import, so the postprocessor recognizes our own write.
                DiskHash = DataHash = TagRegistryFile.HashText(text);
            }

            if (pendingMarkers)
            {
                TagMarkerStore.Save(state.markers);
                savedMarkers = new List<TagMarker>(state.markers);
                savedMarkersPath = TagMarkerStore.FilePath;
            }

            InvalidatePendingCache();

            if (writeData)
                AssetDatabase.ImportAsset(MainPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            Reload(state);

            // Registry first, code last.
            if (writeData)
            {
                TagCodeGen.MarkPending();
                TagCodeGen.Flush();
            }

            StateChanged?.Invoke();
            MarkersChanged?.Invoke();
            return TagOperationResult.Ok();
        }

        /// <summary>
        /// Drop the staged edits: load the main registry and the markers from disk again, and clear the tag undo
        /// history.
        /// </summary>
        public static void Discard()
        {
            var state = TagRegistryState.Find();
            if (state == null) return;

            savedMarkers = null;
            LoadFromDisk(state, false);
            Reload(state);

            StateChanged?.Invoke();
            MarkersChanged?.Invoke();
        }

        /// <summary>
        /// Quitting with staged edits asks to save them, unless the Gameplay Tag Manager is open: Unity asks
        /// through the window then.
        /// </summary>
        private static bool OnWantsToQuit()
        {
            if (Application.isBatchMode || !HasPendingChanges) return true;
            if (Resources.FindObjectsOfTypeAll<GameplayTagManagerWindow>().Length > 0) return true;

            int choice = EditorUtility.DisplayDialogComplex(
                "Unsaved Gameplay Tag Changes",
                "The gameplay tags have unsaved changes. Save them before quitting?",
                "Save", "Cancel", "Discard");

            switch (choice)
            {
                case 0: return GameplayTagCommands.SaveWithDialogs();
                case 2: Discard(); return true;
                default: return false;
            }
        }
        #endregion



        #region Operations
        /// <summary>
        /// Create an empty main registry with a new registry ID. Written immediately, since there is no file to
        /// stage against. Not undoable.
        /// </summary>
        public static TagOperationResult CreateMainRegistry()
        {
            if (File.Exists(MainPath))
                return TagOperationResult.Fail($"A main registry already exists at \"{MainPath}\".");

            if (!TagLockProvider.Current.TryAcquire(LockedPaths, out string lockError))
                return TagOperationResult.Fail(LockMessage(lockError));

            EnsureFolder(Path.GetDirectoryName(MainPath)?.Replace('\\', '/'));

            var state = TagRegistryState.FindOrCreate();
            bool keepMarkers = state.path == MainPath;

            Undo.ClearUndo(state);
            state.path = MainPath;
            state.data = new TagRegistryData
            {
                registryId = TagGuid.NewGuid(),
                name = "Main",
                kind = TagRegistryKind.Main,
            };
            if (!keepMarkers)
                state.markers = new List<TagMarker>(SavedMarkers);
            state.revision = NextRevision();

            string text = TagRegistryFile.Write(state.data);
            WriteAtomic(MainPath, text);
            DiskHash = DataHash = TagRegistryFile.HashText(text);
            InvalidatePendingCache();

            AssetDatabase.ImportAsset(MainPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Reload(state);
            TagCodeGen.MarkPending();

            StateChanged?.Invoke();
            return TagOperationResult.Ok();
        }

        /// <summary>
        /// Add a tag, creating its missing parents as implicit tags. Adding a path that exists as an
        /// implicit tag makes it explicit and keeps its ID.
        /// </summary>
        public static TagOperationResult Add(string _Path, string _Description = null)
        {
            if (!TagPath.IsValidPath(_Path, out string error))
                return TagOperationResult.Fail(error);

            return Apply("Add Gameplay Tag", data =>
            {
                var id = AddPath(data, _Path, _Description, out var outcome);

                return outcome == AddPathOutcome.ExistedExplicit
                    ? TagOperationResult.Fail($"Tag \"{_Path}\" already exists.")
                    : TagOperationResult.Ok(id);
            });
        }

        /// <summary>
        /// Make an implicit tag explicit, keeping its ID and description.
        /// </summary>
        public static TagOperationResult MakeExplicit(TagGuid _Id)
        {
            return Apply("Make Gameplay Tag Explicit", data =>
            {
                int index = data.IndexOf(_Id);
                if (index < 0) return TagOperationResult.Fail(NOT_IN_MAIN);

                var row = data.rows[index];
                if (!row.isImplicit) return TagOperationResult.Fail("The tag is already explicit.");

                row.isImplicit = false;
                data.rows[index] = row;
                return TagOperationResult.Ok(_Id);
            });
        }

        /// <summary>
        /// Import tag file entries into the main registry as one operation, with one Undo step.
        /// </summary>
        /// <param name="_Remove"> Tags to delete with their subtrees, i.e. the tags not in the files. </param>
        public static TagOperationResult ImportTagFiles(IReadOnlyList<TagFileEntry> _Entries, bool _OverwriteDescriptions, IReadOnlyCollection<TagGuid> _Remove)
        {
            foreach (var entry in _Entries)
                if (!TagPath.IsValidPath(entry.Path, out string error))
                    return TagOperationResult.Fail(error);

            return Apply("Import Gameplay Tag Files", data =>
            {
                TagFileImport.ApplyTo(data, _Entries, _OverwriteDescriptions, _Remove);
                return TagOperationResult.Ok();
            });
        }

        /// <summary>
        /// Add the tags that assets imported from another project need, as one operation with one Undo
        /// step. Each tag keeps the ID its references use, if the main registry doesn't use it yet. An existing
        /// path is kept as it is.
        /// </summary>
        /// <param name="_Ids"> The ID of each path, as added or found. </param>
        public static TagOperationResult AddWithIds(IReadOnlyList<(string Path, TagGuid Id)> _Tags, out Dictionary<string, TagGuid> _Ids)
        {
            var ids = new Dictionary<string, TagGuid>(StringComparer.Ordinal);
            _Ids = ids;

            foreach (var tag in _Tags)
                if (!TagPath.IsValidPath(tag.Path, out string error))
                    return TagOperationResult.Fail(error);

            return Apply("Add Imported Gameplay Tags", data =>
            {
                // Parents first, so a parent that is referenced too keeps its ID.
                foreach (var tag in _Tags.OrderBy(e => e.Path.Count(c => c == TagPath.SEPARATOR)).ThenBy(e => e.Path, StringComparer.Ordinal))
                    ids[tag.Path] = AddPath(data, tag.Path, null, out _, tag.Id);

                return TagOperationResult.Ok();
            });
        }

        /// <summary>
        /// Rename a tag's last segment. Descendants and references follow, because they hold IDs.
        /// </summary>
        public static TagOperationResult Rename(TagGuid _Id, string _NewSegment)
        {
            if (!TagPath.IsValidSegment(_NewSegment, out string error))
                return TagOperationResult.Fail(error);

            return Apply("Rename Gameplay Tag", data =>
            {
                int index = data.IndexOf(_Id);
                if (index < 0) return TagOperationResult.Fail(NOT_IN_MAIN);

                var row = data.rows[index];
                if (row.segment == _NewSegment) return TagOperationResult.Ok(_Id);

                if (FindChild(data, row.parent, _NewSegment) >= 0)
                    return TagOperationResult.Fail($"A sibling tag is already named \"{_NewSegment}\".");

                row.segment = _NewSegment;
                data.rows[index] = row;

                return TagOperationResult.Ok(_Id);
            });
        }

        /// <summary>
        /// Delete a tag and all of its descendants.
        /// </summary>
        /// <param name="_RemoveEmptyParents">
        /// If true, implicit ancestors left without children are removed too. Otherwise the parent left empty
        /// stays, with its ID, as an explicit tag.
        /// </param>
        public static TagOperationResult Delete(TagGuid _Id, bool _RemoveEmptyParents)
        {
            return Apply("Delete Gameplay Tag", data =>
            {
                int index = data.IndexOf(_Id);
                if (index < 0) return TagOperationResult.Fail(NOT_IN_MAIN);

                var parent = data.rows[index].parent;
                var subtree = CollectSubtree(data, _Id);
                data.rows.RemoveAll(e => subtree.Contains(e.id));

                if (_RemoveEmptyParents)
                {
                    while (!parent.IsZero)
                    {
                        int parentIndex = data.IndexOf(parent);
                        var parentRow = data.rows[parentIndex];
                        if (!parentRow.isImplicit || HasChildren(data, parent)) break;

                        data.rows.RemoveAt(parentIndex);
                        parent = parentRow.parent;
                    }
                }
                else if (!parent.IsZero && !HasChildren(data, parent))
                {
                    int parentIndex = data.IndexOf(parent);
                    var parentRow = data.rows[parentIndex];
                    parentRow.isImplicit = false;
                    data.rows[parentIndex] = parentRow;
                }

                return TagOperationResult.Ok(_Id);
            });
        }

        /// <summary>
        /// Change a tag's description.
        /// </summary>
        public static TagOperationResult EditDescription(TagGuid _Id, string _Description)
        {
            return Apply("Edit Gameplay Tag Description", data =>
            {
                int index = data.IndexOf(_Id);
                if (index < 0) return TagOperationResult.Fail(NOT_IN_MAIN);

                var row = data.rows[index];
                row.description = _Description ?? string.Empty;
                data.rows[index] = row;

                return TagOperationResult.Ok(_Id);
            });
        }

        /// <summary>
        /// Paths of the implicit ancestors that deleting a tag would leave without children.
        /// </summary>
        public static IReadOnlyList<string> GetParentsLeftEmpty(TagGuid _Id)
        {
            var result = new List<string>();
            var main = TagRegistryService.Snapshot.Main;
            if (main == null || main.AssetPath != MainPath || !main.TryGetRow(_Id, out var row)) return result;

            var removed = _Id;
            for (var parent = row.parent; !parent.IsZero; parent = row.parent)
            {
                if (!main.TryGetRow(parent, out row) || !row.isImplicit) break;

                var children = main.GetChildren(parent);
                if (children.Count != 1 || !children[0].Equals(removed)) break;

                result.Add(main.GetPath(parent));
                removed = parent;
            }

            return result;
        }
        #endregion



        #region Markers
        /// <summary>
        /// The markers to show: staged ones, or the saved ones before anything was staged.
        /// </summary>
        public static IReadOnlyList<TagMarker> Markers => CurrentState?.markers ?? SavedMarkers;

        /// <summary>
        /// The marker colour of a tag, if it has one.
        /// </summary>
        public static bool TryGetMarker(TagGuid _Registry, TagGuid _Tag, out Color _Color)
        {
            foreach (var marker in Markers)
            {
                if (!marker.Is(_Registry, _Tag)) continue;

                _Color = marker.color;
                return true;
            }

            _Color = Color.clear;
            return false;
        }

        /// <summary>
        /// Stage a tag's marker colour, for a tag of any registry. A fully transparent colour removes the marker.
        /// Markers are per user, so no lock is needed. Consecutive changes within one editor event (dragging in
        /// the colour picker) share one undo step.
        /// </summary>
        public static void SetMarker(TagGuid _Registry, TagGuid _Tag, Color _Color)
        {
            // Stored as saved, so a saved marker compares equal to the staged one.
            _Color = (Color32)_Color;

            var state = SyncState();
            int index = state.markers.FindIndex(e => e.Is(_Registry, _Tag));
            bool remove = _Color.a <= 0f;

            if (remove ? index < 0 : index >= 0 && state.markers[index].color == _Color) return;

            Undo.RecordObject(state, "Set Gameplay Tag Marker");
            Undo.SetCurrentGroupName("Set Gameplay Tag Marker");

            if (remove) state.markers.RemoveAt(index);
            else if (index >= 0) state.markers[index] = new TagMarker(_Registry, _Tag, _Color);
            else state.markers.Add(new TagMarker(_Registry, _Tag, _Color));

            state.revision = NextRevision();
            Undo.FlushUndoRecordObjects();

            // The registries didn't change, so they are already up to date with this revision.
            appliedRevision = state.revision;

            StateChanged?.Invoke();
            MarkersChanged?.Invoke();
        }
        #endregion



        #region Transaction
        /// <summary>
        /// Stage one operation: lock, copy, change, validate, record Undo, reload.
        /// </summary>
        private static TagOperationResult Apply(string _UndoName, Func<TagRegistryData, TagOperationResult> _Change)
        {
            if (!TryBegin(out var state, out string error))
                return TagOperationResult.Fail(error);

            var working = state.data.Clone();
            var result = _Change(working);
            if (!result.Success) return result;

            var errors = new List<string>();
            TagRegistryFile.Validate(working, errors);
            if (errors.Count > 0)
                return TagOperationResult.Fail(string.Join("\n", errors));

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(_UndoName);
            Undo.RecordObject(state, _UndoName);

            state.data = working;
            state.revision = NextRevision();
            Undo.FlushUndoRecordObjects();

            Reload(state);
            StateChanged?.Invoke();
            return result;
        }

        private static bool TryBegin(out TagRegistryState _State, out string _Error)
        {
            _State = null;

            if (!File.Exists(MainPath))
            {
                _Error = "There is no main registry yet. Create it in the Gameplay Tag Manager.";
                return false;
            }

            var parsed = TagRegistryFile.Parse(File.ReadAllText(MainPath));
            if (!parsed.IsValid)
            {
                _Error = $"The main registry has errors. Fix them before editing tags:\n{string.Join("\n", parsed.Errors)}";
                return false;
            }

            if (parsed.Data.kind != TagRegistryKind.Main)
            {
                _Error = $"\"{MainPath}\" is not a main registry.";
                return false;
            }

            if (!TagLockProvider.Current.TryAcquire(LockedPaths, out string lockError))
            {
                _Error = LockMessage(lockError);
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
        private static TagRegistryState SyncState()
        {
            var state = TagRegistryState.FindOrCreate();

            if (state.path != MainPath)
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
        /// Load the registry content from disk, and clear the undo history.
        /// </summary>
        /// <param name="_KeepMarkers"> Keep the staged markers, or load the saved ones. </param>
        private static void LoadFromDisk(TagRegistryState _State, bool _KeepMarkers)
        {
            string text = File.Exists(MainPath) ? File.ReadAllText(MainPath) : null;
            var parsed = text != null ? TagRegistryFile.Parse(text) : null;
            bool samePath = _State.path == MainPath;

            Undo.ClearUndo(_State);
            _State.path = MainPath;

            if (parsed != null && parsed.IsValid)
                _State.data = parsed.Data;
            else if (parsed == null || !samePath)
                _State.data = new TagRegistryData();
            // An invalid file keeps the content loaded before, like the registry service.

            if (!_KeepMarkers || !samePath)
                _State.markers = new List<TagMarker>(SavedMarkers);

            _State.revision = NextRevision();

            DiskHash = text != null ? TagRegistryFile.HashText(text) : string.Empty;
            DataHash = TagRegistryFile.HashText(TagRegistryFile.Write(_State.data));
            InvalidatePendingCache();
        }

        /// <summary>
        /// Publish the staged state to every tag view.
        /// </summary>
        private static void Reload(TagRegistryState _State)
        {
            appliedRevision = _State.revision;
            TagRegistryService.Reload();
        }

        /// <summary>
        /// Write to a temporary file, then swap it in, so a crash never leaves a half-written registry.
        /// </summary>
        internal static void WriteAtomic(string _Path, string _Text)
        {
            string target = Path.GetFullPath(_Path);
            string tempFolder = Path.GetFullPath(Path.Combine("Temp", "GameplayTags"));
            string temp = Path.Combine(tempFolder, Path.GetFileName(_Path) + ".tmp");

            Directory.CreateDirectory(tempFolder);
            File.WriteAllText(temp, _Text, new UTF8Encoding(false));

            if (!File.Exists(target))
            {
                File.Move(temp, target);
                return;
            }

            // Right after an import, the file can still be open for a moment (importer, indexer, antivirus),
            // which makes the swap fail on Windows. Retry briefly, then fall back to copying over it.
            const int ATTEMPTS = 5;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Replace(temp, target, null);
                    return;
                }
                catch (IOException) when (attempt < ATTEMPTS)
                {
                    System.Threading.Thread.Sleep(20 * attempt);
                }
                catch (IOException exception)
                {
                    Debug.LogWarning($"Could not swap \"{_Path}\" in atomically ({exception.Message}). It was overwritten in place.");
                    File.Copy(temp, target, true);
                    File.Delete(temp);
                    return;
                }
            }
        }

        internal static void EnsureFolder(string _Folder)
        {
            if (string.IsNullOrEmpty(_Folder) || AssetDatabase.IsValidFolder(_Folder)) return;

            string parent = Path.GetDirectoryName(_Folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(_Folder));
        }

        private static string LockMessage(string _LockError)
            => $"The gameplay tag lock ({TagLockProvider.Current.Name}) is not available. {_LockError}".TrimEnd();
        #endregion



        #region Undo
        /// <summary>
        /// Undo and redo restore the staged state. Nothing is written: the registries are only reloaded with it.
        /// </summary>
        private static void OnUndoRedoPerformed()
        {
            var state = CurrentState;
            if (state == null || state.revision == appliedRevision) return;

            InvalidatePendingCache();
            Reload(state);

            StateChanged?.Invoke();
            MarkersChanged?.Invoke();
        }

        /// <summary>
        /// Called when the main registry was imported. If something other than <see cref="Save"/> changed it
        /// while no registry edits are staged, the state follows the file and its undo history is dropped.
        /// Staged edits are kept, and <see cref="Save"/> is refused until they are discarded.
        /// </summary>
        internal static void OnMainRegistryImported()
        {
            var state = CurrentState;
            if (state == null || !File.Exists(MainPath)) return;
            if (ReadDiskHash() == DiskHash) return;

            UpdatePendingCache(state);
            if (!pendingData)
            {
                LoadFromDisk(state, true);
                appliedRevision = state.revision;
            }

            StateChanged?.Invoke();
        }
        #endregion



        #region Helpers
        /// <summary>
        /// What <see cref="AddPath"/> did with the last segment of the path.
        /// </summary>
        internal enum AddPathOutcome
        {
            /// <summary> A new row was created. </summary>
            Created,

            /// <summary> An implicit row was made explicit, keeping its ID. </summary>
            MadeExplicit,

            /// <summary> An explicit row already existed. Nothing was changed. </summary>
            ExistedExplicit,
        }

        /// <summary>
        /// Add a path to registry content, creating missing parents as implicit rows. The path must be valid.
        /// </summary>
        /// <param name="_Description"> Description of a created or newly explicit row. <see langword="null"/> keeps the current one. </param>
        /// <param name="_PreferredId">
        /// ID for a created row at <paramref name="_Path"/>, if no row uses it yet. Otherwise a new ID. To keep the
        /// IDs of parents too, add shorter paths first: a parent created implicitly gets a new ID.
        /// </param>
        /// <returns> ID of the row at <paramref name="_Path"/>. </returns>
        internal static TagGuid AddPath(TagRegistryData _Data, string _Path, string _Description, out AddPathOutcome _Outcome, TagGuid _PreferredId = default)
        {
            string[] segments = _Path.Split(TagPath.SEPARATOR);
            var parent = TagGuid.Zero;
            _Outcome = AddPathOutcome.Created;

            for (int i = 0; i < segments.Length; i++)
            {
                bool isLast = i == segments.Length - 1;
                int index = FindChild(_Data, parent, segments[i]);

                if (index >= 0)
                {
                    var row = _Data.rows[index];
                    if (isLast)
                    {
                        if (!row.isImplicit)
                        {
                            _Outcome = AddPathOutcome.ExistedExplicit;
                            return row.id;
                        }

                        row.isImplicit = false;
                        if (_Description != null)
                            row.description = _Description;

                        _Data.rows[index] = row;
                        _Outcome = AddPathOutcome.MadeExplicit;
                    }

                    parent = row.id;
                    continue;
                }

                var id = isLast && !_PreferredId.IsZero && _Data.IndexOf(_PreferredId) < 0 ? _PreferredId : NewUniqueId(_Data);
                _Data.rows.Add(new TagRegistryRow(id, parent, segments[i], !isLast, isLast ? _Description : null));
                parent = id;
            }

            return parent;
        }

        /// <summary>
        /// Remove a row and all of its descendants.
        /// </summary>
        internal static void RemoveSubtree(TagRegistryData _Data, TagGuid _Root)
        {
            if (_Data.IndexOf(_Root) < 0) return;

            var subtree = CollectSubtree(_Data, _Root);
            _Data.rows.RemoveAll(e => subtree.Contains(e.id));
        }

        private static int FindChild(TagRegistryData _Data, TagGuid _Parent, string _Segment)
        {
            for (int i = 0; i < _Data.rows.Count; i++)
                if (_Data.rows[i].parent.Equals(_Parent) && _Data.rows[i].segment == _Segment)
                    return i;

            return -1;
        }

        private static bool HasChildren(TagRegistryData _Data, TagGuid _Parent)
        {
            foreach (var row in _Data.rows)
                if (row.parent.Equals(_Parent))
                    return true;

            return false;
        }

        private static HashSet<TagGuid> CollectSubtree(TagRegistryData _Data, TagGuid _Root)
        {
            var result = new HashSet<TagGuid> { _Root };

            bool added = true;
            while (added)
            {
                added = false;
                foreach (var row in _Data.rows)
                    if (result.Contains(row.parent) && result.Add(row.id))
                        added = true;
            }

            return result;
        }

        private static TagGuid NewUniqueId(TagRegistryData _Data)
        {
            TagGuid id;
            do id = TagGuid.NewGuid();
            while (_Data.IndexOf(id) >= 0);

            return id;
        }
        #endregion
    }
}
