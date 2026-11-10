using System.Collections;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using LMK.Editor.Windows;

namespace LMK.Editor.Tests
{
    public class AnchoredPopupTests : WindowTestFixture
    {
        private Button anchor;

        private AnchoredPopup openPopup;

        [TearDown]
        public void ClosePopup()
        {
            openPopup?.Close();
        }

        // Moves each test's anchor: Unity doesn't reopen a popup from the rect where one just closed.
        private static int anchorCount;

        private IEnumerator CreateAnchor()
        {
            anchor = new Button { text = "Add" };
            anchor.style.position = Position.Absolute;
            anchor.style.left = 40f;
            anchor.style.top = 30f + 25f * (anchorCount++ % 8);
            anchor.style.width = 80f;
            Root.Add(anchor);
            yield return WaitForLayout(anchor);
        }

        private static VisualElement CreateContent(out TextField _Field)
        {
            var content = new VisualElement();
            content.Add(new Label("Name"));

            var disabled = new Button { text = "Disabled" };
            disabled.SetEnabled(false);
            content.Add(disabled);

            _Field = new TextField();
            content.Add(_Field);
            content.Add(new Button { text = "After" });
            return content;
        }

        /// <summary>
        /// The popup's root hosted in the test window, without a popup window.
        /// </summary>
        private IEnumerator CreateHostedPopup(VisualElement _Content)
        {
            yield return CreateAnchor();

            openPopup = new AnchoredPopup(anchor, _Content, 0f);
            var root = openPopup.CreateGUI();
            root.style.top = 100f;
            Root.Add(root);
            yield return WaitForLayout(_Content);
        }

        [UnityTest]
        public IEnumerator Escape_ClosesAndRaisesClosedOnce()
        {
            var content = CreateContent(out var field);
            yield return CreateHostedPopup(content);
            int closed = 0;
            openPopup.Closed += () => closed++;

            KeyDown(field, KeyCode.Escape);
            KeyDown(field, KeyCode.Escape);

            Assert.That(openPopup.IsClosed, Is.True);
            Assert.That(closed, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OtherKeys_DoNotClose()
        {
            var content = CreateContent(out var field);
            yield return CreateHostedPopup(content);

            KeyDown(field, KeyCode.A, 'a');

            Assert.That(openPopup.IsClosed, Is.False);
        }

        [Test]
        public void CloseTwice_RaisesClosedOnce()
        {
            var popup = new AnchoredPopup(new Button(), new Label("Content"), 0f);
            int closed = 0;
            popup.Closed += () => closed++;

            popup.Close();
            popup.Close();

            Assert.That(closed, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FirstFocusableChild_GetsFocusAfterOpening()
        {
            var content = CreateContent(out var field);
            yield return CreateHostedPopup(content);
            yield return WaitUntil(() => IsWithin(FocusedElement, field));

            Assert.That(IsWithin(FocusedElement, field), Is.True, $"Focused: {FocusedElement}");
        }

        [UnityTest]
        public IEnumerator Close_ReturnsFocusToAnchor()
        {
            var content = CreateContent(out var field);
            yield return CreateHostedPopup(content);
            yield return WaitUntil(() => IsWithin(FocusedElement, field));

            openPopup.Close();

            Assert.That(FocusedElement, Is.SameAs(anchor));
        }

        [Test]
        public void FindFirstFocusable_SkipsDisabledAndUnfocusable()
        {
            var content = CreateContent(out var field);

            Assert.That(AnchoredPopup.FindFirstFocusable(content), Is.SameAs(field));
        }

        private IEnumerator ShowWithContentSize(float _Width, float _Height, float _PopupWidth = 0f)
        {
            yield return CreateAnchor();

            var content = new VisualElement();
            content.style.width = _Width;
            content.style.height = _Height;

            openPopup = AnchoredPopup.Show(anchor, content, default, _PopupWidth);
            if (openPopup == null)
                Assert.Inconclusive("Unity didn't open a popup window in this Editor session.");

            yield return WaitForPopup(() => openPopup.editorWindow.position.width > 10f);
        }

        // Repaints the popup window too: its layout, and so its size, only updates when it repaints.
        private IEnumerator WaitForPopup(System.Func<bool> _Condition, int _MaxFrames = 120)
        {
            for (int frame = 0; frame < _MaxFrames && !_Condition(); frame++)
            {
                window.Repaint();
                if (openPopup?.editorWindow)
                    openPopup.editorWindow.Repaint();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Window_SizesToContent()
        {
            yield return ShowWithContentSize(220f, 60f);

            // Content plus the popup's padding and borders.
            var size = openPopup.editorWindow.position.size;
            Assert.That(size.x, Is.GreaterThanOrEqualTo(220f).And.LessThan(240f));
            Assert.That(size.y, Is.GreaterThanOrEqualTo(60f).And.LessThan(80f));
        }

        [UnityTest]
        public IEnumerator Window_IsAtLeastAnchorWidth()
        {
            yield return ShowWithContentSize(10f, 10f);

            Assert.That(openPopup.editorWindow.position.width, Is.GreaterThanOrEqualTo(anchor.worldBound.width));
        }

        [UnityTest]
        public IEnumerator Window_UsesGivenWidth()
        {
            yield return ShowWithContentSize(10f, 10f, 300f);

            Assert.That(openPopup.editorWindow.position.width, Is.EqualTo(300f).Within(4f));
        }

        [UnityTest]
        public IEnumerator Window_FollowsContentResize()
        {
            yield return ShowWithContentSize(220f, 60f);

            openPopup.Content.style.height = 120f;
            yield return WaitForPopup(() => openPopup.editorWindow.position.height > 120f);

            Assert.That(openPopup.editorWindow.position.height, Is.GreaterThanOrEqualTo(120f).And.LessThan(140f));
        }

        [UnityTest]
        public IEnumerator Show_OpensUnderAnchor_AndEscapeClosesTheWindow()
        {
            yield return CreateAnchor();
            var content = CreateContent(out var field);

            openPopup = AnchoredPopup.Show(anchor, content, new Vector2(5f, 2f));
            if (openPopup == null)
                Assert.Inconclusive("Unity didn't open a popup window in this Editor session.");

            // Unity sizes the window before it moves it under the anchor, so wait for both.
            yield return WaitForPopup(() => content.worldBound.width > 0f && openPopup.editorWindow.position.width > 10f
                && openPopup.editorWindow.position.position != Vector2.zero);

            // Screen position of the anchor's bottom-left corner, from the host window.
            var anchorBound = anchor.worldBound;
            var rootOrigin = window.rootVisualElement.worldBound.position;
            var expected = window.position.position + new Vector2(anchorBound.xMin, anchorBound.yMax) - rootOrigin + new Vector2(5f, 2f);
            var popupPosition = openPopup.editorWindow.position.position;

            Debug.Log($"AnchoredPopup: expected {expected}, popup at {popupPosition}, size {openPopup.editorWindow.position.size}, " +
                $"host {window.position}, root origin {rootOrigin}");

            Assert.That(popupPosition.x, Is.EqualTo(expected.x).Within(2f));
            Assert.That(popupPosition.y, Is.EqualTo(expected.y).Within(2f));

            int closed = 0;
            openPopup.Closed += () => closed++;
            // Sent to the content's field: it trickles down through the popup root like a real key press.
            KeyDown(field, KeyCode.Escape);
            yield return null;

            Assert.That(closed, Is.EqualTo(1));
            Assert.That(openPopup.editorWindow == null, Is.True, "The popup window is still open.");
        }
    }
}
