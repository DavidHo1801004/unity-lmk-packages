using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;

using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// A package, as the package registry commands see it.
    /// </summary>
    public sealed class RegistryPackage
    {
        public string Name;
        public string Version;
        public string DisplayName;

        /// <summary>
        /// Asset path of the package folder, e.g. <c>Packages/com.vendor.combat</c>.
        /// </summary>
        public string Root;

        /// <summary>
        /// Names of the packages it lists as dependencies in <c>package.json</c>.
        /// </summary>
        public List<string> Dependencies = new();

        public static RegistryPackage From(PackageInfo _Info) => _Info == null ? null : new RegistryPackage
        {
            Name = _Info.name,
            Version = _Info.version,
            DisplayName = string.IsNullOrEmpty(_Info.displayName) ? _Info.name : _Info.displayName,
            Root = _Info.assetPath,
            Dependencies = _Info.dependencies?.Select(e => e.name).ToList() ?? new List<string>(),
        };

        /// <summary>
        /// The package that holds an asset or folder, or <see langword="null"/>.
        /// </summary>
        public static RegistryPackage ForAssetPath(string _AssetPath)
            => string.IsNullOrEmpty(_AssetPath) ? null : From(PackageInfo.FindForAssetPath(_AssetPath));

        /// <summary>
        /// The asset paths under <see cref="Root"/> that can hold references.
        /// </summary>
        public List<string> GetReferenceHolderAssets()
            => AssetPathsUnder(Root).Where(RegistryAssetScanner.CanHoldReferences).ToList();

        /// <summary>
        /// Every asset path under <see cref="Root"/> with an extension, e.g. ".tagregistry".
        /// </summary>
        public List<string> GetAssets(string _Extension)
            => AssetPathsUnder(Root).Where(e => e.EndsWith(_Extension, StringComparison.OrdinalIgnoreCase)).ToList();

        public static IEnumerable<string> AssetPathsUnder(string _Root)
        {
            string prefix = _Root.TrimEnd('/') + "/";
            return AssetDatabase.GetAllAssetPaths()
                .Where(e => e.StartsWith(prefix, StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(e))
                .OrderBy(e => e, StringComparer.Ordinal);
        }
    }
}
