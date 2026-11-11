using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using UnityEditor;
using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Loads every registry in the project and its packages, publishes an immutable
    /// <see cref="TagRegistrySnapshot"/>, and keeps the editor's <see cref="GameplayTagDatabase.Active"/> in sync.
    /// </summary>
    /// <remarks>
    /// Read-only: loading never writes a file. Reloads happen when registry files are imported, deleted
    /// or moved, and when staged tag edits change. <br/>
    /// The published snapshot shows the main registry's staged edits (<see cref="TagOperations"/>), so every tag
    /// view shows them before they are saved. <see cref="SavedSnapshot"/> holds only saved content, for code
    /// generation and builds. While a player is being built, the published snapshot holds only saved content too.
    /// </remarks>
    [InitializeOnLoad]
    internal static class TagRegistryService
    {
        public const string ROOT_FOLDER = "Assets/GameplayTags";

        public const string MAIN_REGISTRY_PATH = ROOT_FOLDER + "/Main." + TagRegistryFile.EXTENSION;

        /// <summary>
        /// Where the main registry lives. Only tests change it.
        /// </summary>
        internal static string MainRegistryPath { get; set; } = MAIN_REGISTRY_PATH;

        private static readonly string[] SEARCH_FOLDERS = { "Assets", "Packages" };

        /// <summary>
        /// Called after a new snapshot was published.
        /// </summary>
        public static event Action OnSnapshotChanged;

        private static TagRegistrySnapshot snapshot;
        private static TagRegistrySnapshot savedSnapshot;
        private static List<TagRegistrySnapshot.Source> savedSources;
        private static GameplayTagDatabase editorDatabase;
        private static bool reloading;

        // Last registry content that loaded, per file, used while a file is rejected.
        private static readonly Dictionary<string, TagRegistrySnapshot.Source> lastGood = new();



        static TagRegistryService()
        {
            GameplayTagDatabase.EditorEnsureActive = EnsureLoaded;
        }

        /// <summary>
        /// The current snapshot. Loads the registries on first access.
        /// </summary>
        public static TagRegistrySnapshot Snapshot
        {
            get
            {
                EnsureLoaded();
                return Volatile.Read(ref snapshot) ?? TagRegistrySnapshot.Empty;
            }
        }

        /// <summary>
        /// The registries as saved on disk, without staged edits. The same as <see cref="Snapshot"/> when
        /// nothing is staged.
        /// </summary>
        public static TagRegistrySnapshot SavedSnapshot
        {
            get
            {
                var current = Snapshot;
                if (savedSources == null) return current;

                return savedSnapshot ??= TagRegistrySnapshot.Build(savedSources, MainRegistryPath);
            }
        }

        /// <summary>
        /// Why generated registries are not loaded: one message per registry whose package isn't installed.
        /// </summary>
        public static IReadOnlyList<string> InactiveRegistries { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// IDs of the generated registries that are not loaded because their package isn't installed. References to
        /// them belong to that package, not to another project.
        /// </summary>
        public static IReadOnlyCollection<TagGuid> InactiveRegistryIds { get; private set; } = Array.Empty<TagGuid>();

        public static void EnsureLoaded()
        {
            if (Volatile.Read(ref snapshot) == null)
                Reload();
        }

        /// <summary>
        /// Load every registry asset again, and publish a new snapshot and editor database.
        /// </summary>
        public static void Reload()
        {
            if (reloading) return;
            reloading = true;

            try
            {
                var sources = new List<TagRegistrySnapshot.Source>();
                var paths = new HashSet<string>();

                foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(TagRegistryAsset)}", SEARCH_FOLDERS))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!paths.Add(path)) continue;

                    var asset = AssetDatabase.LoadAssetAtPath<TagRegistryAsset>(path);
                    if (asset == null) continue;

                    var source = CreateSource(path, asset);
                    if (source != null)
                        sources.Add(source);
                }

                // Forget files that no longer exist.
                var removed = new List<string>();
                foreach (string path in lastGood.Keys)
                    if (!paths.Contains(path))
                        removed.Add(path);

                foreach (string path in removed)
                    lastGood.Remove(path);

                // Generated registries load only while their package is installed.
                var installed = PackageRegistryService.GetInstalledPackages();
                var inactive = sources.Where(e => !PackageRegistryService.IsActive(e.Data, installed)).ToList();
                sources.RemoveAll(inactive.Contains);
                InactiveRegistries = inactive
                    .Select(e => $"\"{e.AssetPath}\" is not loaded: its package \"{e.Data.package}\" isn't installed.")
                    .ToList();
                InactiveRegistryIds = new HashSet<TagGuid>(inactive.Select(e => e.Data.registryId));

                var published = WithPendingMain(sources);
                savedSources = published == sources ? null : sources;
                savedSnapshot = null;

                Publish(TagRegistrySnapshot.Build(published, MainRegistryPath));
            }
            finally
            {
                reloading = false;
            }
        }

        /// <summary>
        /// The sources with the main registry's content replaced by its staged edits, if there are any.
        /// </summary>
        private static List<TagRegistrySnapshot.Source> WithPendingMain(List<TagRegistrySnapshot.Source> _Sources)
        {
            if (BuildPipeline.isBuildingPlayer || !TagOperations.TryGetPendingData(out var data)) return _Sources;

            int index = _Sources.FindIndex(e => e.AssetPath == MainRegistryPath);
            if (index < 0) return _Sources;

            var source = _Sources[index];
            var result = new List<TagRegistrySnapshot.Source>(_Sources)
            {
                [index] = new TagRegistrySnapshot.Source
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

            return result;
        }

        private static TagRegistrySnapshot.Source CreateSource(string _Path, TagRegistryAsset _Asset)
        {
            if (_Asset.IsValid)
            {
                var source = new TagRegistrySnapshot.Source
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

            Debug.LogWarning($"Gameplay tag registry \"{_Path}\" is invalid. The last version that loaded is used until it is fixed.");

            return new TagRegistrySnapshot.Source
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

        private static void Publish(TagRegistrySnapshot _Snapshot)
        {
            var database = _Snapshot.BuildDatabase();
            database.hideFlags = HideFlags.HideAndDontSave;

            var previousDatabase = editorDatabase;
            editorDatabase = database;
            Volatile.Write(ref snapshot, _Snapshot);

            // Only replace the active database if it is ours, so tests can install their own.
            if (GameplayTagDatabase.Active == null || GameplayTagDatabase.Active == previousDatabase)
                GameplayTagDatabase.SetActive(database);

            if (previousDatabase != null)
                UnityEngine.Object.DestroyImmediate(previousDatabase);

            if (EditorApplication.isPlaying)
                Debug.LogWarning("Gameplay tag registries changed during Play Mode. Tag containers rebuild their caches.");

            OnSnapshotChanged?.Invoke();
        }

        /// <summary>
        /// The editor database built from the current snapshot.
        /// </summary>
        internal static GameplayTagDatabase EditorDatabase
        {
            get
            {
                EnsureLoaded();
                return editorDatabase;
            }
        }
    }
}
