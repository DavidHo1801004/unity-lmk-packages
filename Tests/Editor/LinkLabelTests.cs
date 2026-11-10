using System.Collections;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;

namespace LMK.Editor.Tests
{
    public class LinkLabelTests : WindowTestFixture
    {
        private LinkLabel link;

        private int clicks;

        private int presses;

        private IEnumerator CreateLink()
        {
            link = new LinkLabel("Main registry");
            clicks = 0;
            link.clicked += () => clicks++;

            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.RegisterCallback<PointerDownEvent>(_ => presses++, TrickleDown.TrickleDown);
            container.Add(link);
            Root.Add(container);

            presses = 0;
            yield return WaitForLayout(link);
        }

        [UnityTest]
        public IEnumerator Click_RaisesClicked()
        {
            yield return CreateLink();

            Click(link.worldBound.center);

            Assert.That(clicks, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator EnterAndSpace_RaiseClicked()
        {
            yield return CreateLink();

            KeyDown(link, KeyCode.Return);
            KeyDown(link, KeyCode.KeypadEnter);
            KeyDown(link, KeyCode.Space);

            Assert.That(clicks, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator Disabled_RaisesNothing()
        {
            yield return CreateLink();
            link.SetEnabled(false);
            yield return WaitFrames();

            Click(link.worldBound.center);
            KeyDown(link, KeyCode.Return);

            Assert.That(presses, Is.EqualTo(1), "The press didn't reach the link's position.");
            Assert.That(clicks, Is.Zero);
        }

        [Test]
        public void IsFocusable()
        {
            Assert.That(new LinkLabel("Link").focusable, Is.True);
        }
    }
}
