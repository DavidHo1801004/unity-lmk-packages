using UnityEditor.AssetImporters;

using LMK.Editor.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Imports attribute registry files (<c>*.attrregistry</c>). Parses and validates them, and reports problems.
    /// Never writes anything.
    /// </summary>
    /// <remarks>
    /// Imported early, so registries are ready before assets that reference attributes are processed. The registry
    /// core reloads the registries when a registry file changes.
    /// </remarks>
    [ScriptedImporter(1, AttributeKind.EXTENSION, RegistryImport.IMPORT_ORDER)]
    internal sealed class AttributeRegistryImporter : RegistryImporter<AttributeRegistryRow, AttributeRegistryAsset>
    {
        protected override IRegistryFormat<AttributeRegistryRow> Format => AttributeKind.Instance;
    }
}
