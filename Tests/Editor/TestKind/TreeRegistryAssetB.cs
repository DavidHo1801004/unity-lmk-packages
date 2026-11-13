using UnityEditor.AssetImporters;

namespace LMK.Editor.Registry.Tests
{
    /// <summary>
    /// Imported registry of the second test kind. In its own file so Unity can create it.
    /// </summary>
    public sealed class TreeRegistryAssetB : RegistryAsset<TreeRow> { }



    [ScriptedImporter(1, TreeKindB.EXTENSION, RegistryImport.IMPORT_ORDER)]
    public sealed class TreeRegistryImporterB : RegistryImporter<TreeRow, TreeRegistryAssetB>
    {
        protected override IRegistryFormat<TreeRow> Format => TreeFormat.Instance;
    }
}
