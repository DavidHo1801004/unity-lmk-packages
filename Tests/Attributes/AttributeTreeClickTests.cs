using System.Collections;
using System.Collections.Generic;

using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// In the Attribute Manager's tree, only the arrow expands or collapses a branch: clicks anywhere else on its row,
    /// its edges included, leave it as it is.
    /// </summary>
    public sealed class AttributeTreeClickTests : AttributeKindTestBase
    {
        private AttributeManagerWindow window;

        [TearDown]
        public void CloseWindow()
        {
            if (window) window.Close();
        }

        [UnityTest]
        public IEnumerator BranchRow_TogglesOnlyFromItsArrow()
        {
            AddAttribute("Vitals.Health");

            window = ScriptableObject.CreateInstance<AttributeManagerWindow>();
            window.position = new Rect(100, 100, 700, 500);
            window.Show();

            AttributeNodeElement group = null;
            for (int frame = 0; frame < 120 && !(group?.Row.worldBound.width > 0f); frame++)
            {
                window.Repaint();
                yield return null;
                group = window.Tree?.Find(AttributeTreeNode.KeyOfGroup(AttributeRegistryId, "Vitals"));
            }

            if (!(group?.Row.worldBound.width > 0f))
                Assert.Inconclusive("The tree never laid out.");

            var foldout = group.Row.GetFirstAncestorOfType<Foldout>();
            var row = group.Row;
            var arrow = row.Q(className: Toggle.checkmarkUssClassName);
            var name = row.Q<Label>();

            // Every point of the row that is neither the arrow nor the name: its padding, border and free space.
            var points = new List<Vector2>();
            var bounds = row.worldBound;
            for (float y = bounds.yMin + 0.5f; y < bounds.yMax; y += 1f)
                for (float x = bounds.xMin + 0.5f; x < bounds.xMax; x += 1f)
                {
                    var picked = window.rootVisualElement.panel.Pick(new Vector2(x, y));
                    if (picked == null || !row.Contains(picked)) continue;
                    if (arrow.worldBound.Contains(new Vector2(x, y)) || (name != null && name.Contains(picked))) continue;
                    points.Add(new Vector2(x, y));
                }

            Assert.IsNotEmpty(points, "the row has room besides its arrow and name");

            foreach (var point in new[] { points[0], points[points.Count / 2], points[points.Count - 1] })
            {
                bool before = foldout.value;
                Click(point);
                yield return Frames();
                var picked = window.rootVisualElement.panel.Pick(point);
                Assert.AreEqual(before, foldout.value,
                                $"a click at ({point.x - bounds.xMin:0.#}, {point.y - bounds.yMin:0.#}) in the row, on {picked?.GetType().Name} \"{picked?.name}\", toggled it");
            }

            bool expanded = foldout.value;
            Click(arrow.worldBound.center);
            yield return Frames();
            Assert.AreNotEqual(expanded, foldout.value, "the arrow still toggles it");
        }

        private void Click(Vector2 _Position)
        {
            using (var down = PointerDownEvent.GetPooled(MouseEvent(EventType.MouseDown, _Position)))
                window.rootVisualElement.SendEvent(down);
            using (var up = PointerUpEvent.GetPooled(MouseEvent(EventType.MouseUp, _Position)))
                window.rootVisualElement.SendEvent(up);
        }

        private static Event MouseEvent(EventType _Type, Vector2 _Position)
            => new() { type = _Type, mousePosition = _Position, button = 0, clickCount = 1 };

        private IEnumerator Frames()
        {
            for (int frame = 0; frame < 3; frame++)
            {
                window.Repaint();
                yield return null;
            }
        }
    }
}
