using LMK.Editor.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Imported content of a registry file. Read-only: registry files are only written by
    /// <see cref="TagOperations"/> and explicit commands, never through this asset.
    /// </summary>
    internal sealed class TagRegistryAsset : RegistryAsset<TagRegistryRow> { }
}
