using UnityEditor.AssetImporters;
using UnityEngine;

using LMK.Editor.Registry;
using LMK.Editor.Utilities;

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
    [ScriptedImporter(2, TagRegistryFile.EXTENSION, RegistryImport.IMPORT_ORDER)]
    internal sealed class TagRegistryImporter : RegistryImporter<TagRegistryRow, TagRegistryAsset>
    {
        private const string ICON_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/Sprites/tag-multiple_white-64.png";

        protected override IRegistryFormat<TagRegistryRow> Format => TagKind.Instance;

        protected override Texture2D Icon => LMKAssetUtility.Load<Texture2D>(ICON_PATH);
    }
}
