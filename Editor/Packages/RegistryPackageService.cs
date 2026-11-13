using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Outcome of <see cref="RegistryPackageService.Generate"/>.
    /// </summary>
    public sealed class RegistryPackageResult
    {
        public bool Success = true;
        public string Error;

        /// <summary> Registry files written. </summary>
        public readonly List<string> Written = new();

        public readonly List<string> Warnings = new();

        public string Message;

        public static RegistryPackageResult Fail(string _Error) => new() { Success = false, Error = _Error, Message = _Error };
    }



    /// <summary>
    /// Registries of packages, for every kind. Shipped registries load like any other. A package without one gets a
    /// generated registry on an explicit command, kept in the kind's <see cref="RegistryKind.GeneratedRegistryFolder"/>.
    /// A generated registry is loaded only while its package is installed, so references to an uninstalled package
    /// resolve as <c>MissingRegistry</c>.
    /// </summary>
    /// <remarks>
    /// Package events never write: a changed package only reloads the registries, and a package updated past the
    /// version a registry was generated from gets a Regenerate prompt.
    /// </remarks>
    [InitializeOnLoad]
    public static class RegistryPackageService
    {
        static RegistryPackageService()
        {
            Events.registeredPackages += e =>
            {
                var changed = e.added.Concat(e.changedTo).Select(p => p.name).Concat(e.removed.Select(p => p.name));
                OnPackagesChanged(changed);
            };
        }



        #region Package Events
        /// <summary>
        /// Packages were added, updated or removed (an install, or a teammate's pull). Reload every kind, so generated
        /// registries follow their packages, and prompt to regenerate the ones that are out of date. Writes nothing.
        /// </summary>
        /// <returns> The prompts shown. </returns>
        public static List<string> OnPackagesChanged(IEnumerable<string> _Packages)
        {
            var changed = new HashSet<string>(_Packages ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var prompts = new List<string>();

            foreach (var kind in RegistryKinds.All)
            {
                kind.Reload();

                foreach (var (package, path, generatedVersion, installedVersion) in kind.FindOutdatedRegistries())
                {
                    if (!changed.Contains(package)) continue;

                    string prompt = $"Package \"{package}\" is now {installedVersion}, but its {kind.DisplayName} registry \"{path}\" " +
                                    $"was generated from {generatedVersion}. Regenerate the package's registries to add its new rows.";
                    prompts.Add(prompt);
                    Debug.LogWarning(prompt);
                }
            }

            return prompts;
        }
        #endregion



        #region Generate
        /// <summary>
        /// Generate the registries a package needs, or regenerate them, for every kind. Requires the lock. <br/>
        /// The package's assets are scanned once for references of every kind, grouped by kind and registry ID. Each
        /// registry ID that no registry of its kind has gets a generated registry with that ID, and a row per
        /// referenced row with the referenced ID, so the package's references resolve without changing it.
        /// </summary>
        /// <param name="_RemoveMissing"> Remove rows that the package no longer references or declares. Otherwise existing rows are kept. </param>
        public static RegistryPackageResult Generate(RegistryPackage _Package, bool _RemoveMissing)
        {
            if (_Package == null || string.IsNullOrEmpty(_Package.Name))
                return RegistryPackageResult.Fail("Select a folder of an installed package.");

            var result = new RegistryPackageResult();
            var references = ScanPackage(_Package, false, result.Warnings);

            int unknown = references.Count(e => e.Reference.Kind == null);
            if (unknown > 0)
                result.Warnings.Add($"{unknown} reference(s) could not be matched to a registry kind and were skipped.");

            // Plan every kind before writing anything.
            var plans = new List<(RegistryKind Kind, List<(string Path, string Text, int Rows)> Files)>();
            foreach (var kind in RegistryKinds.All)
            {
                var ofKind = references.Where(e => e.Reference.Kind == kind).Select(e => e.Reference).ToList();
                var files = kind.PlanPackageRegistries(_Package, ofKind, _RemoveMissing, result.Warnings, out string error);
                if (files == null) return RegistryPackageResult.Fail(error);
                if (files.Count > 0) plans.Add((kind, files));
            }

            if (plans.Count == 0)
            {
                result.Message = $"\"{_Package.Name}\" needs no generated registry: all of its references resolve to loaded registries.";
                return result;
            }

            var lockedPaths = plans.SelectMany(e => e.Files.Select(f => f.Path).Concat(e.Kind.GeneratedFiles)).Distinct().ToList();
            if (!RegistryLockProvider.Current.TryAcquire(lockedPaths, out string lockError))
                return RegistryPackageResult.Fail(RegistryLockProvider.RefusedMessage(lockError));

            foreach (var (_, files) in plans)
            {
                foreach (var (path, text, _) in files)
                {
                    RegistryAssets.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                    RegistryFile.WriteAtomic(path, text);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    result.Written.Add(path);
                }
            }

            // Registries first, generated files last.
            foreach (var (kind, _) in plans)
            {
                kind.Reload();
                kind.InvokeOnSaved();
            }

            int count = plans.Sum(e => e.Files.Count);
            result.Message = $"Generated {count} registr{(count == 1 ? "y" : "ies")} for \"{_Package.Name}\": " +
                             string.Join(", ", plans.SelectMany(e => e.Files.Select(f => $"\"{f.Path}\" ({f.Rows} {e.Kind.DisplayName} rows)"))) + ".";
            if (result.Warnings.Count > 0)
                result.Message += "\n" + string.Join("\n", result.Warnings);

            return result;
        }

        /// <summary>
        /// Every reference the package's assets hold, with the asset that holds it.
        /// </summary>
        /// <param name="_SkipTests"> Skip the package's <c>Tests</c> folder. </param>
        private static List<(string Path, RegistryReference Reference)> ScanPackage(RegistryPackage _Package, bool _SkipTests, List<string> _Problems)
        {
            string tests = _Package.Root.TrimEnd('/') + "/Tests/";
            var result = new List<(string, RegistryReference)>();

            foreach (string path in _Package.GetReferenceHolderAssets())
            {
                if (_SkipTests && path.StartsWith(tests, StringComparison.Ordinal)) continue;

                var references = new List<RegistryReference>();
                if (!RegistryAssetScanner.Scan(path, references))
                    _Problems.Add($"\"{path}\" could not be read completely.");

                result.AddRange(references.Select(e => (path, e)));
            }

            return result;
        }
        #endregion



        #region Pack Validation
        /// <summary>
        /// Every reason a package you author can't be packed, for every kind it uses. Empty if it can. Per kind, the
        /// package must ship exactly one valid registry of kind <c>package</c>, its assets must reference only that
        /// registry or the registries of its declared dependencies, never the authoring project's main registry, every
        /// referenced row must exist, and the registry ID must be the one it was packed with before. <br/>
        /// The package's <c>Tests</c> folder is skipped: test fixtures reference registries their tests build themselves.
        /// </summary>
        public static List<string> Validate(RegistryPackage _Package)
        {
            var errors = new List<string>();
            var references = ScanPackage(_Package, true, errors);

            foreach (var kind in RegistryKinds.All)
            {
                var ofKind = references.Where(e => e.Reference.Kind == kind).ToList();
                var published = RegistrySettings.instance.TryGetPublishedRegistry(_Package.Name, kind.Id, out var id) ? id : RegistryGuid.Zero;
                kind.ValidatePack(_Package, ofKind, published, errors);
            }

            int unknown = references.Count(e => e.Reference.Kind == null);
            if (unknown > 0)
                errors.Add($"{unknown} reference(s) could not be matched to a registry kind.");

            return errors;
        }

        /// <summary>
        /// Every reason a package you author can't be packed, for one kind. Empty if it can.
        /// </summary>
        /// <param name="_PublishedRegistryId"> The registry ID the package was packed with before, or zero. </param>
        public static List<string> Validate(RegistryPackage _Package, RegistryKind _Kind, RegistryGuid _PublishedRegistryId)
        {
            if (_Kind == null) throw new ArgumentNullException(nameof(_Kind));

            var errors = new List<string>();
            var references = ScanPackage(_Package, true, errors);

            _Kind.ValidatePack(_Package, references.Where(e => e.Reference.Kind == _Kind).ToList(), _PublishedRegistryId, errors);
            return errors;
        }

        /// <summary>
        /// The registry ID of the package's own registry of each kind, as the next pack must keep them.
        /// </summary>
        private static List<(RegistryKind Kind, RegistryGuid Id)> OwnRegistries(RegistryPackage _Package)
        {
            var result = new List<(RegistryKind, RegistryGuid)>();
            var references = ScanPackage(_Package, true, new List<string>());

            foreach (var kind in RegistryKinds.All)
            {
                var ofKind = references.Where(e => e.Reference.Kind == kind).ToList();
                var id = kind.ValidatePack(_Package, ofKind, RegistryGuid.Zero, new List<string>());
                if (!id.IsZero) result.Add((kind, id));
            }

            return result;
        }
        #endregion



        #region Commands
        private const string GENERATE_MENU = "Assets/LMK Registries/Generate Registries For Package...";
        private const string VALIDATE_MENU = "Assets/LMK Registries/Validate Package";
        private const string PACK_MENU = "Assets/LMK Registries/Pack Package...";

        private static PackRequest packRequest;
        private static RegistryPackage packing;
        private static List<(RegistryKind Kind, RegistryGuid Id)> packingRegistries;

        private static RegistryPackage SelectedPackage(bool _AuthoredOnly)
        {
            var package = RegistryPackage.ForAssetPath(AssetDatabase.GetAssetPath(Selection.activeObject));
            if (package == null || !_AuthoredOnly) return package;

            // Only packages you author can be packed: embedded or local ones.
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(package.Root);
            return info != null && (info.source == PackageSource.Embedded || info.source == PackageSource.Local) ? package : null;
        }

        [MenuItem(GENERATE_MENU, false, 2000)]
        private static void GenerateSelected()
        {
            var package = SelectedPackage(false);
            int choice = EditorUtility.DisplayDialogComplex(
                "Generate Registries For Package",
                $"Generate the registries \"{package.Name}\" needs, so its references resolve without changing the package?",
                "Generate", "Cancel", "Generate, Removing Unused Rows");
            if (choice == 1) return;

            var result = Generate(package, choice == 2);
            if (result.Success) Debug.Log(result.Message);
            else Debug.LogError(result.Error);

            EditorUtility.DisplayDialog("Generate Registries For Package", result.Message, "OK");
        }

        [MenuItem(GENERATE_MENU, true)]
        private static bool HasSelectedPackage() => SelectedPackage(false) != null;

        [MenuItem(VALIDATE_MENU, false, 2010)]
        private static void ValidateSelected()
        {
            var package = SelectedPackage(true);
            var errors = Validate(package);

            if (errors.Count == 0)
                EditorUtility.DisplayDialog("Validate Package", $"\"{package.Name}\" can be packed: its references resolve in any project.", "OK");
            else
                ShowErrors(package, errors);
        }

        [MenuItem(PACK_MENU, false, 2011)]
        private static void PackSelected()
        {
            var package = SelectedPackage(true);
            var errors = Validate(package);
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
            packingRegistries = OwnRegistries(package);
            // The newer overload needs a Unity organization ID, which packages packed for Git or OpenUPM don't have.
#pragma warning disable CS0618
            packRequest = Client.Pack(info.resolvedPath, folder);
#pragma warning restore CS0618
            EditorApplication.update += WaitForPack;
        }

        [MenuItem(VALIDATE_MENU, true)]
        [MenuItem(PACK_MENU, true)]
        private static bool HasSelectedAuthoredPackage() => SelectedPackage(true) != null;

        private static void WaitForPack()
        {
            if (packRequest == null || !packRequest.IsCompleted) return;
            EditorApplication.update -= WaitForPack;

            if (packRequest.Status == StatusCode.Success)
            {
                // Later packs must keep these registry IDs.
                foreach (var (kind, id) in packingRegistries)
                    RegistrySettings.instance.SetPublishedRegistry(packing.Name, kind.Id, id);

                Debug.Log($"Packed \"{packing.Name}\" to \"{packRequest.Result.tarballPath}\". Its registry IDs are recorded in the project settings.");
            }
            else
            {
                Debug.LogError($"Packing \"{packing.Name}\" failed: {packRequest.Error?.message}");
            }

            packRequest = null;
            packing = null;
            packingRegistries = null;
        }

        private static void ShowErrors(RegistryPackage _Package, List<string> _Errors)
        {
            string message = $"\"{_Package.Name}\" can't be packed:\n\n- {string.Join("\n- ", _Errors)}";
            Debug.LogError(message);
            EditorUtility.DisplayDialog("Package Registries Are Not Valid", message, "OK");
        }
        #endregion
    }
}
