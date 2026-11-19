using System.Collections.Generic;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;
using LMK.Runtime.AbilitySystem;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Inspector of <see cref="GameplayEffect"/>, in sections: Duration (with the period), Modifiers, Executions,
    /// Components, Stacking and Cues. Fields that do nothing for the duration policy are hidden: the duration for
    /// anything but <see cref="DurationPolicy.HasDuration"/>, the period and stacking for
    /// <see cref="DurationPolicy.Instant"/>.
    /// </summary>
    [CustomEditor(typeof(GameplayEffect)), CanEditMultipleObjects]
    internal sealed class GameplayEffectEditor : UnityEditor.Editor
    {
        private const string DURATION_POLICY = "durationPolicy";

        public override VisualElement CreateInspectorGUI()
        {
            var root = InspectorSection.CreateRoot();
            var drawn = new HashSet<string> { "m_Script" };
            var type = typeof(GameplayEffect);

            PropertyField Field(VisualElement _Parent, string _Path, string _Label = null)
            {
                drawn.Add(_Path);
                return InspectorSection.Field(_Parent, serializedObject, _Path, _Label);
            }

            var duration = InspectorSection.Create(root, type, "Duration");
            Field(duration, DURATION_POLICY, "Policy");
            var durationMagnitude = Field(duration, "durationMagnitude", "Duration");

            var period = new VisualElement { name = "period" };
            duration.Add(period);
            Field(period, "period");
            Field(period, "executePeriodicEffectOnApplication", "Execute On Application");
            Field(period, "periodicInhibitionPolicy", "Inhibition Policy");

            PropertyList ListSection(string _Path, string _Title, string _EmptyText)
            {
                drawn.Add(_Path);
                return InspectorSection.List(root, serializedObject, _Path, type, _Title, _EmptyText);
            }

            ListSection("modifiers", "Modifiers", "No modifiers.");
            ListSection("executions", "Executions", "No executions.");
            ListSection("components", "Components", "No components.");

            // The stacking config's own fields, without a second foldout inside the section.
            var stacking = InspectorSection.Create(root, type, "Stacking");
            drawn.Add("stacking");
            var stackingProperty = serializedObject.FindProperty("stacking");
            var end = stackingProperty.GetEndProperty();
            var child = stackingProperty.Copy();
            for (bool enter = true; child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end); enter = false)
                stacking.Add(new PropertyField(child.Copy()));

            var cues = ListSection("cues", "Cues", "No cues.");
            Field(InspectorSection.Content(cues), "suppressStackingCues");

            // Fields added to the class later still show, until they get a place.
            AddRemaining(root, drawn);

            void UpdateVisibility()
            {
                var policy = serializedObject.FindProperty(DURATION_POLICY);
                if (policy == null) return;

                // With several effects selected, show everything that applies to any of them.
                bool mixed = policy.hasMultipleDifferentValues;
                var value = (DurationPolicy)policy.enumValueIndex;

                InspectorSection.SetShown(durationMagnitude, mixed || value == DurationPolicy.HasDuration);
                InspectorSection.SetShown(period, mixed || value != DurationPolicy.Instant);
                InspectorSection.SetShown(stacking, mixed || value != DurationPolicy.Instant);
            }

            root.TrackPropertyValue(serializedObject.FindProperty(DURATION_POLICY), _ => UpdateVisibility());
            UpdateVisibility();

            return root;
        }

        private void AddRemaining(VisualElement _Root, HashSet<string> _Drawn)
        {
            VisualElement other = null;
            var property = serializedObject.GetIterator();
            for (bool enter = true; property.NextVisible(enter); enter = false)
            {
                if (_Drawn.Contains(property.propertyPath)) continue;

                other ??= InspectorSection.Create(_Root, typeof(GameplayEffect), "Other");
                other.Add(new PropertyField(property.Copy()));
            }
        }
    }
}
