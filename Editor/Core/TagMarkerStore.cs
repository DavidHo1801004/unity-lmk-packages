using System.Collections.Generic;

using LMK.Editor.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// The user's tag markers file: colours one user gave tags, shown as a strip on their rows in tag trees.
    /// Editor-only and per user, under <c>UserSettings/</c>. Read and written by the registry core.
    /// </summary>
    internal static class TagMarkerStore
    {
        public const string DEFAULT_FILE_PATH = "UserSettings/LMKGameplayTags/Markers.json";

        /// <summary>
        /// Where the markers are saved. Only tests change it.
        /// </summary>
        internal static string FilePath
        {
            get => TagKind.Instance.MarkersPath;
            set => TagKind.Instance.MarkersPath = value;
        }

        /// <summary>
        /// The saved markers. Empty if the file is missing or unreadable.
        /// </summary>
        public static List<RegistryMarker> Load() => RegistryMarkerStore.Load(FilePath);
    }
}
