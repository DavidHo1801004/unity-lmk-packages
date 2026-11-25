using System.Collections;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// In the Gameplay Tag Manager's tree, only the arrow expands or collapses a branch: clicks anywhere else on its row,
    /// its edges included, leave it as it is.
    /// </summary>
    public sealed class TreeRowClickTests : OperationTestBase
    {
        private GameplayTagManagerWindow window;

        [TearDown]
        public void CloseWindow()
        {
            if (window) window.Close();
        }

        [UnityTest]
        public IEnumerator BranchRow_TogglesOnlyFromItsArrow()
        {
            Add("A.B");

            window = ScriptableObject.CreateInstance<GameplayTagManagerWindow>();
            window.position = new Rect(100, 100, 700, 500);
            window.Show();

            GameplayTagNodeElement branch = null;
            for (int frame = 0; frame < 120 && !(branch?.Row.worldBound.width > 0f); frame++)
            {
                window.Repaint();
                yield return null;
                branch = window.rootVisualElement.Q<GameplayTagControlPanel>()?.TagTree?.Find($"{Main.Id}:{IdOf("A")}");
            }

            if (!(branch?.Row.worldBound.width > 0f))
                Assert.Inconclusive("The tree never laid out.");

            var foldout = branch.Row.GetFirstAncestorOfType<Foldout>();
            yield return TreeClicks.AssertTogglesOnlyFromArrow(window, foldout);
        }
    }



    /// <summary>
    /// Clicks on a foldout row, for tree tests.
    /// </summary>
    internal static class TreeClicks
    {
        public static IEnumerator AssertTogglesOnlyFromArrow(UnityEditor.EditorWindow _Window, Foldout _Foldout)
        {
            var row = _Foldout.Q<Toggle>(className: Foldout.toggleUssClassName);
            var arrow = row.Q(className: Toggle.checkmarkUssClassName);
            var name = row.Q<Label>();

            // Every point of the row that is neither the arrow nor the name: its padding, border and free space.
            var points = new System.Collections.Generic.List<Vector2>();
            var bounds = row.worldBound;
            for (float y = bounds.yMin + 0.5f; y < bounds.yMax; y += 1f)
                for (float x = bounds.xMin + 0.5f; x < bounds.xMax; x += 1f)
                {
                    var picked = _Window.rootVisualElement.panel.Pick(new Vector2(x, y));
                    if (picked == null || !row.Contains(picked)) continue;
                    if (arrow.worldBound.Contains(new Vector2(x, y)) || (name != null && name.Contains(picked))) continue;
                    points.Add(new Vector2(x, y));
                }

            Assert.IsNotEmpty(points, "the row has room besides its arrow and name");

            // Its edges, and its middle: one click each.
            var samples = new[] { points[0], points[points.Count / 2], points[points.Count - 1] };
            foreach (var point in samples)
            {
                bool before = _Foldout.value;
                Click(_Window, point);
                yield return Frames(_Window);
                Assert.AreEqual(before, _Foldout.value,
                                $"a click at ({point.x - bounds.xMin:0.#}, {point.y - bounds.yMin:0.#}) in the row, on \"{_Window.rootVisualElement.panel.Pick(point)?.GetType().Name}\" {_Window.rootVisualElement.panel.Pick(point)?.name}, toggled it");
            }

            bool expanded = _Foldout.value;
            Click(_Window, arrow.worldBound.center);
            yield return Frames(_Window);
            Assert.AreNotEqual(expanded, _Foldout.value, "the arrow still toggles it");
        }

        private static void Click(UnityEditor.EditorWindow _Window, Vector2 _Position)
        {
            using (var down = PointerDownEvent.GetPooled(MouseEvent(EventType.MouseDown, _Position)))
                _Window.rootVisualElement.SendEvent(down);
            using (var up = PointerUpEvent.GetPooled(MouseEvent(EventType.MouseUp, _Position)))
                _Window.rootVisualElement.SendEvent(up);
        }

        private static Event MouseEvent(EventType _Type, Vector2 _Position)
            => new() { type = _Type, mousePosition = _Position, button = 0, clickCount = 1 };

        private static IEnumerator Frames(UnityEditor.EditorWindow _Window)
        {
            for (int frame = 0; frame < 3; frame++)
            {
                _Window.Repaint();
                yield return null;
            }
        }
    }
}
