using System;
using System.Collections.Generic;

using UnityEditor.PackageManager;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Installed packages, as registry loading sees them.
    /// </summary>
    public static class RegistryPackages
    {
        /// <summary>
        /// Replaces the installed package list. Only tests set it, including the tests of domain packages.
        /// </summary>
        public static Func<IReadOnlyDictionary<string, string>> InstalledPackagesOverride { get; set; }

        /// <summary>
        /// Installed packages: name to version.
        /// </summary>
        public static IReadOnlyDictionary<string, string> GetInstalled()
        {
            if (InstalledPackagesOverride != null)
                return InstalledPackagesOverride();

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
                result[package.name] = package.version;

            return result;
        }

        /// <summary>
        /// Whether a registry loads: always, except a generated registry whose package isn't installed.
        /// </summary>
        public static bool IsActive(RegistryOrigin _Origin, string _Package, IReadOnlyDictionary<string, string> _Installed)
            => _Origin != RegistryOrigin.Generated
            || string.IsNullOrEmpty(_Package)
            || _Installed.ContainsKey(_Package);
    }
}
