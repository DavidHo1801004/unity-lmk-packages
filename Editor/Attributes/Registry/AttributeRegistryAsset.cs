using LMK.Editor.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Imported content of an attribute registry file. Read-only: registry files are only written by
    /// <see cref="AttributeOperations"/> and explicit commands, never through this asset.
    /// </summary>
    internal sealed class AttributeRegistryAsset : RegistryAsset<AttributeRegistryRow> { }
}
