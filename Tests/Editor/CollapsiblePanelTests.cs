using System.Collections;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;

namespace LMK.Editor.Tests
{
    public class CollapsiblePanelTests : WindowTestFixture
    {
        [Test]
        public void Collapsed_RaisesCollapsedChangedOncePerChange()
        {
            var panel = new CollapsiblePanel("Inspector");
            List<bool> changes = new();
            panel.CollapsedChanged += changes.Add;

            panel.Collapsed = true;
            panel.Collapsed = true;
            panel.Collapsed = false;

            Assert.That(changes, Is.EqualTo(new[] { true, false }));
        }

        [Test]
        public void SetCollapsedWithoutNotify_ChangesStateSilently()
        {
            var panel = new CollapsiblePanel("Inspector");
            int changes = 0;
            panel.CollapsedChanged += _ => changes++;

            panel.SetCollapsedWithoutNotify(true);

            Assert.That(panel.Collapsed, Is.True);
            Assert.That(panel.ClassListContains(CollapsiblePanel.USS_CLASS_COLLAPSED), Is.True);
            Assert.That(changes, Is.Zero);
        }

        [Test]
        public void Collapse_HidesContentWithoutRemovingIt()
        {
            var panel = new CollapsiblePanel("Inspector");
            var child = new Label("Content");
            panel.Add(child);

            panel.Collapsed = true;

            Assert.That(child.hierarchy.parent, Is.SameAs(panel.contentContainer));
            Assert.That(panel.contentContainer.style.display.value, Is.EqualTo(DisplayStyle.None));

            panel.Collapsed = false;

            Assert.That(panel.contentContainer.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void Add_GoesIntoContent()
        {
            var panel = new CollapsiblePanel("Inspector");
            var child = new Label("Content");

            panel.Add(child);

            Assert.That(child.hierarchy.parent, Is.SameAs(panel.contentContainer));
            Assert.That(panel.Header.hierarchy.parent, Is.SameAs(panel));
        }

        [Test]
        public void CollapseAxis_SetsHorizontalClass()
        {
            var panel = new CollapsiblePanel("Inspector");

            Assert.That(panel.ClassListContains(CollapsiblePanel.USS_CLASS_HORIZONTAL), Is.False);

            panel.CollapseAxis = TwoPaneSplitViewOrientation.Horizontal;

            Assert.That(panel.ClassListContains(CollapsiblePanel.USS_CLASS_HORIZONTAL), Is.True);
        }

        [Test]
        public void NotCollapsible_HidesButton()
        {
            var panel = new CollapsiblePanel("Inspector") { Collapsible = false };

            Assert.That(panel.CollapseButton.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        private IEnumerator CreateInWindow(CollapsiblePanel _Panel)
        {
            _Panel.style.width = 200f;
            _Panel.style.height = 150f;
            _Panel.Add(new Label("Content"));
            Root.Add(_Panel);
            yield return WaitForLayout(_Panel.Header);
        }

        [UnityTest]
        public IEnumerator CollapseButton_Click_Toggles()
        {
            var panel = new CollapsiblePanel("Inspector");
            yield return CreateInWindow(panel);

            Click(panel.CollapseButton.worldBound.center);

            Assert.That(panel.Collapsed, Is.True);

            Click(panel.CollapseButton.worldBound.center);

            Assert.That(panel.Collapsed, Is.False);
        }

        [UnityTest]
        public IEnumerator HeaderDoubleClick_Toggles()
        {
            var panel = new CollapsiblePanel("Inspector");
            yield return CreateInWindow(panel);

            var titlePosition = new Vector2(panel.Header.worldBound.xMin + 20f, panel.Header.worldBound.center.y);
            DoubleClick(titlePosition);

            Assert.That(panel.Collapsed, Is.True);
        }

        [UnityTest]
        public IEnumerator NotCollapsible_HeaderDoubleClick_DoesNothing()
        {
            var panel = new CollapsiblePanel("Inspector") { Collapsible = false };
            yield return CreateInWindow(panel);

            int presses = 0;
            panel.Header.RegisterCallback<PointerDownEvent>(_ => presses++, TrickleDown.TrickleDown);

            var titlePosition = new Vector2(panel.Header.worldBound.xMin + 20f, panel.Header.worldBound.center.y);
            DoubleClick(titlePosition);

            Assert.That(presses, Is.EqualTo(2), "The presses didn't reach the header.");
            Assert.That(panel.Collapsed, Is.False);
        }

        [UnityTest]
        public IEnumerator HorizontalCollapse_ShrinksToStrip()
        {
            var panel = new CollapsiblePanel("Inspector") { CollapseAxis = TwoPaneSplitViewOrientation.Horizontal };
            yield return CreateInWindow(panel);

            panel.Collapsed = true;
            yield return WaitFrames();

            Assert.That(panel.layout.width, Is.LessThan(30f));
            Assert.That(panel.layout.height, Is.EqualTo(150f));
        }

        [UnityTest]
        public IEnumerator VerticalCollapse_ShrinksToHeader()
        {
            var panel = new CollapsiblePanel("Inspector");
            panel.style.position = Position.Absolute;
            yield return CreateInWindow(panel);

            // Without a fixed height, so the collapsed panel can shrink.
            panel.style.height = StyleKeyword.Null;
            panel.Collapsed = true;
            yield return WaitFrames();

            Assert.That(panel.layout.height, Is.EqualTo(panel.Header.layout.height).Within(2f));
        }
    }
}
