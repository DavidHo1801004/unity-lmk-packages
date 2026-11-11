using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

using UnityEditor;
using UnityEngine;

using LMK.Runtime.GameplayTags;

using Debug = UnityEngine.Debug;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Outcome of <see cref="ProjectAssetImport.Map"/>.
    /// </summary>
    internal sealed class TagImportResult
    {
        public bool Success = true;

        /// <summary>
        /// The lock wasn't available, so the assets wait for <see cref="ProjectAssetImport.MapPending"/>.
        /// </summary>
        public bool Postponed;

        public string Error;

        /// <summary> Tags added to the main registry, as "path (ID)". </summary>
        public readonly List<string> AddedTags = new();

        /// <summary> References rewritten, one line each. </summary>
        public readonly List<string> Rewritten = new();

        /// <summary> References left as they are because they can't be mapped, one line each. </summary>
        public readonly List<string> Unmapped = new();

        public readonly List<string> Warnings = new();

        /// <summary> Assets checked. </summary>
        public int Assets;

        public double Seconds;

        /// <summary> The import log file, if one was written. </summary>
        public string LogPath;

        public string Message;
    }



    /// <summary>
    /// Maps the tag references of assets brought in from another project: references into that project's main
    /// registry, which this project doesn't have, are rewritten to tags of this project's main registry, by name.
    /// </summary>
    /// <remarks>
    /// Runs only for explicit imports: a <c>.unitypackage</c> import, or the Map Tags From Another Project command.
    /// Pulls, file copies and domain reloads never map anything. Requires the lock: without it, the assets
    /// wait until Map Imported Assets runs. Only the incoming assets are rewritten, and an import log lists
    /// every added tag and rewritten reference.
    /// <list type="bullet">
    /// <item>A reference to a loaded registry, or to a generated registry whose package isn't installed, is left as it is.</item>
    /// <item>A name that exists in the main registry is rewritten to that tag.</item>
    /// <item>A new name is added to the main registry, with the incoming ID if the main registry doesn't use it.</item>
    /// <item>A reference without a name is left as it is, and reported.</item>
    /// </list>
    /// </remarks>
    [InitializeOnLoad]
    internal static class ProjectAssetImport
    {
        public const string PENDING_PATH = "Library/GameplayTags/PendingImports";
        public const string UNDO_NAME = "Map Imported Gameplay Tags";

        private const string SESSION_ACTIVE = "LMK.GameplayTags.Import.Active";
        private const string SESSION_PATHS = "LMK.GameplayTags.Import.Paths";
        private const string MAP_MENU = "Assets/Gameplay Tags/Map Tags From Another Project...";
        private const int MAX_LISTED = 10;

        /// <summary>
        /// Where postponed assets wait for the lock. Only tests change it.
        /// </summary>
        internal static string PendingPath { get; set; } = PENDING_PATH;

        /// <summary>
        /// Where import logs are written. Only tests change it.
        /// </summary>
        internal static string LogFolder { get; set; } = "Logs/GameplayTags";



        static ProjectAssetImport()
        {
            AssetDatabase.importPackageStarted += OnImportStarted;
            AssetDatabase.importPackageCompleted += e => OnImportCompleted(e);
            AssetDatabase.importPackageCancelled += OnImportCancelled;
            AssetDatabase.importPackageFailed += (e, _) => OnImportCancelled(e);
        }



        #region Package Import Events
        /// <summary>
        /// Whether a <c>.unitypackage</c> import is under way, and its assets are being collected.
        /// </summary>
        public static bool IsCollecting => SessionState.GetBool(SESSION_ACTIVE, false);

        // Kept in SessionState, so a domain reload during the import doesn't lose them.
        private static List<string> Collected
        {
            get => SessionState.GetString(SESSION_PATHS, string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            set => SessionState.SetString(SESSION_PATHS, string.Join("\n", value));
        }

        internal static void OnImportStarted(string _Package)
        {
            SessionState.SetBool(SESSION_ACTIVE, true);
            Collected = new List<string>();
        }

        /// <summary>
        /// Called for every import. Collects the assets only while a <c>.unitypackage</c> import is under way.
        /// </summary>
        internal static void OnAssetsImported(IEnumerable<string> _Paths)
        {
            if (!IsCollecting) return;

            var collected = Collected;
            var known = new HashSet<string>(collected);
            foreach (string path in _Paths)
                if (IsMappable(path) && known.Add(path))
                    collected.Add(path);

            Collected = collected;
        }

        /// <summary>
        /// The import finished: map the assets it brought in.
        /// </summary>
        /// <returns> The result, or <see langword="null"/> if nothing was collected. </returns>
        internal static TagImportResult OnImportCompleted(string _Package)
        {
            if (!IsCollecting) return null;

            var paths = Collected;
            Stop();

            return paths.Count == 0 ? null : Map(paths, $"package \"{_Package}\"", !Application.isBatchMode);
        }

        /// <summary>
        /// The import was cancelled or failed: forget it. Nothing is mapped.
        /// </summary>
        internal static void OnImportCancelled(string _Package) => Stop();

        private static void Stop()
        {
            SessionState.EraseBool(SESSION_ACTIVE);
            SessionState.EraseString(SESSION_PATHS);
        }

        private static bool IsMappable(string _Path)
            => _Path.StartsWith("Assets/", StringComparison.Ordinal) && TagAssetScanner.CanHoldTags(_Path);
        #endregion



        #region Postponed
        /// <summary>
        /// Assets imported while the lock wasn't available, waiting to be mapped.
        /// </summary>
        public static List<string> PendingAssets
        {
            get
            {
                if (!File.Exists(PendingPath)) return new List<string>();

                return File.ReadAllLines(PendingPath)
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(e => !string.IsNullOrEmpty(e))
                    .Distinct()
                    .ToList();
            }
        }

        public static bool HasPending => PendingAssets.Count > 0;

        private static void AddPending(IEnumerable<string> _Paths)
        {
            var guids = new HashSet<string>(File.Exists(PendingPath) ? File.ReadAllLines(PendingPath) : Array.Empty<string>());
            foreach (string path in _Paths)
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid))
                    guids.Add(guid);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PendingPath) ?? ".");
            File.WriteAllLines(PendingPath, guids.OrderBy(e => e, StringComparer.Ordinal));
        }

        internal static void ClearPending()
        {
            if (File.Exists(PendingPath))
                File.Delete(PendingPath);
        }

        /// <summary>
        /// Map the assets that waited for the lock.
        /// </summary>
        public static TagImportResult MapPending(bool _ShowProgress)
        {
            var paths = PendingAssets;
            if (paths.Count == 0)
                return new TagImportResult { Message = "No imported assets are waiting to be mapped." };

            var result = Map(paths, "postponed import", _ShowProgress);
            if (result.Success && !result.Postponed)
                ClearPending();

            return result;
        }
        #endregion



        #region Mapping
        /// <summary>
        /// Map the tag references of <paramref name="_Paths"/>, assets from another project.
        /// </summary>
        /// <param name="_Source"> What the assets came from, for the log. </param>
        public static TagImportResult Map(IReadOnlyCollection<string> _Paths, string _Source, bool _ShowProgress)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new TagImportResult();
            var paths = _Paths.Where(e => IsMappable(e) && File.Exists(e)).Distinct().ToList();
            result.Assets = paths.Count;

            var snapshot = TagRegistryService.Snapshot;
            var main = snapshot.Main;

            if (main == null || main.IsReadOnly)
                return Postpone(result, paths, "There is no main registry yet. Create it in the Gameplay Tag Manager, then run Map Imported Assets.");

            if (!TagLockProvider.Current.TryAcquire(TagOperations.LockedPaths, out string lockError))
                return Postpone(result, paths, $"The gameplay tag lock ({TagLockProvider.Current.Name}) is not available. {lockError}".TrimEnd());

            var known = new HashSet<TagGuid>(snapshot.Registries.Select(e => e.Id));
            known.UnionWith(TagRegistryService.InactiveRegistryIds);

            // Only load the assets whose saved data references an unknown registry.
            var candidates = paths.Where(e => HasUnknownRegistry(e, snapshot, known)).ToList();

            var changes = TagReferenceTools.Collect(candidates, _ShowProgress,
                tag => tag.IsNone || known.Contains(tag.RegistryId) ? GameplayTag.None : tag);

            // The tag each name maps to: existing, or added.
            var ids = new Dictionary<string, TagGuid>(StringComparer.Ordinal);
            var additions = new List<(string Path, TagGuid Id)>();
            var added = new HashSet<string>(StringComparer.Ordinal);

            foreach (var change in changes.ToList())
            {
                string name = change.From.LastKnownName;
                if (string.IsNullOrEmpty(name) || !TagPath.IsValidPath(name, out _))
                {
                    result.Unmapped.Add($"{Describe(change)}: tag {change.From.Id} of registry {change.From.RegistryId} has no valid name (\"{name}\").");
                    changes.Remove(change);
                    continue;
                }

                if (ids.ContainsKey(name) || added.Contains(name)) continue;

                if (main.TryFind(name, out var id))
                    ids[name] = id;
                else if (added.Add(name))
                    additions.Add((name, change.From.Id));
            }

            if (additions.Count > 0 && !AddTags(additions, ids, result))
                return Finish(result, stopwatch, _Source);

            foreach (var change in changes)
                change.To = GameplayTag.FromId(main.Id, ids[change.From.LastKnownName], change.From.LastKnownName);

            var applied = TagReferenceTools.Apply(changes, UNDO_NAME, _ShowProgress);
            if (applied.Skipped > 0)
                result.Warnings.Add($"{applied.Skipped} reference(s) could not be rewritten.");
            if (applied.UnsavedScenes.Count > 0)
                result.Warnings.Add($"Open scene(s) changed but not saved: {string.Join(", ", applied.UnsavedScenes)}.");

            foreach (var change in changes)
                result.Rewritten.Add($"{Describe(change)}: \"{change.From.LastKnownName}\" {change.From.RegistryId}:{change.From.Id} -> main {change.To.Id}");

            return Finish(result, stopwatch, _Source);
        }

        /// <summary>
        /// Add the new names to the main registry. They are saved right away, unless other tag edits are unsaved:
        /// then they stay staged with them.
        /// </summary>
        private static bool AddTags(List<(string Path, TagGuid Id)> _Additions, Dictionary<string, TagGuid> _Ids, TagImportResult _Result)
        {
            bool hadPending = TagOperations.HasPendingChanges;

            var added = TagOperations.AddWithIds(_Additions, out var addedIds);
            if (!added.Success)
            {
                _Result.Success = false;
                _Result.Error = $"The tags could not be added to the main registry, so nothing was rewritten:\n{added.Error}";
                return false;
            }

            foreach (var (path, id) in addedIds)
            {
                _Ids[path] = id;
                _Result.AddedTags.Add($"{path} ({id})");
            }

            if (hadPending)
            {
                _Result.Warnings.Add("The added tags are staged with your other unsaved tag edits. Save them in the Gameplay Tag Manager.");
                return true;
            }

            var saved = TagOperations.Save();
            if (!saved.Success)
                _Result.Warnings.Add($"The added tags are staged, but could not be saved: {saved.Error}");

            return true;
        }

        private static bool HasUnknownRegistry(string _Path, TagRegistrySnapshot _Snapshot, HashSet<TagGuid> _Known)
        {
            var tags = new List<GameplayTag>();
            TagAssetScanner.Scan(_Path, tags);

            // A prefab override that changed only the tag ID: unknown if no loaded registry has the ID.
            return tags.Any(e => e.RegistryId.IsZero
                ? _Snapshot.Registries.All(registry => !registry.Contains(e.Id))
                : !_Known.Contains(e.RegistryId));
        }

        private static TagImportResult Postpone(TagImportResult _Result, List<string> _Paths, string _Reason)
        {
            AddPending(_Paths);

            _Result.Postponed = true;
            _Result.Message = $"Gameplay tags of {_Paths.Count} imported asset(s) were not mapped: {_Reason} " +
                              "Their references show as (deleted) until you run Map Imported Assets.";
            Debug.LogWarning(_Result.Message);

            return _Result;
        }

        private static TagImportResult Finish(TagImportResult _Result, Stopwatch _Stopwatch, string _Source)
        {
            _Result.Seconds = _Stopwatch.Elapsed.TotalSeconds;

            if (!_Result.Success)
            {
                _Result.Message = _Result.Error;
                Debug.LogError($"Gameplay tags of the {_Source}: {_Result.Error}");
                return _Result;
            }

            var message = new StringBuilder($"Gameplay tags of the {_Source}: {_Result.Assets} asset(s) checked in {_Result.Seconds:0.0} s. " +
                                            $"{_Result.AddedTags.Count} tag(s) added to the main registry, {_Result.Rewritten.Count} reference(s) rewritten");
            message.Append(_Result.Unmapped.Count > 0 ? $", {_Result.Unmapped.Count} left unmapped." : ".");
            foreach (string warning in _Result.Warnings)
                message.Append(' ').Append(warning);

            if (_Result.AddedTags.Count + _Result.Rewritten.Count + _Result.Unmapped.Count > 0)
            {
                _Result.LogPath = WriteLog(_Result, _Source);
                message.Append($" Details: \"{_Result.LogPath}\".");
            }

            _Result.Message = message.ToString();
            if (_Result.Unmapped.Count > 0 || _Result.Warnings.Count > 0) Debug.LogWarning(_Result.Message);
            else Debug.Log(_Result.Message);

            return _Result;
        }

        private static string WriteLog(TagImportResult _Result, string _Source)
        {
            var log = new StringBuilder();
            log.AppendLine($"Gameplay tag import mapping: {_Source}");
            log.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            log.AppendLine($"Assets checked: {_Result.Assets}. Time: {_Result.Seconds:0.000} s.");

            AppendSection(log, "Tags added to the main registry", _Result.AddedTags);
            AppendSection(log, "References rewritten", _Result.Rewritten);
            AppendSection(log, "References left unmapped", _Result.Unmapped);
            AppendSection(log, "Warnings", _Result.Warnings);

            Directory.CreateDirectory(LogFolder);
            string path = $"{LogFolder}/Import_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log";
            File.WriteAllText(path, log.ToString(), new UTF8Encoding(false));

            return path;
        }

        private static void AppendSection(StringBuilder _Log, string _Title, List<string> _Lines)
        {
            _Log.AppendLine().AppendLine($"{_Title} ({_Lines.Count}):");
            foreach (string line in _Lines)
                _Log.AppendLine("  " + line);
        }

        private static string Describe(TagReferenceChange _Change)
            => $"{_Change.AssetPath} > {_Change.ObjectName} > {_Change.PropertyPath}";
        #endregion



        #region Commands
        [MenuItem(MAP_MENU, false, 2020)]
        private static void MapSelected()
        {
            var paths = SelectedAssets();
            if (!EditorUtility.DisplayDialog(
                    "Map Tags From Another Project",
                    $"Map the gameplay tags of {paths.Count} asset(s) brought in from another project?\n\n" +
                    "References to registries this project doesn't have are rewritten to main registry tags with the same " +
                    "name, and missing names are added to the main registry. Only these assets are changed.",
                    "Map",
                    "Cancel"))
                return;

            ShowResult("Map Tags From Another Project", Map(paths, "selected assets", true));
        }

        [MenuItem(MAP_MENU, true)]
        private static bool MapSelectedValidate() => SelectedAssets().Count > 0;

        /// <summary>
        /// Map the assets that waited for the lock, and show the result.
        /// </summary>
        public static void MapPendingWithDialog() => ShowResult("Map Imported Assets", MapPending(true));

        private static void ShowResult(string _Title, TagImportResult _Result)
        {
            string message = _Result.Message;
            if (_Result.Unmapped.Count > 0)
                message += "\n\nLeft unmapped:\n" + string.Join("\n", _Result.Unmapped.Take(MAX_LISTED));

            EditorUtility.DisplayDialog(_Title, message, "OK");
        }

        /// <summary>
        /// The selected assets that can hold tags, with the contents of selected folders.
        /// </summary>
        private static List<string> SelectedAssets()
        {
            var result = new List<string>();
            foreach (string guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path))
                    result.AddRange(TagPackage.AssetPathsUnder(path).Where(IsMappable));
                else if (IsMappable(path))
                    result.Add(path);
            }

            return result.Distinct().ToList();
        }
        #endregion
    }



    /// <summary>
    /// Collects the assets of a <c>.unitypackage</c> import for <see cref="ProjectAssetImport"/>.
    /// </summary>
    internal sealed class ProjectAssetImportPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] _Imported, string[] _Deleted, string[] _Moved, string[] _MovedFrom)
            => ProjectAssetImport.OnAssetsImported(_Imported);
    }
}
