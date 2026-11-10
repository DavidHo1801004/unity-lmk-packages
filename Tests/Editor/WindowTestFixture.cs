using System.Collections;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.UIElements;

namespace LMK.Editor.Tests
{
    /// <summary>
    /// Base for tests that need layout, focus or real input: shows a <see cref="PointerTestWindow"/>
    /// and sends events through its panel.
    /// </summary>
    /// <remarks>
    /// Pointer positions must come from <see cref="VisualElement.worldBound"/>: the window's tab strip offsets its content.
    /// See <see cref="PointerFlowTests"/>.
    /// </remarks>
    public abstract class WindowTestFixture
    {
        protected PointerTestWindow window;

        protected VisualElement Root => window.rootVisualElement;

        [SetUp]
        public void SetUpWindow()
        {
            window = ScriptableObject.CreateInstance<PointerTestWindow>();
            window.position = new Rect(100, 100, 500, 400);
            window.Show();
            window.Focus();
        }

        [TearDown]
        public void TearDownWindow()
        {
            if (window)
                window.Close();
        }

        #region Pointer
        protected void Press(Vector2 _Position, int _ClickCount = 1)
        {
            using var pointerEvent = PointerDownEvent.GetPooled(CreateMouseEvent(EventType.MouseDown, _Position, _ClickCount));
            Root.SendEvent(pointerEvent);
        }

        protected void Drag(Vector2 _Position)
        {
            using var pointerEvent = PointerMoveEvent.GetPooled(CreateMouseEvent(EventType.MouseDrag, _Position));
            Root.SendEvent(pointerEvent);
        }

        /// <summary>
        /// Move the pointer with no button pressed, e.g. to set hover states.
        /// </summary>
        protected void Hover(Vector2 _Position)
        {
            using var pointerEvent = PointerMoveEvent.GetPooled(CreateMouseEvent(EventType.MouseMove, _Position));
            Root.SendEvent(pointerEvent);
        }

        protected void Release(Vector2 _Position, int _ClickCount = 1)
        {
            using var pointerEvent = PointerUpEvent.GetPooled(CreateMouseEvent(EventType.MouseUp, _Position, _ClickCount));
            Root.SendEvent(pointerEvent);
        }

        protected void Click(Vector2 _Position, int _ClickCount = 1)
        {
            Press(_Position, _ClickCount);
            Release(_Position, _ClickCount);
        }

        protected void DoubleClick(Vector2 _Position)
        {
            Click(_Position, 1);
            Click(_Position, 2);
        }

        /// <summary>
        /// Press at <paramref name="_From"/>, move in steps to <paramref name="_To"/>, release there.
        /// </summary>
        protected void DragFromTo(Vector2 _From, Vector2 _To)
        {
            Press(_From);
            for (int step = 1; step <= 4; step++)
                Drag(Vector2.Lerp(_From, _To, step / 4f));
            Release(_To);
        }

        private static Event CreateMouseEvent(EventType _Type, Vector2 _Position, int _ClickCount = 1)
        {
            return new Event
            {
                type = _Type,
                mousePosition = _Position,
                button = 0,
                clickCount = _ClickCount,
            };
        }
        #endregion

        #region Keyboard
        /// <summary>
        /// Send a key down to <paramref name="_Target"/>, e.g. the focused element.
        /// </summary>
        protected static void KeyDown(VisualElement _Target, KeyCode _KeyCode, char _Character = '\0')
        {
            using var keyEvent = KeyDownEvent.GetPooled(_Character, _KeyCode, EventModifiers.None);
            keyEvent.target = _Target;
            _Target.SendEvent(keyEvent);
        }

        /// <summary>
        /// Type a character the way the Editor sends it: a key down with the key code, then one with the character.
        /// </summary>
        protected static void Type(VisualElement _Target, char _Character)
        {
            KeyDown(_Target, KeyCode.None, _Character);
        }

        protected VisualElement FocusedElement => Root.panel?.focusController?.focusedElement as VisualElement;

        protected static bool IsWithin(VisualElement _Element, VisualElement _Ancestor)
        {
            for (var element = _Element; element != null; element = element.hierarchy.parent)
            {
                if (element == _Ancestor)
                    return true;
            }
            return false;
        }
        #endregion

        #region Waiting
        /// <summary>
        /// Layout and style updates need a panel update, which a batch mode window only gets on repaint.
        /// </summary>
        protected IEnumerator WaitForLayout(VisualElement _Element)
        {
            for (int frame = 0; frame < 30; frame++)
            {
                window.Repaint();
                yield return null;

                if (_Element.worldBound.width > 0f && !float.IsNaN(_Element.worldBound.width))
                    yield break;
            }

            Assert.Inconclusive("The window never laid out.");
        }

        /// <summary>
        /// Repaint until <paramref name="_Condition"/> holds, for scheduled items: in batch mode the scheduler
        /// only ticks on repaints, and not on every one, so a fixed frame count is flaky.
        /// </summary>
        protected IEnumerator WaitUntil(System.Func<bool> _Condition, int _MaxFrames = 120)
        {
            for (int frame = 0; frame < _MaxFrames && !_Condition(); frame++)
            {
                window.Repaint();
                yield return null;
            }
        }

        /// <summary>
        /// Let a few panel updates run, e.g. for layout after a change.
        /// </summary>
        protected IEnumerator WaitFrames(int _Frames = 3)
        {
            for (int frame = 0; frame < _Frames; frame++)
            {
                window.Repaint();
                yield return null;
            }
        }
        #endregion

        protected static VisualElement CreateBox(float _Width, float _Height)
        {
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = 0f;
            box.style.top = 0f;
            box.style.width = _Width;
            box.style.height = _Height;
            return box;
        }
    }
}
