using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Which saved assets reference which tags, for the Delete dialog, Find References, Relink and Replace.
    /// </summary>
    /// <remarks>
    /// Kept only in <c>Library/</c>, never committed: asset GUID → (dependency hash, set of (registry, tag ID)).
    /// An entry is current while the asset's dependency hash matches. Every query scans the assets that are new or
    /// changed first, so results are accurate for saved data. After a <c>Library/</c> wipe, the first query scans
    /// everything. <br/>
    /// Saving an asset updates its entry from the objects being saved, without a scan. Changes from outside (a pull,
    /// a revert) only mark entries stale: nothing is read or written until the next query. <br/>
    /// Assets are read by <see cref="TagAssetScanner"/>: as text when they are text-serialized, loaded
    /// otherwise. Values a prefab instance inherits from its prefab are listed for the prefab, not the instance.
    /// </remarks>
    [InitializeOnLoad]
    internal static class TagReferenceIndex
    {
        public const string DEFAULT_PATH = "Library/GameplayTags/RefIndex";

        private const string HEADER = "LMK.GameplayTags.RefIndex 1";
        private const string PROGRESS_TITLE = "Gameplay Tag References";
        private const string NO_VALUE = "-";

        // Fewer stale assets than this are scanned without a progress bar.
        private const int PROGRESS_THRESHOLD = 10;

        private sealed class Entry
        {
            /// <summary>
            /// Dependency hash the tags were read at. <see langword="null"/> when stale.
            /// </summary>
            public string Hash;

            /// <summary>
            /// Set when the tags were read from objects being saved: the dependency hash before the save. The tags
            /// are taken as current once the hash differs, i.e. once the saved file was imported.
            /// </summary>
            public string PendingFrom;

            public HashSet<TagKey> Tags = new();
        }

        /// <summary>
        /// Where the index is stored. Only tests change it.
        /// </summary>
        internal static string IndexPath { get; set; } = DEFAULT_PATH;

        /// <summary>
        /// Number of assets the last query had to scan.
        /// </summary>
        internal static int LastScanCount { get; private set; }

        /// <summary>
        /// Replaces the progress bar, for tests: called with the asset being scanned and the progress, returns
        /// whether to cancel.
        /// </summary>
        internal static Func<string, float, bool> ProgressOverride { get; set; }

        // By asset GUID. Loaded from loadedPath on first use.
        private static Dictionary<string, Entry> entries;
        private static string loadedPath;
        private static bool dirty;
        private static bool flushScheduled;



        static TagReferenceIndex()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Flush;
            EditorApplication.quitting += Flush;
        }

        private static Dictionary<string, Entry> Entries
        {
            get
            {
                if (entries != null && loadedPath == IndexPath) return entries;

                Flush();
                loadedPath = IndexPath;
                entries = Load(loadedPath);
                dirty = false;

                return entries;
            }
        }

        /// <summary>
        /// Whether assets at <paramref name="_Path"/> are indexed: project assets that can hold serialized tags.
        /// </summary>
        public static bool IsIndexed(string _Path)
            => !string.IsNullOrEmpty(_Path)
            && _Path.StartsWith("Assets/", StringComparison.Ordinal)
            && TagAssetScanner.CanHoldTags(_Path);



        #region Queries
        /// <summary>
        /// Saved assets with a reference matching <paramref name="_Match"/>, sorted by path. Stale entries are scanned
        /// first.
        /// </summary>
        /// <returns> Whether every stale entry was scanned. False if the user cancelled the scan. </returns>
        public static bool TryFindAssets(Func<TagKey, bool> _Match, bool _ShowProgress, out List<string> _AssetPaths)
        {
            bool complete = Refresh(_ShowProgress);

            _AssetPaths = new List<string>();
            foreach (var pair in Entries)
            {
                if (!pair.Value.Tags.Any(_Match)) continue;

                string path = AssetDatabase.GUIDToAssetPath(pair.Key);
                if (!string.IsNullOrEmpty(path))
                    _AssetPaths.Add(path);
            }

            _AssetPaths.Sort(StringComparer.Ordinal);
            return complete;
        }

        /// <summary>
        /// Saved assets referencing any of <paramref name="_Tags"/>, sorted by path.
        /// </summary>
        /// <returns> Whether every stale entry was scanned. False if the user cancelled the scan. </returns>
        public static bool TryFindAssets(IReadOnlyCollection<GameplayTag> _Tags, bool _ShowProgress, out List<string> _AssetPaths)
        {
            var ids = new HashSet<TagGuid>(_Tags.Select(e => e.Id));
            return TryFindAssets(key => ids.Contains(key.Id) && _Tags.Any(key.Matches), _ShowProgress, out _AssetPaths);
        }

        /// <summary>
        /// The tags an asset references, as last indexed. Empty if it isn't indexed.
        /// </summary>
        public static IReadOnlyCollection<TagKey> GetTags(string _AssetPath)
        {
            string guid = AssetDatabase.AssetPathToGUID(_AssetPath);
            return !string.IsNullOrEmpty(guid) && Entries.TryGetValue(guid, out var entry)
                ? entry.Tags
                : (IReadOnlyCollection<TagKey>)Array.Empty<TagKey>();
        }

        /// <summary>
        /// Bring the index up to date: forget deleted assets and scan the new or changed ones.
        /// </summary>
        /// <returns> Whether every stale entry was scanned. False if the user cancelled the scan. </returns>
        public static bool Refresh(bool _ShowProgress)
        {
            var index = Entries;
            var candidates = GetCandidates();
            bool changed = false;

            foreach (string guid in index.Keys.Where(e => !candidates.ContainsKey(e)).ToList())
            {
                index.Remove(guid);
                changed = true;
            }

            var stale = new List<(string Guid, string Path)>();
            foreach (var pair in candidates)
            {
                if (index.TryGetValue(pair.Key, out var entry))
                {
                    string hash = GetHash(pair.Value);
                    if (entry.Hash != null && entry.Hash == hash) continue;

                    if (entry.PendingFrom != null && hash != null && hash != entry.PendingFrom)
                    {
                        entry.Hash = hash;
                        entry.PendingFrom = null;
                        changed = true;
                        continue;
                    }
                }

                stale.Add((pair.Key, pair.Value));
            }

            LastScanCount = 0;
            bool cancelled = false;
            bool showBar = _ShowProgress && ProgressOverride == null && stale.Count >= PROGRESS_THRESHOLD;

            try
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    var (guid, path) = stale[i];

                    if (_ShowProgress && ReportProgress(path, i / (float)stale.Count, showBar))
                    {
                        cancelled = true;
                        break;
                    }

                    string hash = GetHash(path);
                    var tags = new HashSet<TagKey>();
                    bool complete = Scan(path, tags);

                    // An asset that couldn't be read fully stays stale, so the next query tries again.
                    index[guid] = new Entry { Hash = complete ? hash : null, Tags = tags };
                    LastScanCount++;
                    changed = true;
                }
            }
            finally
            {
                if (showBar)
                    EditorUtility.ClearProgressBar();
            }

            if (changed)
                MarkDirty();

            return !cancelled;
        }

        /// <summary>
        /// Forget everything, as after a <c>Library/</c> wipe. The next query scans every asset.
        /// </summary>
        public static void Clear()
        {
            // Another index is loaded: keep its changes.
            if (loadedPath != IndexPath)
                Flush();

            entries = new Dictionary<string, Entry>();
            loadedPath = IndexPath;
            dirty = false;

            if (File.Exists(IndexPath))
                File.Delete(IndexPath);
        }

        /// <summary>
        /// Forget everything, then scan every asset.
        /// </summary>
        public static bool Rebuild(bool _ShowProgress)
        {
            Clear();
            return Refresh(_ShowProgress);
        }

        private static bool ReportProgress(string _Path, float _Progress, bool _ShowBar)
        {
            if (ProgressOverride != null)
                return ProgressOverride(_Path, _Progress);

            return _ShowBar && EditorUtility.DisplayCancelableProgressBar(PROGRESS_TITLE, $"Scanning {_Path}", _Progress);
        }

        /// <summary>
        /// Indexed assets, by GUID.
        /// </summary>
        private static Dictionary<string, string> GetCandidates()
        {
            var result = new Dictionary<string, string>();
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!IsIndexed(path)) continue;

                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid) && !AssetDatabase.IsValidFolder(path))
                    result[guid] = path;
            }

            return result;
        }

        private static string GetHash(string _Path)
        {
            var hash = AssetDatabase.GetAssetDependencyHash(_Path);
            return hash.isValid ? hash.ToString() : null;
        }
        #endregion



        #region Scanning
        /// <summary>
        /// Read the tag keys an asset file references.
        /// </summary>
        /// <returns> Whether the asset could be read completely. </returns>
        internal static bool Scan(string _Path, HashSet<TagKey> _Result)
        {
            var tags = new List<GameplayTag>();
            bool complete = TagAssetScanner.Scan(_Path, tags);

            foreach (var tag in tags)
                _Result.Add(TagKey.Of(tag));

            return complete;
        }

        private static HashSet<TagKey> Collect(IEnumerable<UnityEngine.Object> _Objects)
        {
            var tags = new List<GameplayTag>();
            TagAssetScanner.Collect(_Objects, tags);

            return new HashSet<TagKey>(tags.Select(TagKey.Of));
        }
        #endregion



        #region Asset Events
        /// <summary>
        /// Update the entries of assets about to be saved from their loaded objects, so saving needs no scan.
        /// Prefabs are left to the next query: in Prefab Mode, the prefab asset isn't what gets saved.
        /// </summary>
        internal static void OnWillSaveAssets(string[] _Paths)
        {
            foreach (string path in _Paths)
            {
                if (!IsIndexed(path) || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;

                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) continue;

                HashSet<TagKey> tags;
                if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    var scene = SceneManager.GetSceneByPath(path);
                    if (!scene.isLoaded) continue;

                    tags = Collect(TagPropertyWalker.GetSceneObjects(scene));
                }
                else
                {
                    tags = Collect(TagPropertyWalker.GetAssetObjects(path));
                }

                Entries[guid] = new Entry { PendingFrom = GetHash(path) ?? string.Empty, Tags = tags };
                MarkDirty();
            }
        }

        /// <summary>
        /// Imported assets: confirm entries written by a save, and mark the others stale. Deleted assets are
        /// forgotten. Nothing is read here.
        /// </summary>
        internal static void OnAssetsChanged(string[] _Imported, string[] _Deleted)
        {
            // Not loaded yet: the next query compares the hashes anyway.
            if (entries == null || loadedPath != IndexPath) return;

            bool changed = false;

            foreach (string path in _Imported)
            {
                if (!IsIndexed(path)) continue;

                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid) || !entries.TryGetValue(guid, out var entry)) continue;

                string hash = GetHash(path);
                if (entry.PendingFrom != null)
                {
                    if (hash == null || hash == entry.PendingFrom) continue;

                    entry.Hash = hash;
                    entry.PendingFrom = null;
                    changed = true;
                }
                else if (entry.Hash != null && entry.Hash != hash)
                {
                    entry.Hash = null;
                    changed = true;
                }
            }

            foreach (string path in _Deleted)
            {
                if (!IsIndexed(path)) continue;

                string guid = AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.IncludeRecentlyDeletedAssets);
                if (!string.IsNullOrEmpty(guid) && entries.Remove(guid))
                    changed = true;
            }

            if (changed)
                MarkDirty();
        }
        #endregion



        #region Storage
        private static void MarkDirty()
        {
            dirty = true;
            if (flushScheduled) return;

            flushScheduled = true;
            EditorApplication.delayCall += () =>
            {
                flushScheduled = false;
                Flush();
            };
        }

        /// <summary>
        /// Write the index to <c>Library/</c>, if it changed.
        /// </summary>
        internal static void Flush()
        {
            if (!dirty || entries == null || string.IsNullOrEmpty(loadedPath)) return;
            dirty = false;

            var text = new StringBuilder(HEADER).Append('\n');
            foreach (var pair in entries)
            {
                text.Append(pair.Key)
                    .Append('\t').Append(pair.Value.Hash ?? NO_VALUE)
                    .Append('\t').Append(pair.Value.PendingFrom == null ? NO_VALUE : pair.Value.PendingFrom.Length == 0 ? "0" : pair.Value.PendingFrom)
                    .Append('\t').Append(string.Join(",", pair.Value.Tags))
                    .Append('\n');
            }

            try
            {
                string folder = Path.GetDirectoryName(loadedPath);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);

                string temp = loadedPath + ".tmp";
                File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));

                if (File.Exists(loadedPath))
                    File.Delete(loadedPath);
                File.Move(temp, loadedPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // A cache: the next query rebuilds what's missing.
                Debug.LogWarning($"Gameplay tag references: could not write \"{loadedPath}\" ({exception.Message}).");
            }
        }

        /// <summary>
        /// Read the index. A missing or unreadable file gives an empty index, which the next query fills.
        /// </summary>
        private static Dictionary<string, Entry> Load(string _Path)
        {
            var result = new Dictionary<string, Entry>();

            string[] lines;
            try
            {
                if (!File.Exists(_Path)) return result;
                lines = File.ReadAllLines(_Path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return result;
            }

            if (lines.Length == 0 || lines[0] != HEADER) return result;

            for (int i = 1; i < lines.Length; i++)
            {
                string[] fields = lines[i].Split('\t');
                if (fields.Length != 4 || string.IsNullOrEmpty(fields[0])) continue;

                var entry = new Entry
                {
                    Hash = fields[1] == NO_VALUE ? null : fields[1],
                    PendingFrom = fields[2] == NO_VALUE ? null : fields[2] == "0" ? string.Empty : fields[2],
                };

                foreach (string key in fields[3].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (TagKey.TryParse(key, out var tag))
                        entry.Tags.Add(tag);

                result[fields[0]] = entry;
            }

            return result;
        }
        #endregion
    }



    /// <summary>
    /// Keeps the reference index current when assets are saved.
    /// </summary>
    internal sealed class TagReferenceIndexSaveHook : UnityEditor.AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] _Paths)
        {
            TagReferenceIndex.OnWillSaveAssets(_Paths);
            return _Paths;
        }
    }



    /// <summary>
    /// Marks reference index entries stale when assets change on disk.
    /// </summary>
    internal sealed class TagReferenceIndexPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] _Imported, string[] _Deleted, string[] _Moved, string[] _MovedFrom)
            => TagReferenceIndex.OnAssetsChanged(_Imported, _Deleted);
    }
}
