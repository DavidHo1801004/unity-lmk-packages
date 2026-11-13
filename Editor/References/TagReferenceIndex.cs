using System;
using System.Collections.Generic;
using System.Linq;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Which saved assets reference which tags, for the Delete dialog, Find References, Relink and Replace: the tag
    /// references of the registry core's <see cref="RegistryReferenceIndex"/>.
    /// </summary>
    /// <remarks>
    /// Kept only in <c>Library/</c>, never committed. Every query scans the assets that are new or changed first, so
    /// results are accurate for saved data. After a <c>Library/</c> wipe, or a 1.0.0 index, the first query scans
    /// everything.
    /// </remarks>
    internal static class TagReferenceIndex
    {
        public const string DEFAULT_PATH = RegistryReferenceIndex.DEFAULT_PATH;

        /// <summary>
        /// Where the index is stored. Only tests change it.
        /// </summary>
        internal static string IndexPath
        {
            get => RegistryReferenceIndex.IndexPath;
            set => RegistryReferenceIndex.IndexPath = value;
        }

        /// <summary>
        /// Number of assets the last query had to scan.
        /// </summary>
        internal static int LastScanCount => RegistryReferenceIndex.LastScanCount;

        /// <summary>
        /// Replaces the progress bar, for tests: called with the asset being scanned and the progress, returns
        /// whether to cancel.
        /// </summary>
        internal static Func<string, float, bool> ProgressOverride
        {
            get => RegistryReferenceIndex.ProgressOverride;
            set => RegistryReferenceIndex.ProgressOverride = value;
        }

        /// <summary>
        /// Whether assets at <paramref name="_Path"/> are indexed: project assets that can hold serialized tags.
        /// </summary>
        public static bool IsIndexed(string _Path) => RegistryReferenceIndex.IsIndexed(_Path);

        /// <summary>
        /// Saved assets with a tag reference matching <paramref name="_Match"/>, sorted by path. Stale entries are
        /// scanned first.
        /// </summary>
        /// <returns> Whether every stale entry was scanned. False if the user cancelled the scan. </returns>
        public static bool TryFindAssets(Func<TagKey, bool> _Match, bool _ShowProgress, out List<string> _AssetPaths)
            => RegistryReferenceIndex.TryFindAssets(key => key.Kind == TagKind.ID && _Match(TagKey.Of(key)), _ShowProgress, out _AssetPaths);

        /// <summary>
        /// Saved assets referencing any of <paramref name="_Tags"/>, sorted by path.
        /// </summary>
        /// <returns> Whether every stale entry was scanned. False if the user cancelled the scan. </returns>
        public static bool TryFindAssets(IReadOnlyCollection<GameplayTag> _Tags, bool _ShowProgress, out List<string> _AssetPaths)
            => RegistryReferenceIndex.TryFindAssets(TagKind.Instance, _Tags.Select(e => (e.RegistryId, e.Id)).ToList(), _ShowProgress, out _AssetPaths);

        /// <summary>
        /// The tags an asset references, as last indexed. Empty if it isn't indexed.
        /// </summary>
        public static IReadOnlyCollection<TagKey> GetTags(string _AssetPath)
            => RegistryReferenceIndex.GetReferences(_AssetPath).Where(e => e.Kind == TagKind.ID).Select(e => TagKey.Of(e)).ToList();

        /// <summary>
        /// Bring the index up to date: forget deleted assets and scan the new or changed ones.
        /// </summary>
        /// <returns> Whether every stale entry was scanned. False if the user cancelled the scan. </returns>
        public static bool Refresh(bool _ShowProgress) => RegistryReferenceIndex.Refresh(_ShowProgress);

        /// <summary>
        /// Forget everything, as after a <c>Library/</c> wipe. The next query scans every asset.
        /// </summary>
        public static void Clear() => RegistryReferenceIndex.Clear();

        /// <summary>
        /// Forget everything, then scan every asset.
        /// </summary>
        public static bool Rebuild(bool _ShowProgress) => RegistryReferenceIndex.Rebuild(_ShowProgress);

        /// <summary>
        /// Write the index to <c>Library/</c>, if it changed.
        /// </summary>
        internal static void Flush() => RegistryReferenceIndex.Flush();
    }
}
