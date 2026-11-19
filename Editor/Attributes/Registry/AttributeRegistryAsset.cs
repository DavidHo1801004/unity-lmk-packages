using UnityEngine;

using LMK.Editor.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Imported content of an attribute registry file. Read-only: registry files are only written by
    /// <see cref="AttributeOperations"/> and explicit commands, never through this asset.
    /// </summary>
    /// <remarks>
    /// The importer gives the asset its Project window icon; the class's icon is the one its Inspector header shows.
    /// </remarks>
    [Icon(ICON_PATH)]
    internal sealed class AttributeRegistryAsset : RegistryAsset<AttributeRegistryRow>
    {
        internal const string ICON_PATH = "Packages/com.lmk.abilitysystem/Editor/Assets/Icons/AttributeRegistry.png";
    }
}
