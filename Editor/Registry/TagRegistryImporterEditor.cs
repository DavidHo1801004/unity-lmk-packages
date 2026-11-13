using UnityEditor;

using LMK.Editor.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Inspector of a registry file: its name, kind, ID and tag count, whether code generation writes a class
    /// for it, and a button to open it in the Gameplay Tag Manager. The file itself is only edited there.
    /// The registry core's inspector, for the tag kind.
    /// </summary>
    [CustomEditor(typeof(TagRegistryImporter))]
    internal sealed class TagRegistryImporterEditor : RegistryImporterEditor<TagRegistryRow, TagRegistryAsset>
    {
        protected override RegistryKindEditor<TagRegistryRow, TagRegistryAsset> Kind => TagKind.Instance;
    }
}
