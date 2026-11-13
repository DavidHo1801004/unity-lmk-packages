using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEngine;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// The one staging session shared by every registry kind. <see cref="Save"/> writes every changed registry of
    /// every kind, then their generated files, as one action under one lock request; <see cref="Discard"/> drops
    /// every staged edit. Undo and redo of any kind's edits go through here.
    /// </summary>
    [InitializeOnLoad]
    public static class RegistrySession
    {
        private const string SESSION_COUNTER = "LMK.Registry.RevisionCounter";

        /// <summary>
        /// Called after the staged state of any kind changed: an edit, an undo or redo, a save or a discard.
        /// </summary>
        public static event Action StateChanged;

        /// <summary>
        /// Called after a save wrote files, once every kind is reloaded and its generated files are written.
        /// </summary>
        public static event Action Saved;

        /// <summary>
        /// Checks that tell quitting not to prompt, e.g. "a registry window is open and asks itself".
        /// </summary>
        public static readonly List<Func<bool>> QuitPromptSuppressors = new();

        static RegistrySession()
        {
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorApplication.wantsToQuit += OnWantsToQuit;
        }

        /// <summary>
        /// Whether any kind has staged edits that are not saved.
        /// </summary>
        public static bool HasPendingChanges => RegistryKinds.All.Any(e => e.HasPendingChanges);

        /// <summary>
        /// The kinds with unsaved staged edits.
        /// </summary>
        public static IReadOnlyList<RegistryKind> PendingKinds => RegistryKinds.All.Where(e => e.HasPendingChanges).ToList();

        /// <summary>
        /// Whether any kind's main registry changed on disk while it has unsaved edits. <see cref="Save"/> is
        /// refused then.
        /// </summary>
        public static bool DiskChangedWhilePending => RegistryKinds.All.Any(e => e.DiskChangedWhilePending);

        internal static int NextRevision()
        {
            int revision = SessionState.GetInt(SESSION_COUNTER, 0) + 1;
            SessionState.SetInt(SESSION_COUNTER, revision);

            return revision;
        }

        internal static void RaiseStateChanged() => StateChanged?.Invoke();



        /// <summary>
        /// Write every staged edit: each changed main registry, the markers, then the generated files.
        /// Refused as a whole, before anything is written, if any registry changed on disk since its edits began,
        /// is invalid, or if the lock is not available for every file.
        /// </summary>
        public static RegistryResult Save()
        {
            var kinds = PendingKinds;
            if (kinds.Count == 0) return RegistryResult.Ok();

            var dataKinds = kinds.Where(e => e.HasPendingData).ToList();
            var markerKinds = kinds.Where(e => e.HasPendingMarkers).ToList();

            foreach (var kind in dataKinds)
            {
                string error = kind.CheckSave();
                if (error != null) return RegistryResult.Fail(error);
            }

            if (dataKinds.Count > 0)
            {
                var paths = dataKinds.SelectMany(e => e.LockedPaths).Distinct().ToList();
                if (!RegistryLockProvider.Current.TryAcquire(paths, out string lockError))
                    return RegistryResult.Fail(RegistryLockProvider.RefusedMessage(lockError));
            }

            var written = dataKinds.Select(e => e.WriteStaged()).ToList();

            foreach (var kind in markerKinds)
                kind.SaveMarkers();

            foreach (string path in written)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            foreach (var kind in kinds)
                kind.AfterSave();

            // Registries first, generated files last.
            foreach (var kind in dataKinds)
                kind.InvokeOnSaved();

            if (written.Count > 0)
                Saved?.Invoke();

            RaiseStateChanged();
            return RegistryResult.Ok();
        }

        /// <summary>
        /// Drop every staged edit of every kind: load the main registries and the markers from disk again, and
        /// clear their undo history.
        /// </summary>
        public static void Discard()
        {
            foreach (var kind in RegistryKinds.All)
                kind.DiscardStaged();

            RaiseStateChanged();
        }



        /// <summary>
        /// Undo and redo restore the staged state. Nothing is written: the registries are only reloaded with it.
        /// </summary>
        private static void OnUndoRedoPerformed()
        {
            bool changed = false;
            foreach (var kind in RegistryKinds.All)
            {
                kind.OnUndoRedo();
                changed = true;
            }

            if (changed)
                RaiseStateChanged();
        }

        /// <summary>
        /// Quitting with staged edits asks to save them, unless a registry window will ask itself.
        /// </summary>
        private static bool OnWantsToQuit()
        {
            if (Application.isBatchMode || !HasPendingChanges) return true;
            if (QuitPromptSuppressors.Any(e => e())) return true;

            string names = string.Join(" and ", PendingKinds.Select(e => e.DisplayName));
            int choice = EditorUtility.DisplayDialogComplex(
                "Unsaved Registry Changes",
                $"{names} have unsaved changes. Save them before quitting?",
                "Save", "Cancel", "Discard");

            switch (choice)
            {
                case 0:
                    var result = Save();
                    if (result.Success) return true;

                    EditorUtility.DisplayDialog("Could Not Save", result.Error, "OK");
                    return false;

                case 2:
                    Discard();
                    return true;

                default:
                    return false;
            }
        }
    }



    /// <summary>
    /// The transaction API a kind's operations are written against.
    /// </summary>
    public static class RegistryEdit
    {
        /// <summary>
        /// Stage one operation on a kind's main registry: lock check, Undo record, change on a staged copy,
        /// the kind's validation, then publication of the new snapshot.
        /// </summary>
        public static RegistryResult Apply<TRow, TAsset>(RegistryKindEditor<TRow, TAsset> _Kind, string _UndoName,
                                                         Func<RegistryData<TRow>, RegistryResult> _Change)
            where TRow : struct
            where TAsset : RegistryAsset<TRow>
        {
            if (_Kind == null) throw new ArgumentNullException(nameof(_Kind));
            return _Kind.Apply(_UndoName, _Change);
        }
    }
}
