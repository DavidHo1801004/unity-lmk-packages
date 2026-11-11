using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using UnityEditor;
using UnityEngine;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Writes a registry's paths and descriptions out as a tag file, for review or sharing.
    /// Read-only with respect to the registry.
    /// </summary>
    internal static class TagFileExport
    {
        public const string DEFAULT_FOLDER = TagRegistryService.ROOT_FOLDER + "/Sources";

        /// <summary>
        /// The tag file text of a registry. Explicit tags are listed, plus implicit tags with a description.
        /// Other implicit tags are the parents of listed paths, so importing the file recreates them.
        /// </summary>
        public static string ExportText(TagRegistrySnapshot.Registry _Registry)
        {
            var entries = new List<TagFileEntry>();
            foreach (var row in _Registry.Rows)
            {
                if (row.isImplicit && string.IsNullOrEmpty(row.description)) continue;

                string path = _Registry.GetPath(row.id);
                if (path != null)
                    entries.Add(new TagFileEntry(path, row.description));
            }

            var header = new[]
            {
                $"Tags of the \"{_Registry.Name}\" registry, exported by the Gameplay Tag Manager.",
                "One tag per line: <path> [; <description>]. Edit freely: nothing changes until the file is imported.",
            };

            return TagFile.Write(entries, header);
        }

        /// <summary>
        /// Write a registry's tag file to <paramref name="_Path"/>, and import it if it's inside the project.
        /// </summary>
        public static bool ExportToFile(TagRegistrySnapshot.Registry _Registry, string _Path, out string _Error)
        {
            try
            {
                string folder = Path.GetDirectoryName(Path.GetFullPath(_Path));
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);

                File.WriteAllText(_Path, ExportText(_Registry), new UTF8Encoding(false));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _Error = $"Could not write \"{_Path}\": {exception.Message}";
                return false;
            }

            string assetPath = ToAssetPath(_Path);
            if (assetPath != null)
                AssetDatabase.ImportAsset(assetPath);

            _Error = null;
            return true;
        }

        /// <summary>
        /// Ask for a file and export a registry to it.
        /// </summary>
        /// <returns> The written path, or <see langword="null"/> if cancelled or failed. </returns>
        public static string ExportWithDialog(TagRegistrySnapshot.Registry _Registry)
        {
            if (_Registry == null) return null;

            string folder = AssetDatabase.IsValidFolder(DEFAULT_FOLDER) ? DEFAULT_FOLDER : TagRegistryService.ROOT_FOLDER;
            string path = EditorUtility.SaveFilePanel("Export Tag File", folder, SafeFileName(_Registry.Name), TagFile.EXTENSION);
            if (string.IsNullOrEmpty(path)) return null;

            if (!ExportToFile(_Registry, path, out string error))
            {
                EditorUtility.DisplayDialog("Export Tag File", error, "OK");
                return null;
            }

            string assetPath = ToAssetPath(path);
            if (assetPath != null)
                EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(assetPath));

            return assetPath ?? path;
        }

        /// <summary>
        /// The loaded registry of a registry file, or <see langword="null"/> if the file isn't loaded: rejected with
        /// no version that loaded before, or ignored because another file has the same registry ID.
        /// </summary>
        public static TagRegistrySnapshot.Registry FindRegistry(string _AssetPath)
        {
            if (string.IsNullOrEmpty(_AssetPath)) return null;

            foreach (var registry in TagRegistryService.Snapshot.Registries)
                if (string.Equals(registry.AssetPath, _AssetPath, StringComparison.Ordinal))
                    return registry;

            return null;
        }

        /// <summary>
        /// The file each registry would be exported to in <paramref name="_Folder"/>: <c>&lt;registry name&gt;.tags</c>,
        /// with <c>_2</c>, <c>_3</c>… appended when names clash.
        /// </summary>
        public static List<string> FolderTargets(IReadOnlyList<TagRegistrySnapshot.Registry> _Registries, string _Folder)
        {
            var result = new List<string>(_Registries.Count);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var registry in _Registries)
            {
                string name = SafeFileName(registry.Name);
                string candidate = name;
                for (int i = 2; !used.Add(candidate); i++)
                    candidate = $"{name}_{i}";

                result.Add(Path.Combine(_Folder, candidate + "." + TagFile.EXTENSION).Replace('\\', '/'));
            }

            return result;
        }

        /// <summary>
        /// Export each registry to its own tag file in <paramref name="_Folder"/> (see <see cref="FolderTargets"/>).
        /// </summary>
        /// <returns> The written paths. Failures are added to <paramref name="_Errors"/>. </returns>
        public static List<string> ExportToFolder(IReadOnlyList<TagRegistrySnapshot.Registry> _Registries, string _Folder, List<string> _Errors)
        {
            var targets = FolderTargets(_Registries, _Folder);
            var written = new List<string>();

            for (int i = 0; i < _Registries.Count; i++)
            {
                if (ExportToFile(_Registries[i], targets[i], out string error))
                    written.Add(targets[i]);
                else
                    _Errors.Add(error);
            }

            return written;
        }

        /// <summary>
        /// Export registry files selected in the Project window: one file through a save dialog, several to a folder.
        /// </summary>
        public static void ExportSelected(IReadOnlyList<string> _AssetPaths)
        {
            var registries = new List<TagRegistrySnapshot.Registry>();
            var problems = new List<string>();

            foreach (string path in _AssetPaths)
            {
                var registry = FindRegistry(path);
                if (registry == null)
                {
                    problems.Add($"\"{path}\" is not loaded: it is invalid, or another registry file has the same registry ID.");
                    continue;
                }

                if (registry.UsingLastGood)
                    Debug.LogWarning($"\"{path}\" is invalid. Its last loaded version is exported.");

                registries.Add(registry);
            }

            if (problems.Count > 0)
                EditorUtility.DisplayDialog("Export Tag File", string.Join("\n\n", problems), "OK");

            if (registries.Count == 1)
            {
                ExportWithDialog(registries[0]);
                return;
            }

            if (registries.Count == 0) return;

            string start = AssetDatabase.IsValidFolder(DEFAULT_FOLDER) ? DEFAULT_FOLDER : TagRegistryService.ROOT_FOLDER;
            string folder = EditorUtility.OpenFolderPanel($"Export {registries.Count} Registries To Tag Files", start, string.Empty);
            if (string.IsNullOrEmpty(folder)) return;

            var existing = FolderTargets(registries, folder).Where(File.Exists).ToList();
            if (existing.Count > 0 && !EditorUtility.DisplayDialog(
                    "Export Tag Files",
                    $"These files will be overwritten:\n\n{string.Join("\n", existing.Select(Path.GetFileName))}",
                    "Overwrite", "Cancel"))
                return;

            var errors = new List<string>();
            var written = ExportToFolder(registries, folder, errors);

            if (errors.Count > 0)
                EditorUtility.DisplayDialog("Export Tag Files", string.Join("\n", errors), "OK");

            if (written.Count > 0)
                Debug.Log($"Exported {written.Count} registr{(written.Count == 1 ? "y" : "ies")} to tag files:\n{string.Join("\n", written)}");
        }

        /// <summary>
        /// A project-relative path ("Assets/…"), or <see langword="null"/> if the file is outside the Assets folder.
        /// </summary>
        internal static string ToAssetPath(string _Path)
        {
            if (string.IsNullOrEmpty(_Path)) return null;

            string full = Path.GetFullPath(_Path).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');

            if (full.Equals(assets, StringComparison.OrdinalIgnoreCase)) return "Assets";
            if (!full.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase)) return null;

            return "Assets" + full[assets.Length..];
        }

        private static string SafeFileName(string _Name)
        {
            var builder = new StringBuilder();
            foreach (char c in _Name ?? string.Empty)
                builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);

            return builder.Length > 0 ? builder.ToString() : "Tags";
        }
    }
}
