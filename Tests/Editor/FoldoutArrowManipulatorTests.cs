using System.Collections;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;

namespace LMK.Editor.Tests
{
    public class FoldoutArrowManipulatorTests : WindowTestFixture
    {
        private Foldout foldout;

        private Toggle header;

        private VisualElement arrow;

        private FoldoutArrowManipulator manipulator;

        // Foldout changes that reached a listener outside the foldout.
        private readonly List<bool> parentChanges = new();

        // Elements pressed in the header, to check presses land and still reach it.
        private readonly List<VisualElement> headerPresses = new();

        private int headerClicks;

        private IEnumerator SetUpFoldout()
        {
            parentChanges.Clear();
            headerPresses.Clear();
            headerClicks = 0;

            foldout = new Foldout { text = "Group", value = true };
            foldout.Add(new Label("Child"));
            Root.Add(foldout);
            Root.RegisterCallback<ChangeEvent<bool>>(e =>
            {
                if (e.target == foldout) parentChanges.Add(e.newValue);
            });

            header = foldout.Q<Toggle>(className: Foldout.toggleUssClassName);
            arrow = header.Q(className: Toggle.checkmarkUssClassName);
            header.RegisterCallback<PointerDownEvent>(e => headerPresses.Add(e.target as VisualElement), TrickleDown.TrickleDown);
            header.RegisterCallback<ClickEvent>(_ => headerClicks++);

            manipulator = new FoldoutArrowManipulator();
            foldout.AddManipulator(manipulator);

            yield return WaitForLayout(arrow);
        }

        private void ClickAndAssertLanded(Vector2 _Position)
        {
            int presses = headerPresses.Count;
            Click(_Position);
            Assert.That(headerPresses.Count, Is.EqualTo(presses + 1), "the press landed in the header");
        }

        [UnityTest]
        public IEnumerator ArrowClick_Toggles()
        {
            yield return SetUpFoldout();

            ClickAndAssertLanded(arrow.worldBound.center);
            yield return WaitUntil(() => !foldout.value);

            Assert.That(foldout.value, Is.False);
            Assert.That(parentChanges, Is.EqualTo(new[] { false }));

            yield return WaitFrames(); // let the press clear
            ClickAndAssertLanded(arrow.worldBound.center);
            yield return WaitUntil(() => foldout.value);

            Assert.That(foldout.value, Is.True);
        }

        [UnityTest]
        public IEnumerator LabelClick_DoesNotToggle_ButReachesHeader()
        {
            yield return SetUpFoldout();
            var label = header.Q<Label>();
            Assert.That(label, Is.Not.Null);

            ClickAndAssertLanded(label.worldBound.center);
            yield return WaitFrames(5);

            Assert.That(foldout.value, Is.True);
            Assert.That(parentChanges, Is.Empty);
            Assert.That(headerClicks, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FreeSpaceClick_DoesNotToggle_ButReachesHeader()
        {
            yield return SetUpFoldout();
            var bound = header.worldBound;
            Assume.That(bound.width, Is.GreaterThan(200f), "the header spans the window");

            ClickAndAssertLanded(new Vector2(bound.xMax - 10f, bound.center.y));
            yield return WaitFrames(5);

            Assert.That(foldout.value, Is.True);
            Assert.That(parentChanges, Is.Empty);
            Assert.That(headerClicks, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Keyboard_StillToggles()
        {
            yield return SetUpFoldout();

            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = header;
                header.SendEvent(submit);
            }
            yield return WaitUntil(() => !foldout.value);

            Assert.That(foldout.value, Is.False);
            Assert.That(parentChanges, Is.EqualTo(new[] { false }));
        }

        [UnityTest]
        public IEnumerator CodeChange_StillToggles()
        {
            yield return SetUpFoldout();

            foldout.value = false;
            yield return WaitUntil(() => parentChanges.Count > 0);

            Assert.That(foldout.value, Is.False);
            Assert.That(parentChanges, Is.EqualTo(new[] { false }));
        }

        [UnityTest]
        public IEnumerator CodeChange_AfterHeaderClick_StillToggles()
        {
            yield return SetUpFoldout();
            var bound = header.worldBound;

            ClickAndAssertLanded(new Vector2(bound.xMax - 10f, bound.center.y));
            yield return WaitFrames(5);

            foldout.value = false;
            yield return WaitUntil(() => parentChanges.Count > 0);

            Assert.That(foldout.value, Is.False);
        }

        [UnityTest]
        public IEnumerator CodeChange_InHeaderClickHandler_IsKept()
        {
            yield return SetUpFoldout();
            header.RegisterCallback<ClickEvent>(_ => foldout.value = false); // e.g. a tree collapsing the row it selects
            var bound = header.worldBound;

            ClickAndAssertLanded(new Vector2(bound.xMax - 10f, bound.center.y));
            yield return WaitUntil(() => headerClicks > 0);
            yield return WaitFrames(5);

            Assert.That(foldout.value, Is.False);
            Assert.That(header.value, Is.False, "the header shows the foldout's state");
            Assert.That(parentChanges, Is.EqualTo(new[] { false }));
        }

        [UnityTest]
        public IEnumerator RemoveManipulator_HeaderClickTogglesAgain()
        {
            yield return SetUpFoldout();
            foldout.RemoveManipulator(manipulator);
            var bound = header.worldBound;

            ClickAndAssertLanded(new Vector2(bound.xMax - 10f, bound.center.y));
            yield return WaitUntil(() => !foldout.value);

            Assert.That(foldout.value, Is.False);
        }

        [Test]
        public void NotAFoldout_LogsWarning()
        {
            var element = new VisualElement();

            element.AddManipulator(new FoldoutArrowManipulator());

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("needs a Foldout"));
        }
    }
}
