using UnityEngine;

using LMK.Editor.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Imported content of a registry file. Read-only: registry files are only written by
    /// <see cref="TagOperations"/> and explicit commands, never through this asset.
    /// </summary>
    /// <remarks>
    /// The importer gives the asset its Project window icon; the class's icon is the one its Inspector header shows.
    /// </remarks>
    [Icon(ICON_PATH)]
    internal sealed class TagRegistryAsset : RegistryAsset<TagRegistryRow>
    {
        internal const string ICON_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/Icons/TagRegistry.png";
    }
}
