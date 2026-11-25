using UnityEditor.AssetImporters;

using LMK.Editor.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Imports registry files (<c>*.tagregistry</c>). Parses and validates them, and reports problems.
    /// Never writes anything.
    /// </summary>
    /// <remarks>
    /// Imported early, so registries are ready before assets that reference tags are processed. The registries
    /// are reloaded by the registry core when a registry file changes.
    /// </remarks>
    [ScriptedImporter(4, TagRegistryFile.EXTENSION, RegistryImport.IMPORT_ORDER)]
    internal sealed class TagRegistryImporter : RegistryImporter<TagRegistryRow, TagRegistryAsset>
    {
        protected override IRegistryFormat<TagRegistryRow> Format => TagKind.Instance;

        protected override string IconPath => TagRegistryAsset.ICON_PATH;
    }
}
