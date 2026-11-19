using System.Collections.Generic;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

using LMK.Runtime.AbilitySystem;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Inspector of <see cref="GameplayAbility"/>, in sections: Tags, Cost &amp; Cooldown, Policies and Logic.
    /// </summary>
    [CustomEditor(typeof(GameplayAbility)), CanEditMultipleObjects]
    internal sealed class GameplayAbilityEditor : UnityEditor.Editor
    {
        private static readonly string[] TAGS =
        {
            nameof(GameplayAbility.abilityTags),
            nameof(GameplayAbility.cancelAbilitiesWithTag),
            nameof(GameplayAbility.blockAbilitiesWithTag),
            nameof(GameplayAbility.activationOwnedTags),
            nameof(GameplayAbility.activationRequiredTags),
            nameof(GameplayAbility.activationBlockedTags),
            nameof(GameplayAbility.sourceRequiredTags),
            nameof(GameplayAbility.sourceBlockedTags),
            nameof(GameplayAbility.targetRequiredTags),
            nameof(GameplayAbility.targetBlockedTags),
        };

        private static readonly string[] COST_AND_COOLDOWN =
        {
            nameof(GameplayAbility.costEffect),
            nameof(GameplayAbility.cooldownEffect),
        };

        private static readonly string[] POLICIES =
        {
            nameof(GameplayAbility.instancingPolicy),
            nameof(GameplayAbility.retriggerInstancedAbility),
            nameof(GameplayAbility.triggers),
            nameof(GameplayAbility.netExecutionPolicy),
        };

        public override VisualElement CreateInspectorGUI()
        {
            var root = InspectorSection.CreateRoot();
            var drawn = new HashSet<string> { "m_Script" };
            var type = typeof(GameplayAbility);

            void Section(string _Title, IEnumerable<string> _Paths)
            {
                var section = InspectorSection.Create(root, type, _Title);
                foreach (string path in _Paths)
                {
                    drawn.Add(path);
                    if (InspectorSection.IsList(serializedObject.FindProperty(path)))
                        InspectorSection.List(section, serializedObject, path, type, ObjectNames.NicifyVariableName(path));
                    else
                        InspectorSection.Field(section, serializedObject, path);
                }
            }

            Section("Tags", TAGS);
            Section("Cost & Cooldown", COST_AND_COOLDOWN);
            Section("Policies", POLICIES);
            Section("Logic", new[] { nameof(GameplayAbility.logic) });

            // Fields added to the class later still show, until they get a place.
            VisualElement other = null;
            var property = serializedObject.GetIterator();
            for (bool enter = true; property.NextVisible(enter); enter = false)
            {
                if (drawn.Contains(property.propertyPath)) continue;

                other ??= InspectorSection.Create(root, type, "Other");
                other.Add(new PropertyField(property.Copy()));
            }

            return root;
        }
    }
}
