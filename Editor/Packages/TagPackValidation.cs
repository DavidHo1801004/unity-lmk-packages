using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Checks a package you author before it is packed, so the tags it ships resolve in every project:
    /// <list type="bullet">
    /// <item>it contains exactly one registry of kind <c>package</c>, valid and with a valid checksum;</item>
    /// <item>its assets reference only its own registry, or the registry of a package it depends on;</item>
    /// <item>no asset references the authoring project's main registry;</item>
    /// <item>every referenced tag ID exists in its registry;</item>
    /// <item>its registry ID is the one it was packed with before.</item>
    /// </list>
    /// </summary>
    internal static class TagPackValidation
    {
        private const int MAX_LISTED = 20;



        /// <summary>
        /// Every reason the package can't be packed. Empty if it can.
        /// </summary>
        /// <param name="_PublishedRegistryId"> The registry ID of the previously packed version, or zero if there is none. </param>
        public static List<string> Validate(TagPackage _Package, TagGuid _PublishedRegistryId)
        {
            var errors = new List<string>();
            var snapshot = TagRegistryService.Snapshot;

            // A package that ships no tags has nothing to check.
            var references = ReadReferences(_Package, errors);
            if (references.Count == 0 && !HasRegistryFile(_Package))
                return errors;

            // Rule 1: one valid package registry.
            var own = ReadOwnRegistry(_Package, errors);

            // Rule 5: same registry ID as before.
            if (own != null && !_PublishedRegistryId.IsZero && own.registryId != _PublishedRegistryId)
                errors.Add($"The registry ID changed from {_PublishedRegistryId} to {own.registryId} since the last pack. " +
                           "Every reference made with the old ID would stop resolving. Restore the old ID.");

            // The registries references may point to, with their tag IDs.
            var allowed = new Dictionary<TagGuid, (string Name, HashSet<TagGuid> Ids)>();
            if (own != null)
                allowed[own.registryId] = ($"its own registry \"{own.name}\"", new HashSet<TagGuid>(own.rows.Select(e => e.id)));

            foreach (string dependency in _Package.Dependencies)
                foreach (var registry in DependencyRegistries(snapshot, dependency))
                    allowed.TryAdd(registry.Id, ($"\"{dependency}\"'s registry", new HashSet<TagGuid>(registry.Rows.Select(e => e.id))));

            var mainId = snapshot.Main?.Id ?? TagGuid.Zero;

            // Rules 2 to 4: every reference.
            var problems = new List<string>();
            foreach (var (path, tag) in references)
            {
                string problem = Check(tag, allowed, mainId);
                if (problem != null)
                    problems.Add($"\"{path}\": {Describe(tag)} {problem}");
            }

            var distinct = problems.Distinct().ToList();
            errors.AddRange(distinct.Take(MAX_LISTED));
            if (distinct.Count > MAX_LISTED)
                errors.Add($"(and {distinct.Count - MAX_LISTED} more problems with references)");

            return errors;
        }

        private static string Check(GameplayTag _Tag, Dictionary<TagGuid, (string Name, HashSet<TagGuid> Ids)> _Allowed, TagGuid _MainId)
        {
            // A prefab override that changed only the tag ID: its registry is the one that has the ID.
            if (_Tag.RegistryId.IsZero)
                return _Allowed.Values.Any(e => e.Ids.Contains(_Tag.Id))
                    ? null
                    : "is not a tag of the package's registry or of a dependency's registry.";

            if (!_MainId.IsZero && _Tag.RegistryId == _MainId)
                return "references this project's main registry, which other projects don't have. Move the tag to the package's registry.";

            if (!_Allowed.TryGetValue(_Tag.RegistryId, out var registry))
                return $"references registry {_Tag.RegistryId}, which is neither the package's own registry nor the registry of a package listed in its dependencies.";

            return registry.Ids.Contains(_Tag.Id) ? null : $"is not in {registry.Name}: its tag ID {_Tag.Id} doesn't exist there.";
        }

        private static string Describe(GameplayTag _Tag)
            => string.IsNullOrEmpty(_Tag.LastKnownName) ? $"tag {_Tag.Id}" : $"\"{_Tag.LastKnownName}\"";

        /// <summary>
        /// The references of the package's assets. Its <c>Tests</c> folder is skipped: test fixtures reference
        /// registries their tests build themselves.
        /// </summary>
        private static List<(string Path, GameplayTag Tag)> ReadReferences(TagPackage _Package, List<string> _Errors)
        {
            string tests = _Package.Root.TrimEnd('/') + "/Tests/";
            var result = new List<(string, GameplayTag)>();

            foreach (string path in _Package.GetTagHolderAssets())
            {
                if (path.StartsWith(tests, StringComparison.Ordinal)) continue;

                var tags = new List<GameplayTag>();
                if (!TagAssetScanner.Scan(path, tags))
                    _Errors.Add($"\"{path}\" could not be read completely.");

                result.AddRange(tags.Select(e => (path, e)));
            }

            return result;
        }

        private static bool HasRegistryFile(TagPackage _Package)
            => TagPackage.AssetPathsUnder(_Package.Root).Any(e => e.EndsWith("." + TagRegistryFile.EXTENSION, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The package's own registry, read from its file. Reports every problem with rule 1.
        /// </summary>
        private static TagRegistryData ReadOwnRegistry(TagPackage _Package, List<string> _Errors)
        {
            var files = TagPackage.AssetPathsUnder(_Package.Root)
                .Where(e => e.EndsWith("." + TagRegistryFile.EXTENSION, StringComparison.OrdinalIgnoreCase))
                .ToList();

            TagRegistryData own = null;
            int count = 0;

            foreach (string path in files)
            {
                var parsed = TagRegistryFile.Parse(File.ReadAllText(path));
                if (!parsed.IsValid)
                {
                    _Errors.Add($"\"{path}\" is invalid: {string.Join(" ", parsed.Errors)}");
                    continue;
                }

                if (parsed.Data.kind != TagRegistryKind.Package)
                {
                    _Errors.Add($"\"{path}\" is a {TagRegistryFile.KindToText(parsed.Data.kind)} registry. A package ships only a registry of kind \"package\".");
                    continue;
                }

                if (parsed.ChecksumMismatch)
                    _Errors.Add($"\"{path}\" has no valid checksum: it was edited outside the Gameplay Tag Manager.");

                count++;
                own ??= parsed.Data;
            }

            if (count == 0)
                _Errors.Add($"\"{_Package.Root}\" contains no registry of kind \"package\". A package that ships tags needs exactly one.");
            else if (count > 1)
                _Errors.Add($"\"{_Package.Root}\" contains {count} registries of kind \"package\". A package needs exactly one.");

            return count == 1 ? own : null;
        }

        /// <summary>
        /// The registries of a dependency: the ones it ships, and the one generated for it.
        /// </summary>
        private static IEnumerable<TagRegistrySnapshot.Registry> DependencyRegistries(TagRegistrySnapshot _Snapshot, string _Dependency)
        {
            string prefix = $"Packages/{_Dependency}/";
            return _Snapshot.Registries.Where(e =>
                (e.Kind == TagRegistryKind.Package && e.AssetPath.StartsWith(prefix, StringComparison.Ordinal))
                || (e.Kind == TagRegistryKind.Generated && e.Data.package == _Dependency));
        }



        #region Commands
        private const string VALIDATE_MENU = "Assets/Gameplay Tags/Validate Package Tags";
        private const string PACK_MENU = "Assets/Gameplay Tags/Pack Package...";

        private static PackRequest packRequest;
        private static TagPackage packing;
        private static TagGuid packingRegistry;

        private static TagPackage SelectedPackage()
        {
            var package = TagPackage.ForAssetPath(AssetDatabase.GetAssetPath(Selection.activeObject));

            // Only packages you author can be packed: embedded or local ones.
            var info = package != null ? UnityEditor.PackageManager.PackageInfo.FindForAssetPath(package.Root) : null;
            return info != null && (info.source == PackageSource.Embedded || info.source == PackageSource.Local) ? package : null;
        }

        private static TagGuid PublishedRegistry(TagPackage _Package)
            => GameplayTagSettings.instance.TryGetPublishedRegistry(_Package.Name, out var id) ? id : TagGuid.Zero;

        [MenuItem(VALIDATE_MENU, false, 2010)]
        private static void ValidateSelected()
        {
            var package = SelectedPackage();
            var errors = Validate(package, PublishedRegistry(package));

            if (errors.Count == 0)
                EditorUtility.DisplayDialog("Validate Package Tags", $"\"{package.Name}\" can be packed: its tags resolve in any project.", "OK");
            else
                ShowErrors(package, errors);
        }

        [MenuItem(PACK_MENU, false, 2011)]
        private static void PackSelected()
        {
            var package = SelectedPackage();
            var errors = Validate(package, PublishedRegistry(package));
            if (errors.Count > 0)
            {
                ShowErrors(package, errors);
                return;
            }

            if (packRequest != null && !packRequest.IsCompleted)
            {
                EditorUtility.DisplayDialog("Pack Package", "Another package is being packed.", "OK");
                return;
            }

            string folder = EditorUtility.SaveFolderPanel($"Pack {package.Name}", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(folder)) return;

            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(package.Root);
            packing = package;
            packingRegistry = ReadOwnRegistry(package, new List<string>())?.registryId ?? TagGuid.Zero;
            // The newer overload needs a Unity organization ID, which packages packed for Git or OpenUPM don't have.
#pragma warning disable CS0618
            packRequest = Client.Pack(info.resolvedPath, folder);
#pragma warning restore CS0618
            EditorApplication.update += WaitForPack;
        }

        [MenuItem(VALIDATE_MENU, true)]
        [MenuItem(PACK_MENU, true)]
        private static bool HasSelectedPackage() => SelectedPackage() != null;

        private static void WaitForPack()
        {
            if (packRequest == null || !packRequest.IsCompleted) return;
            EditorApplication.update -= WaitForPack;

            if (packRequest.Status == StatusCode.Success)
            {
                // Later packs must keep this registry ID.
                GameplayTagSettings.instance.SetPublishedRegistry(packing.Name, packingRegistry);
                Debug.Log($"Packed \"{packing.Name}\" to \"{packRequest.Result.tarballPath}\". Its registry ID {packingRegistry} is recorded in the project settings.");
            }
            else
            {
                Debug.LogError($"Packing \"{packing.Name}\" failed: {packRequest.Error?.message}");
            }

            packRequest = null;
            packing = null;
        }

        private static void ShowErrors(TagPackage _Package, List<string> _Errors)
        {
            string message = $"\"{_Package.Name}\" can't be packed:\n\n- {string.Join("\n- ", _Errors)}";
            Debug.LogError(message);
            EditorUtility.DisplayDialog("Package Tags Are Not Valid", message, "OK");
        }
        #endregion
    }
}
