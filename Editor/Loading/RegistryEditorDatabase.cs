using System;
using System.IO;

using UnityEditor;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Connects a kind's editor tooling to its runtime database type.
    /// </summary>
    public static class RegistryEditorDatabase
    {
        /// <summary>
        /// Make <paramref name="_EnsureLoaded"/> the way <c>TDatabase.Active</c> gets an editor database when none
        /// is active. Call it once, when the editor loads.
        /// </summary>
        public static void Hook<TDatabase, TRow>(Action _EnsureLoaded)
            where TDatabase : RegistryDatabase<TDatabase, TRow>
            where TRow : struct
        {
            RegistryDatabase<TDatabase, TRow>.EditorEnsureActive = _EnsureLoaded;
        }

        /// <summary>
        /// Make a freshly built editor database active, if the active one is the previous editor database or
        /// none (tests may install their own), and destroy the previous one. A previous database that was active
        /// lives until the <c>ActiveChanged</c> listeners have returned.
        /// </summary>
        public static void Publish<TDatabase, TRow>(RegistryDatabase _Database, RegistryDatabase _Previous)
            where TDatabase : RegistryDatabase<TDatabase, TRow>
            where TRow : struct
        {
            var database = (TDatabase)_Database;
            var active = RegistryDatabase<TDatabase, TRow>.Active;
            bool previousWasActive = _Previous != null && active == _Previous;

            if (active == null || previousWasActive)
                RegistryDatabase<TDatabase, TRow>.SetActive(database, _DestroyPrevious: true);

            if (_Previous != null && !previousWasActive)
                UnityEngine.Object.DestroyImmediate(_Previous);
        }
    }



    /// <summary>
    /// Asset database helpers for registry tooling.
    /// </summary>
    public static class RegistryAssets
    {
        /// <summary>
        /// Create a project folder and its missing parents, through the asset database.
        /// </summary>
        public static void EnsureFolder(string _Folder)
        {
            if (string.IsNullOrEmpty(_Folder) || AssetDatabase.IsValidFolder(_Folder)) return;

            string parent = Path.GetDirectoryName(_Folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(_Folder));
        }
    }



    /// <summary>
    /// Reloads a kind's registries when one of its registry files is imported, deleted or moved. Read-only.
    /// </summary>
    internal sealed class RegistryPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] _ImportedAssets,
            string[] _DeletedAssets,
            string[] _MovedAssets,
            string[] _MovedFromAssetPaths,
            bool _DidDomainReload)
        {
            foreach (var kind in RegistryKinds.All)
            {
                if (!Contains(kind, _ImportedAssets) && !Contains(kind, _DeletedAssets)
                    && !Contains(kind, _MovedAssets) && !Contains(kind, _MovedFromAssetPaths))
                    continue;

                kind.Reload();

                // A main registry changed by something other than a save (a pull, a revert):
                // its undo history no longer matches the file.
                foreach (string path in _ImportedAssets)
                    if (path == kind.MainRegistryPath)
                        kind.OnMainRegistryImported();
            }
        }

        private static bool Contains(RegistryKind _Kind, string[] _Paths)
        {
            foreach (string path in _Paths)
                if (_Kind.IsRegistryPath(path))
                    return true;

            return false;
        }
    }
}
