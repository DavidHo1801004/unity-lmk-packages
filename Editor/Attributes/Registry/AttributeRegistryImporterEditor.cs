using UnityEditor;

using LMK.Editor.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Inspector of an attribute registry file: its name, kind, ID and attribute count, whether code generation
    /// writes a class for it, and a button to open it in the Attribute Manager. The file itself is only edited there.
    /// The registry core's inspector, for the attribute kind.
    /// </summary>
    [CustomEditor(typeof(AttributeRegistryImporter))]
    internal sealed class AttributeRegistryImporterEditor : RegistryImporterEditor<AttributeRegistryRow, AttributeRegistryAsset>
    {
        protected override RegistryKindEditor<AttributeRegistryRow, AttributeRegistryAsset> Kind => AttributeKind.Instance;
    }
}
