using UnityEditor;

namespace LMK.Editor.Registry.Tests
{
    /// <summary>
    /// Inspector of the tree test kind's registry files.
    /// </summary>
    [CustomEditor(typeof(TreeRegistryImporter))]
    public sealed class TreeRegistryImporterEditor : RegistryImporterEditor<TreeRow, TreeRegistryAsset>
    {
        protected override RegistryKindEditor<TreeRow, TreeRegistryAsset> Kind => RegistryKinds.Find("tree") as TreeKind;
    }
}
