using UnityEditor;
using UnityEngine.UIElements;

using LMK.Runtime.AbilitySystem;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Inspector of <see cref="AttributeInitialValues"/>: one starting value per attribute, each attribute once.
    /// </summary>
    [CustomEditor(typeof(AttributeInitialValues)), CanEditMultipleObjects]
    internal sealed class AttributeInitialValuesEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = InspectorSection.CreateRoot();

            var values = AttributeEntryList.Create(root, serializedObject, "entries", "value", typeof(AttributeInitialValues), "Initial Values",
                                                   "No values. Unlisted attributes start at their registry default.");
            InspectorSection.Note(InspectorSection.Content(values), "Used by the Ability System Component that references this asset, for the attributes its sets add. " +
                                                                    "A curve multiplies the value by the unit's level.");

            return root;
        }
    }
}
