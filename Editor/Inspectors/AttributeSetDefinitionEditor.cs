using UnityEditor;
using UnityEngine.UIElements;

using LMK.Runtime.AbilitySystem;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Inspector of <see cref="AttributeSetDefinition"/>: the attributes, each once and picked from the registries, and
    /// the behaviours.
    /// </summary>
    [CustomEditor(typeof(AttributeSetDefinition)), CanEditMultipleObjects]
    internal sealed class AttributeSetDefinitionEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = InspectorSection.CreateRoot();
            var type = typeof(AttributeSetDefinition);

            var attributes = AttributeEntryList.Create(root, serializedObject, "entries", null, type, "Attributes",
                                                       "No attributes. Add the attributes units with this set have.");
            InspectorSection.Note(InspectorSection.Content(attributes), "Starting values come from the registry defaults, or from the unit's Attribute Initial Values.");

            InspectorSection.List(root, serializedObject, "behaviours", type, "Behaviours", "No behaviours.");

            return root;
        }
    }
}
