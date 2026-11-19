using System;
using System.Collections;
using System.Linq;

using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using LMK.Runtime.AbilitySystem;

using Object = UnityEngine.Object;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// A window that hosts an inspector, so tests get real layout.
    /// </summary>
    internal sealed class InspectorTestWindow : EditorWindow { }



    /// <summary>
    /// The asset inspectors' sections leave room for what Unity draws at their left edge, such as the foldout arrows of
    /// lists and nested fields.
    /// </summary>
    public sealed class InspectorLayoutTests
    {
        private const string SECTION_CONTENT = "lmk-collapsible-panel__content";

        private InspectorTestWindow window;
        private Object target;

        [TearDown]
        public void TearDown()
        {
            if (window) window.Close();
            if (target) Object.DestroyImmediate(target);
        }

        [UnityTest]
        public IEnumerator FoldoutArrows_StayInsideTheirSection(
            [Values(typeof(GameplayEffect), typeof(GameplayAbility), typeof(AttributeSetDefinition))] Type _Type)
        {
            target = ScriptableObject.CreateInstance(_Type);
            if (target is GameplayEffect effect)
                effect.DurationPolicy = DurationPolicy.HasDuration;  // shows every section

            // An element in a list, so its row has a foldout: empty lists show none.
            if (target is GameplayAbility ability)
                ability.triggers.Add(new AbilityTrigger());
            if (target is AttributeSetDefinition set)
                set.Add(new TransferMetaAttribute());

            window = ScriptableObject.CreateInstance<InspectorTestWindow>();
            window.position = new Rect(100, 100, 420, 900);
            window.Show();

            var inspector = new InspectorElement(target);
            var scroll = new ScrollView();
            scroll.Add(inspector);
            window.rootVisualElement.Add(scroll);

            for (int frame = 0; frame < 60 && inspector.Query<Foldout>().ToList().All(e => !(e.worldBound.width > 0f)); frame++)
            {
                window.Repaint();
                yield return null;
            }

            for (int frame = 0; frame < 5; frame++)
            {
                window.Repaint();
                yield return null;
            }

            var sections = inspector.Query(className: SECTION_CONTENT).ToList();
            Assert.IsNotEmpty(sections, "the inspector has sections");

            int checkedArrows = 0;
            foreach (var section in sections.Where(e => e.resolvedStyle.display == DisplayStyle.Flex && e.worldBound.width > 0f))
            {
                foreach (var foldout in section.Query<Foldout>().ToList())
                {
                    var arrow = foldout.Q(className: Toggle.checkmarkUssClassName);
                    if (arrow == null || !(arrow.worldBound.width > 0f) || arrow.resolvedStyle.display == DisplayStyle.None) continue;

                    checkedArrows++;
                    Assert.GreaterOrEqual(arrow.worldBound.xMin, section.worldBound.xMin - 0.5f,
                                          $"The arrow of \"{foldout.text}\" starts {section.worldBound.xMin - arrow.worldBound.xMin:0.#} px left of its section's content.");
                }
            }

            if (checkedArrows == 0)
                Assert.Inconclusive("No foldout arrow was laid out.");
        }
    }
}
