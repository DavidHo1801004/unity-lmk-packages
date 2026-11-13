using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

using UnityEditor;
using UnityEngine;

using LMK.Runtime.Registry;

using Debug = UnityEngine.Debug;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Outcome of <see cref="RegistryImportMapping.Map"/>.
    /// </summary>
    public sealed class RegistryImportResult
    {
        public bool Success = true;

        /// <summary>
        /// The lock or a main registry wasn't available, so the assets wait for <see cref="RegistryImportMapping.MapPending"/>.
        /// </summary>
        public bool Postponed;

        public string Error;

        /// <summary> Rows added to main registries, as "kind: name (ID)". </summary>
        public readonly List<string> AddedRows = new();

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
    /// Maps the references of assets brought in from another project, for every kind at once: references into
    /// registries this project doesn't have (that project's main registries) are rewritten, by name, to rows of
    /// this project's main registry of the same kind.
    /// </summary>
    /// <remarks>
    /// Runs only for explicit imports: a <c>.unitypackage</c> import, or the Map References From Another Project
    /// command. Pulls, file copies and domain reloads never map anything. Requires the lock: without it, the
    /// assets wait until Map Imported Assets runs. Only the incoming assets are rewritten, and an import log lists
    /// every added row and rewritten reference.
    /// <list type="bullet">
    /// <item>A reference to a registry its kind knows (loaded, or generated for an uninstalled package) is left as it is.</item>
    /// <item>A name that exists in the kind's main registry is rewritten to that row.</item>
    /// <item>A new name is added to the main registry, with the incoming ID if the main registry doesn't use it.</item>
    /// <item>A reference without a usable name is left as it is, and reported.</item>
    /// </list>
    /// </remarks>
    [InitializeOnLoad]
    public static class RegistryImportMapping
    {
        public const string PENDING_PATH = "Library/LMKRegistry/PendingImports";
        public const string UNDO_NAME = "Map Imported Registry References";

        private const string SESSION_ACTIVE = "LMK.Registry.Import.Active";
        private const string SESSION_PATHS = "LMK.Registry.Import.Paths";
        private const string MAP_MENU = "Assets/LMK Registries/Map References From Another Project...";
        private const int MAX_LISTED = 10;

        /// <summary>
        /// Where postponed assets wait for the lock. Only tests change it, including the tests of domain packages.
        /// </summary>
        public static string PendingPath { get; set; } = PENDING_PATH;

        /// <summary>
        /// Where import logs are written. Only tests change it, including the tests of domain packages.
        /// </summary>
        public static string LogFolder { get; set; } = "Logs/LMKRegistry";



        static RegistryImportMapping()
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

        public static void OnImportStarted(string _Package)
        {
            SessionState.SetBool(SESSION_ACTIVE, true);
            Collected = new List<string>();
        }

        /// <summary>
        /// Called for every import. Collects the assets only while a <c>.unitypackage</c> import is under way.
        /// </summary>
        public static void OnAssetsImported(IEnumerable<string> _Paths)
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
        public static RegistryImportResult OnImportCompleted(string _Package)
        {
            if (!IsCollecting) return null;

            var paths = Collected;
            Stop();

            return paths.Count == 0 || RegistryKinds.All.Count == 0 ? null : Map(paths, $"package \"{_Package}\"", !Application.isBatchMode);
        }

        /// <summary>
        /// The import was cancelled or failed: forget it. Nothing is mapped.
        /// </summary>
        public static void OnImportCancelled(string _Package) => Stop();

        private static void Stop()
        {
            SessionState.EraseBool(SESSION_ACTIVE);
            SessionState.EraseString(SESSION_PATHS);
        }

        private static bool IsMappable(string _Path)
            => _Path.StartsWith("Assets/", StringComparison.Ordinal) && RegistryAssetScanner.CanHoldReferences(_Path);
        #endregion



        #region Postponed
        /// <summary>
        /// Assets imported while the lock or a main registry wasn't available, waiting to be mapped.
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

        public static void ClearPending()
        {
            if (File.Exists(PendingPath))
                File.Delete(PendingPath);
        }

        /// <summary>
        /// Map the assets that waited.
        /// </summary>
        public static RegistryImportResult MapPending(bool _ShowProgress)
        {
            var paths = PendingAssets;
            if (paths.Count == 0)
                return new RegistryImportResult { Message = "No imported assets are waiting to be mapped." };

            var result = Map(paths, "postponed import", _ShowProgress);
            if (result.Success && !result.Postponed)
                ClearPending();

            return result;
        }
        #endregion



        #region Mapping
        /// <summary>
        /// Map the references of <paramref name="_Paths"/>, assets from another project, for every kind.
        /// </summary>
        /// <param name="_Source"> What the assets came from, for the log. </param>
        public static RegistryImportResult Map(IReadOnlyCollection<string> _Paths, string _Source, bool _ShowProgress)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new RegistryImportResult();
            var paths = _Paths.Where(e => IsMappable(e) && File.Exists(e)).Distinct().ToList();
            result.Assets = paths.Count;

            // Only load the assets whose saved data references a registry its kind doesn't know.
            var kinds = new HashSet<RegistryKind>();
            var candidates = new List<string>();
            foreach (string path in paths)
            {
                var references = new List<RegistryReference>();
                RegistryAssetScanner.Scan(path, references);

                var unknown = references.Where(IsUnknown).Select(e => e.Kind).ToList();
                if (unknown.Count == 0) continue;

                candidates.Add(path);
                kinds.UnionWith(unknown);
            }

            if (candidates.Count == 0)
                return Finish(result, stopwatch, _Source);

            foreach (var kind in kinds)
                if (!kind.HasEditableMain)
                    return Postpone(result, paths, $"There is no {kind.DisplayName} main registry yet. Create it, then run Map Imported Assets.");

            var lockedPaths = kinds.SelectMany(e => e.LockedPaths).Distinct().ToList();
            if (!RegistryLockProvider.Current.TryAcquire(lockedPaths, out string lockError))
                return Postpone(result, paths, RegistryLockProvider.RefusedMessage(lockError));

            var changes = RegistryReferenceTools.Collect(candidates, _ShowProgress, reference => IsUnknown(reference) ? reference : null);

            // Per kind: the row each name maps to, existing or added.
            bool hadPending = RegistrySession.HasPendingChanges;
            bool added = false;
            foreach (var group in changes.GroupBy(e => e.From.Kind).ToList())
            {
                var kind = group.Key;
                var ids = new Dictionary<string, RegistryGuid>(StringComparer.Ordinal);
                var additions = new List<(string Name, RegistryGuid Id)>();

                foreach (var change in group)
                {
                    string name = change.From.Name;
                    if (string.IsNullOrEmpty(name) || ids.ContainsKey(name) || additions.Any(e => e.Name == name)) continue;

                    if (kind.TryFindInMain(name, out var id)) ids[name] = id;
                    else additions.Add((name, change.From.Id));
                }

                if (additions.Count > 0)
                {
                    var unmapped = new List<string>();
                    var addResult = kind.AddImportedRows(additions, ids, unmapped);
                    if (!addResult.Success)
                    {
                        result.Success = false;
                        result.Error = $"The rows could not be added to the {kind.DisplayName} main registry, so nothing was rewritten:\n{addResult.Error}";
                        return Finish(result, stopwatch, _Source);
                    }

                    result.Warnings.AddRange(unmapped);
                    foreach (var (name, _) in additions)
                        if (ids.TryGetValue(name, out var id))
                            result.AddedRows.Add($"{kind.DisplayName}: {name} ({id})");

                    added = true;
                }

                foreach (var change in group)
                {
                    string name = change.From.Name;
                    if (string.IsNullOrEmpty(name) || !ids.TryGetValue(name, out var id))
                    {
                        result.Unmapped.Add($"{change.Location}: {kind.RowNoun} {change.From.Id} of registry {change.From.Registry} has no usable name (\"{name}\").");
                        change.Selected = false;
                        continue;
                    }

                    change.To = new RegistryReference(kind, kind.MainRegistryId, id, name);
                }
            }

            if (added)
            {
                if (hadPending)
                {
                    result.Warnings.Add("The added rows are staged with your other unsaved registry edits. Save them to keep them.");
                }
                else
                {
                    var saved = RegistrySession.Save();
                    if (!saved.Success)
                        result.Warnings.Add($"The added rows are staged, but could not be saved: {saved.Error}");
                }
            }

            var applied = RegistryReferenceTools.Apply(changes, UNDO_NAME, _ShowProgress);
            if (applied.Skipped > 0)
                result.Warnings.Add($"{applied.Skipped} reference(s) could not be rewritten.");
            if (applied.UnsavedScenes.Count > 0)
                result.Warnings.Add($"Open scene(s) changed but not saved: {string.Join(", ", applied.UnsavedScenes)}.");

            foreach (var change in changes.Where(e => e.Selected))
                result.Rewritten.Add($"{change.Location}: {change.From.Kind.DisplayName} \"{change.From.Name}\" {change.From.Registry}:{change.From.Id} -> main {change.To.Id}");

            return Finish(result, stopwatch, _Source);
        }

        /// <summary>
        /// Whether a reference points into a registry its kind doesn't know. A prefab override that changed only the
        /// row ID is unknown if no registry of its kind has the ID.
        /// </summary>
        private static bool IsUnknown(RegistryReference _Reference)
            => _Reference.Kind != null
            && (_Reference.HasRegistry ? !_Reference.Kind.KnowsRegistry(_Reference.Registry) : !_Reference.Kind.HasRowAnywhere(_Reference.Id));

        private static RegistryImportResult Postpone(RegistryImportResult _Result, List<string> _Paths, string _Reason)
        {
            AddPending(_Paths);

            _Result.Postponed = true;
            _Result.Message = $"References of {_Paths.Count} imported asset(s) were not mapped: {_Reason} " +
                              "They show as (deleted) until you run Map Imported Assets.";
            Debug.LogWarning(_Result.Message);

            return _Result;
        }

        private static RegistryImportResult Finish(RegistryImportResult _Result, Stopwatch _Stopwatch, string _Source)
        {
            _Result.Seconds = _Stopwatch.Elapsed.TotalSeconds;

            if (!_Result.Success)
            {
                _Result.Message = _Result.Error;
                Debug.LogError($"Registry references of the {_Source}: {_Result.Error}");
                return _Result;
            }

            var message = new StringBuilder($"Registry references of the {_Source}: {_Result.Assets} asset(s) checked in {_Result.Seconds:0.0} s. " +
                                            $"{_Result.AddedRows.Count} row(s) added to main registries, {_Result.Rewritten.Count} reference(s) rewritten");
            message.Append(_Result.Unmapped.Count > 0 ? $", {_Result.Unmapped.Count} left unmapped." : ".");
            foreach (string warning in _Result.Warnings)
                message.Append(' ').Append(warning);

            if (_Result.AddedRows.Count + _Result.Rewritten.Count + _Result.Unmapped.Count > 0)
            {
                _Result.LogPath = WriteLog(_Result, _Source);
                message.Append($" Details: \"{_Result.LogPath}\".");
            }

            _Result.Message = message.ToString();
            if (_Result.Unmapped.Count > 0 || _Result.Warnings.Count > 0) Debug.LogWarning(_Result.Message);
            else Debug.Log(_Result.Message);

            return _Result;
        }

        private static string WriteLog(RegistryImportResult _Result, string _Source)
        {
            var log = new StringBuilder();
            log.AppendLine($"Registry import mapping: {_Source}");
            log.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            log.AppendLine($"Assets checked: {_Result.Assets}. Time: {_Result.Seconds:0.000} s.");

            AppendSection(log, "Rows added to main registries", _Result.AddedRows);
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
        #endregion



        #region Commands
        [MenuItem(MAP_MENU, false, 2020)]
        private static void MapSelected()
        {
            var paths = SelectedAssets();
            if (!EditorUtility.DisplayDialog(
                    "Map References From Another Project",
                    $"Map the registry references of {paths.Count} asset(s) brought in from another project?\n\n" +
                    "References to registries this project doesn't have are rewritten to main registry rows with the same " +
                    "name, and missing names are added to the main registries. Only these assets are changed.",
                    "Map",
                    "Cancel"))
                return;

            ShowResult("Map References From Another Project", Map(paths, "selected assets", true));
        }

        [MenuItem(MAP_MENU, true)]
        private static bool MapSelectedValidate() => RegistryKinds.All.Count > 0 && SelectedAssets().Count > 0;

        /// <summary>
        /// Map the assets that waited, and show the result.
        /// </summary>
        public static void MapPendingWithDialog() => ShowResult("Map Imported Assets", MapPending(true));

        private static void ShowResult(string _Title, RegistryImportResult _Result)
        {
            string message = _Result.Message;
            if (_Result.Unmapped.Count > 0)
                message += "\n\nLeft unmapped:\n" + string.Join("\n", _Result.Unmapped.Take(MAX_LISTED));

            EditorUtility.DisplayDialog(_Title, message, "OK");
        }

        /// <summary>
        /// The selected assets that can hold references, with the contents of selected folders.
        /// </summary>
        private static List<string> SelectedAssets()
        {
            var result = new List<string>();
            foreach (string guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path))
                    result.AddRange(RegistryPackage.AssetPathsUnder(path).Where(IsMappable));
                else if (IsMappable(path))
                    result.Add(path);
            }

            return result.Distinct().ToList();
        }
        #endregion
    }
}
