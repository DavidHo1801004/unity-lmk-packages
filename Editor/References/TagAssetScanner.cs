using System.Collections.Generic;

using UnityEngine;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Reads the tag references saved in an asset file, without changing it: the tag references found by the
    /// registry core's <see cref="RegistryAssetScanner"/>.
    /// </summary>
    /// <remarks>
    /// Text-serialized files are read as text, which is fast. Binary files are loaded and walked instead.
    /// Values a prefab instance inherits from its prefab are not returned: the prefab holds them. A prefab instance
    /// override that changed only the tag ID has no registry: its <see cref="GameplayTag.RegistryId"/> is zero
    /// (see <see cref="TagKey"/>).
    /// </remarks>
    internal static class TagAssetScanner
    {
        /// <summary>
        /// Whether a file can hold tag references: assets, prefabs and scenes, not scripts or registries.
        /// </summary>
        public static bool CanHoldTags(string _Path) => RegistryAssetScanner.CanHoldReferences(_Path);

        /// <summary>
        /// Add the tag references saved in an asset file.
        /// </summary>
        /// <returns> Whether the file could be read. </returns>
        public static bool Scan(string _Path, List<GameplayTag> _Result)
        {
            var references = new List<RegistryReference>();
            bool read = RegistryAssetScanner.Scan(_Path, references);
            AddTags(references, _Result);

            return read;
        }

        /// <summary>
        /// The fast path for text-serialized files: tag values, and prefab instance overrides of tags.
        /// </summary>
        internal static void ScanText(string _Text, List<GameplayTag> _Result)
        {
            var references = new List<RegistryReference>();
            RegistryAssetScanner.ScanText(_Text, references);

            foreach (var reference in references)
                _Result.Add(ToTag(reference));
        }

        /// <summary>
        /// Add the tag references held by loaded objects.
        /// </summary>
        public static void Collect(IEnumerable<Object> _Objects, List<GameplayTag> _Result)
        {
            var references = new List<RegistryReference>();
            RegistryAssetScanner.Collect(_Objects, references);
            AddTags(references, _Result);
        }

        /// <summary>
        /// The tag a reference of the tag kind stands for.
        /// </summary>
        internal static GameplayTag ToTag(in RegistryReference _Reference)
            => GameplayTag.FromId(_Reference.Registry, _Reference.Id, _Reference.Name);

        private static void AddTags(List<RegistryReference> _References, List<GameplayTag> _Result)
        {
            foreach (var reference in _References)
                if (reference.Kind == null || reference.Kind == TagKind.Instance)
                    _Result.Add(ToTag(reference));
        }
    }
}
