using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// The identity of a stored reference, as the reference index keeps it: (kind ID, registry ID, row ID).
    /// A zero <see cref="Registry"/> means the registry is unknown (see <see cref="RegistryReference"/>).
    /// </summary>
    public readonly struct RegistryKey : IEquatable<RegistryKey>
    {
        public readonly string Kind;
        public readonly RegistryGuid Registry;
        public readonly RegistryGuid Id;

        public RegistryKey(string _Kind, RegistryGuid _Registry, RegistryGuid _Id)
        {
            Kind = _Kind ?? string.Empty;
            Registry = _Registry;
            Id = _Id;
        }

        public static RegistryKey Of(in RegistryReference _Reference) => new(_Reference.Kind?.Id, _Reference.Registry, _Reference.Id);

        public bool HasRegistry => !Registry.IsZero;

        /// <summary>
        /// Whether this key refers to row (<paramref name="_Registry"/>, <paramref name="_Id"/>) of kind <paramref name="_Kind"/>.
        /// </summary>
        public bool Matches(string _Kind, RegistryGuid _Registry, RegistryGuid _Id)
            => Kind == _Kind && Id == _Id && (Registry.IsZero || Registry == _Registry);

        public bool Matches(in RegistryReference _Reference) => Matches(_Reference.Kind?.Id, _Reference.Registry, _Reference.Id);

        public bool Equals(RegistryKey _Other) => Kind == _Other.Kind && Registry == _Other.Registry && Id == _Other.Id;

        public override bool Equals(object _Obj) => _Obj is RegistryKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Registry, Id);

        /// <summary>
        /// <c>kind:registry:id</c>. Kind IDs never contain ':'.
        /// </summary>
        public override string ToString() => $"{Kind}:{Registry}:{Id}";

        public static bool TryParse(string _Text, out RegistryKey _Key)
        {
            _Key = default;
            if (string.IsNullOrEmpty(_Text)) return false;

            string[] parts = _Text.Split(':');
            if (parts.Length != 3 || parts[0].Length == 0
                || !RegistryGuid.TryParse(parts[1], out var registry)
                || !RegistryGuid.TryParse(parts[2], out var id)
                || id.IsZero)
                return false;

            _Key = new RegistryKey(parts[0], registry, id);
            return true;
        }
    }



    /// <summary>
    /// Which saved assets reference which rows, of every kind: one index for the Delete dialogs, Find References,
    /// Relink and Replace of every registry kind.
    /// </summary>
    /// <remarks>
    /// Kept only in <c>Library/</c>, never committed: asset GUID → (dependency hash, set of (kind, registry, row ID)).
    /// An entry is current while the asset's dependency hash matches. Every query scans the assets that are new or
    /// changed first, so results are accurate for saved data. After a <c>Library/</c> wipe, or when the set of
    /// registered kinds changes, the first query scans everything. <br/>
    /// Saving an asset updates its entry from the objects being saved, without a scan. Changes from outside (a pull,
    /// a revert) only mark entries stale: nothing is read or written until the next query. <br/>
    /// Assets are read by <see cref="RegistryAssetScanner"/>. Values a prefab instance inherits from its prefab are
    /// listed for the prefab, not the instance.
    /// </remarks>
    [InitializeOnLoad]
    public static class RegistryReferenceIndex
    {
        public const string DEFAULT_PATH = "Library/LMKRegistry/RefIndex";

        private const string HEADER = "LMK.Registry.RefIndex 1";
        private const string PROGRESS_TITLE = "Registry References";
        private const string NO_VALUE = "-";

        // Fewer stale assets than this are scanned without a progress bar.
        private const int PROGRESS_THRESHOLD = 10;

        private sealed class Entry
        {
            /// <summary>
            /// Dependency hash the references were read at. <see langword="null"/> when stale.
            /// </summary>
            public string Hash;

            /// <summary>
            /// Set when the references were read from objects being saved: the dependency hash before the save. They
            /// are taken as current once the hash differs, i.e. once the saved file was imported.
            /// </summary>
            public string PendingFrom;

            public HashSet<RegistryKey> Keys = new();
        }

        /// <summary>
        /// Where the index is stored. Only tests change it, including the tests of domain packages.
        /// </summary>
        public static string IndexPath { get; set; } = DEFAULT_PATH;

        /// <summary>
        /// Number of assets the last query had to scan.
        /// </summary>
        public static int LastScanCount { get; private set; }

        /// <summary>
        /// Replaces the progress bar, for tests: called with the asset being scanned and the progress, returns
        /// whether to cancel.
        /// </summary>
        public static Func<string, float, bool> ProgressOverride { get; set; }

        // By asset GUID. Loaded from loadedPath on first use, for the kinds in loadedKinds.
        private static Dictionary<string, Entry> entries;
        private static string loadedPath;
        private static string loadedKinds;
        private static bool dirty;
        private static bool flushScheduled;



        static RegistryReferenceIndex()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Flush;
            EditorApplication.quitting += Flush;
        }

        /// <summary>
        /// The registered kinds, as the index records them: a text reference is placed by the kinds that exist when
        /// it is read, so entries read with other kinds are rescanned.
        /// </summary>
        private static string KindsSignature => string.Join(",", RegistryKinds.All.Select(e => e.Id).OrderBy(e => e, StringComparer.Ordinal));

        private static Dictionary<string, Entry> Entries
        {
            get
            {
                if (entries != null && loadedPath == IndexPath)
                {
                    if (loadedKinds == KindsSignature) return entries;

                    // Kinds changed since the entries were read: read everything again.
                    entries = new Dictionary<string, Entry>();
                    loadedKinds = KindsSignature;
                    MarkDirty();
                    return entries;
                }

                Flush();
                loadedPath = IndexPath;
                entries = Load(loadedPath, KindsSignature);
                loadedKinds = KindsSignature;
                dirty = false;

                return entries;
            }
        }

        /// <summary>
        /// Whether assets at <paramref name="_Path"/> are indexed: project assets that can hold serialized references.
        /// </summary>
        public static bool IsIndexed(string _Path)
            => !string.IsNullOrEmpty(_Path)
            && _Path.StartsWith("Assets/", StringComparison.Ordinal)
            && RegistryAssetScanner.CanHoldReferences(_Path);



        #region Queries
        /// <summary>
        /// Saved assets with a reference matching <paramref name="_Match"/>, sorted by path. Stale entries are scanned
        /// first.
        /// </summary>
        /// <returns> Whether every stale entry was scanned. False if the user cancelled the scan. </returns>
        public static bool TryFindAssets(Func<RegistryKey, bool> _Match, bool _ShowProgress, out List<string> _AssetPaths)
        {
            bool complete = Refresh(_ShowProgress);

            _AssetPaths = new List<string>();
            foreach (var pair in Entries)
            {
                if (!pair.Value.Keys.Any(_Match)) continue;

                string path = AssetDatabase.GUIDToAssetPath(pair.Key);
                if (!string.IsNullOrEmpty(path))
                    _AssetPaths.Add(path);
            }

            _AssetPaths.Sort(StringComparer.Ordinal);
            return complete;
        }

        /// <summary>
        /// Saved assets referencing any of the rows (<paramref name="_Kind"/>, registry, ID), sorted by path. Only that
        /// kind's references count: the same IDs in another kind are other rows.
        /// </summary>
        /// <returns> Whether every stale entry was scanned. False if the user cancelled the scan. </returns>
        public static bool TryFindAssets(RegistryKind _Kind, IReadOnlyCollection<(RegistryGuid Registry, RegistryGuid Id)> _Rows, bool _ShowProgress,
                                         out List<string> _AssetPaths)
        {
            var ids = new HashSet<RegistryGuid>(_Rows.Select(e => e.Id));
            return TryFindAssets(key => key.Kind == _Kind.Id && ids.Contains(key.Id) && _Rows.Any(row => key.Matches(_Kind.Id, row.Registry, row.Id)),
                                 _ShowProgress, out _AssetPaths);
        }

        /// <summary>
        /// The references an asset holds, as last indexed. Empty if it isn't indexed.
        /// </summary>
        public static IReadOnlyCollection<RegistryKey> GetReferences(string _AssetPath)
        {
            string guid = AssetDatabase.AssetPathToGUID(_AssetPath);
            return !string.IsNullOrEmpty(guid) && Entries.TryGetValue(guid, out var entry)
                ? entry.Keys
                : (IReadOnlyCollection<RegistryKey>)Array.Empty<RegistryKey>();
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
                    var keys = new HashSet<RegistryKey>();
                    bool complete = Scan(path, keys);

                    // An asset that couldn't be read fully stays stale, so the next query tries again.
                    index[guid] = new Entry { Hash = complete ? hash : null, Keys = keys };
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
            loadedKinds = KindsSignature;
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
        /// Read the keys of the references an asset file holds. References no kind could be found for are left out.
        /// </summary>
        /// <returns> Whether the asset could be read completely. </returns>
        internal static bool Scan(string _Path, HashSet<RegistryKey> _Result)
        {
            var references = new List<RegistryReference>();
            bool complete = RegistryAssetScanner.Scan(_Path, references);

            foreach (var reference in references)
                if (reference.Kind != null)
                    _Result.Add(RegistryKey.Of(reference));

            return complete;
        }

        private static HashSet<RegistryKey> Collect(IEnumerable<UnityEngine.Object> _Objects)
        {
            var references = new List<RegistryReference>();
            RegistryAssetScanner.Collect(_Objects, references);

            return new HashSet<RegistryKey>(references.Select(e => RegistryKey.Of(e)));
        }
        #endregion



        #region Asset Events
        /// <summary>
        /// Update the entries of assets about to be saved from their loaded objects, so saving needs no scan.
        /// Prefabs are left to the next query: in Prefab Mode, the prefab asset isn't what gets saved.
        /// </summary>
        internal static void OnWillSaveAssets(string[] _Paths)
        {
            if (RegistryKinds.All.Count == 0) return;

            foreach (string path in _Paths)
            {
                if (!IsIndexed(path) || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;

                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) continue;

                HashSet<RegistryKey> keys;
                if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    var scene = SceneManager.GetSceneByPath(path);
                    if (!scene.isLoaded) continue;

                    keys = Collect(RegistryPropertyWalker.GetSceneObjects(scene));
                }
                else
                {
                    keys = Collect(RegistryPropertyWalker.GetAssetObjects(path));
                }

                Entries[guid] = new Entry { PendingFrom = GetHash(path) ?? string.Empty, Keys = keys };
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
        public static void Flush()
        {
            if (!dirty || entries == null || string.IsNullOrEmpty(loadedPath)) return;
            dirty = false;

            var text = new StringBuilder(HEADER).Append('\t').Append(loadedKinds).Append('\n');
            foreach (var pair in entries)
            {
                text.Append(pair.Key)
                    .Append('\t').Append(pair.Value.Hash ?? NO_VALUE)
                    .Append('\t').Append(pair.Value.PendingFrom == null ? NO_VALUE : pair.Value.PendingFrom.Length == 0 ? "0" : pair.Value.PendingFrom)
                    .Append('\t').Append(string.Join(",", pair.Value.Keys))
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
                Debug.LogWarning($"Registry references: could not write \"{loadedPath}\" ({exception.Message}).");
            }
        }

        /// <summary>
        /// Read the index. A missing or unreadable file, or one read with other kinds, gives an empty index, which
        /// the next query fills.
        /// </summary>
        private static Dictionary<string, Entry> Load(string _Path, string _Kinds)
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

            if (lines.Length == 0 || lines[0] != $"{HEADER}\t{_Kinds}") return result;

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
                    if (RegistryKey.TryParse(key, out var parsed))
                        entry.Keys.Add(parsed);

                result[fields[0]] = entry;
            }

            return result;
        }
        #endregion
    }



    /// <summary>
    /// Keeps the reference index current when assets are saved.
    /// </summary>
    internal sealed class RegistryReferenceIndexSaveHook : UnityEditor.AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] _Paths)
        {
            RegistryReferenceIndex.OnWillSaveAssets(_Paths);
            return _Paths;
        }
    }



    /// <summary>
    /// Marks reference index entries stale when assets change on disk, and collects assets for import mapping.
    /// </summary>
    internal sealed class RegistryReferenceIndexPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] _Imported, string[] _Deleted, string[] _Moved, string[] _MovedFrom)
        {
            RegistryReferenceIndex.OnAssetsChanged(_Imported, _Deleted);
            RegistryImportMapping.OnAssetsImported(_Imported);
        }
    }
}
