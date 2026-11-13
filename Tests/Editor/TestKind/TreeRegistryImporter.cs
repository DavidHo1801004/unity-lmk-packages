using UnityEditor.AssetImporters;

namespace LMK.Editor.Registry.Tests
{
    [ScriptedImporter(1, TreeFormat.EXTENSION, RegistryImport.IMPORT_ORDER)]
    public sealed class TreeRegistryImporter : RegistryImporter<TreeRow, TreeRegistryAsset>
    {
        protected override IRegistryFormat<TreeRow> Format => TreeFormat.Instance;
    }
}
