using System;
using System.Collections.Generic;
using System.Linq;

using LMK.Editor.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Registries generated for installed packages that ship none, through the registry core's
    /// <see cref="RegistryPackageService"/>. A package's tag files add their tags to its generated registry
    /// (<see cref="TagKind"/>).
    /// </summary>
    /// <remarks>
    /// Package events (an install, a teammate's pull) only reload the registries and prompt: generated registries are
    /// written only by the explicit command.
    /// </remarks>
    internal static class PackageRegistryService
    {
        public const string GENERATED_FOLDER = TagRegistryService.ROOT_FOLDER + "/Packages";

        /// <summary>
        /// Where generated registries are written. Only tests change it.
        /// </summary>
        internal static string GeneratedFolder
        {
            get => TagKind.Instance.GeneratedRegistryFolder;
            set => TagKind.Instance.GeneratedRegistryFolder = value;
        }

        /// <summary>
        /// Replaces the installed packages (name → version), for tests.
        /// </summary>
        internal static Func<IReadOnlyDictionary<string, string>> InstalledPackagesOverride
        {
            get => RegistryPackages.InstalledPackagesOverride;
            set => RegistryPackages.InstalledPackagesOverride = value;
        }

        /// <summary>
        /// Installed packages: name → version.
        /// </summary>
        public static IReadOnlyDictionary<string, string> GetInstalledPackages() => RegistryPackages.GetInstalled();

        /// <summary>
        /// Packages were added, updated or removed. Reload the registries, and prompt to regenerate the ones that are
        /// out of date. Writes nothing.
        /// </summary>
        /// <returns> The prompts shown. </returns>
        internal static List<string> OnPackagesChanged(IEnumerable<string> _Packages) => RegistryPackageService.OnPackagesChanged(_Packages);

        /// <summary>
        /// Generated tag registries whose package is installed in another version than they were generated from.
        /// </summary>
        public static List<(TagRegistrySnapshot.Registry Registry, string InstalledVersion)> FindOutdated(TagRegistrySnapshot _Snapshot)
        {
            var result = new List<(TagRegistrySnapshot.Registry, string)>();
            foreach (var (_, path, _, installed) in TagKind.Instance.FindOutdatedRegistries())
            {
                var registry = _Snapshot.Registries.FirstOrDefault(e => e.AssetPath == path);
                if (registry != null)
                    result.Add((registry, installed));
            }

            return result;
        }

        /// <summary>
        /// Generate the registries a package needs, or regenerate them. Requires the lock.
        /// </summary>
        /// <param name="_RemoveMissing"> Remove tags that the package no longer references or declares. Otherwise existing tags are kept. </param>
        public static RegistryPackageResult Generate(RegistryPackage _Package, bool _RemoveMissing) => RegistryPackageService.Generate(_Package, _RemoveMissing);
    }
}
