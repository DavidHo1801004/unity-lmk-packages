using System.Collections;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;

namespace LMK.Editor.Tests
{
    /// <summary>
    /// Sends real pointer events through a window's panel, for the capture and dispatch paths.
    /// </summary>
    /// <remarks>
    /// The panel picks the event target from the position, like real input, so positions must come from
    /// <see cref="VisualElement.worldBound"/>: the window's tab strip offsets its content.
    /// </remarks>
    public class PointerFlowTests
    {
        private const float CHILD_SIZE = 50f;

        private const string USS_CLASS_DRAGGING = "drag-reorder__item--dragging";

        private PointerTestWindow window;

        [SetUp]
        public void SetUp()
        {
            window = ScriptableObject.CreateInstance<PointerTestWindow>();
            window.position = new Rect(100, 100, 400, 300);
            window.Show();
        }

        [TearDown]
        public void TearDown()
        {
            if (window)
                window.Close();
        }

        private void Press(Vector2 _Position)
        {
            using var pointerEvent = PointerDownEvent.GetPooled(CreateEvent(EventType.MouseDown, _Position));
            window.rootVisualElement.SendEvent(pointerEvent);
        }

        private void Drag(Vector2 _Position)
        {
            using var pointerEvent = PointerMoveEvent.GetPooled(CreateEvent(EventType.MouseDrag, _Position));
            window.rootVisualElement.SendEvent(pointerEvent);
        }

        private void Hover(Vector2 _Position)
        {
            using var pointerEvent = PointerMoveEvent.GetPooled(CreateEvent(EventType.MouseMove, _Position));
            window.rootVisualElement.SendEvent(pointerEvent);
        }

        private void Release(Vector2 _Position)
        {
            using var pointerEvent = PointerUpEvent.GetPooled(CreateEvent(EventType.MouseUp, _Position));
            window.rootVisualElement.SendEvent(pointerEvent);
        }

        private static Event CreateEvent(EventType _Type, Vector2 _Position)
        {
            return new Event
            {
                type = _Type,
                mousePosition = _Position,
                button = 0,
                clickCount = 1,
            };
        }

        private static VisualElement CreateBox(float _Width, float _Height)
        {
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = 0f;
            box.style.top = 0f;
            box.style.width = _Width;
            box.style.height = _Height;
            return box;
        }

        private VisualElement CreateRow(out List<(int from, int to)> _Moves)
        {
            var row = CreateBox(CHILD_SIZE * 3f, CHILD_SIZE);
            row.style.flexDirection = FlexDirection.Row;

            for (int i = 0; i < 3; i++)
            {
                var child = new VisualElement();
                child.style.width = CHILD_SIZE;
                child.style.height = CHILD_SIZE;
                child.style.flexDirection = FlexDirection.Row;
                row.Add(child);
            }

            List<(int, int)> moves = new();
            var manipulator = new DragReorderManipulator(FlexDirection.Row);
            manipulator.Reordered += (_From, _To) => moves.Add((_From, _To));
            row.AddManipulator(manipulator);

            window.rootVisualElement.Add(row);
            _Moves = moves;
            return row;
        }

        // Layout and style updates need a panel update, which a batch mode window only gets on repaint.
        private IEnumerator WaitForLayout(VisualElement _Element)
        {
            for (int frame = 0; frame < 30; frame++)
            {
                window.Repaint();
                yield return null;

                if (_Element.worldBound.width > 0f)
                    yield break;
            }

            Assert.Inconclusive("The window never laid out.");
        }

        #region ResizeHandle
        [UnityTest]
        public IEnumerator ResizeHandle_DragResizesParentFromItsLaidOutSize()
        {
            var parent = CreateBox(200f, 100f);
            parent.style.flexDirection = FlexDirection.Row;
            parent.style.justifyContent = Justify.FlexEnd;
            var handle = new ResizeHandle { ResizeParent = true };
            parent.Add(handle);
            window.rootVisualElement.Add(parent);
            yield return WaitForLayout(handle);

            var start = handle.worldBound.center;
            Press(start);

            Assert.That(handle.HasPointerCapture(PointerId.mousePointerId), Is.True);

            Drag(start + new Vector2(50f, 0f));
            Release(start + new Vector2(50f, 0f));

            Assert.That(parent.style.width.value.value, Is.EqualTo(250f));
            Assert.That(handle.HasPointerCapture(PointerId.mousePointerId), Is.False);
        }

        [UnityTest]
        public IEnumerator ResizeHandle_PressDoesNotStartAncestorDrag()
        {
            var row = CreateRow(out var moves);
            var handle = new ResizeHandle();
            int dragged = 0;
            handle.Dragged += _ => dragged++;
            row[0].Add(handle);
            yield return WaitForLayout(handle);

            var start = handle.worldBound.center;
            var end = new Vector2(row[2].worldBound.xMax, start.y);
            Press(start);
            Drag(end);
            Release(end);

            Assert.That(dragged, Is.GreaterThan(0), "The press didn't reach the handle.");
            Assert.That(moves, Is.Empty);
        }
        #endregion

        #region DragReorderManipulator
        [UnityTest]
        public IEnumerator DragReorder_DragForward_RaisesReordered()
        {
            var row = CreateRow(out var moves);
            yield return WaitForLayout(row);

            var end = new Vector2(row[2].worldBound.xMax - 1f, row[2].worldBound.center.y);
            Press(row[0].worldBound.center);
            Drag(end);
            Release(end);

            Assert.That(moves, Is.EqualTo(new[] { (0, 2) }));
        }

        [UnityTest]
        public IEnumerator DragReorder_DragBackward_RaisesReordered()
        {
            var row = CreateRow(out var moves);
            yield return WaitForLayout(row);

            var end = new Vector2(row[0].worldBound.xMin + 1f, row[0].worldBound.center.y);
            Press(row[2].worldBound.center);
            Drag(end);
            Release(end);

            Assert.That(moves, Is.EqualTo(new[] { (2, 0) }));
        }

        [UnityTest]
        public IEnumerator DragReorder_RowMovedByHandler_LeavesNoDragState()
        {
            var row = CreateRow(out var moves);
            yield return WaitForLayout(row);

            var dragged = row[0];
            var end = new Vector2(row[2].worldBound.xMax - 1f, row[2].worldBound.center.y);
            Press(row[0].worldBound.center);
            Drag(end);

            Assert.That(dragged.ClassListContains(USS_CLASS_DRAGGING), Is.True);
            Assert.That(row.HasPointerCapture(PointerId.mousePointerId), Is.True);

            Release(end);

            Assert.That(dragged.ClassListContains(USS_CLASS_DRAGGING), Is.False);
            Assert.That(row.HasPointerCapture(PointerId.mousePointerId), Is.False);
        }

        [UnityTest]
        public IEnumerator DragReorder_DuringDrag_ShowsIndicatorThenRemovesIt()
        {
            var row = CreateRow(out _);
            yield return WaitForLayout(row);
            int childCount = row.hierarchy.childCount;

            var end = new Vector2(row[2].worldBound.xMax - 1f, row[2].worldBound.center.y);
            Press(row[0].worldBound.center);
            Drag(end);

            Assert.That(row.hierarchy.childCount, Is.EqualTo(childCount + 1));

            Release(end);

            Assert.That(row.hierarchy.childCount, Is.EqualTo(childCount));
        }

        [UnityTest]
        public IEnumerator DragReorder_MoveInsideThreshold_IsAClick()
        {
            var row = CreateRow(out var moves);
            yield return WaitForLayout(row);

            var start = row[0].worldBound.center;
            Press(start);
            Drag(start + new Vector2(2f, 0f));

            Assert.That(row.HasPointerCapture(PointerId.mousePointerId), Is.False);

            // Past the threshold, so this press would have dragged: proves it reached the manipulator.
            Drag(start + new Vector2(10f, 0f));

            Assert.That(row.HasPointerCapture(PointerId.mousePointerId), Is.True);

            Release(start + new Vector2(10f, 0f));

            Assert.That(moves, Is.Empty);
        }

        [UnityTest]
        public IEnumerator DragReorder_DropOnStart_RaisesNothing()
        {
            var row = CreateRow(out var moves);
            yield return WaitForLayout(row);

            var start = row[1].worldBound.center;
            var end = start + new Vector2(20f, 0f); // still before the next child's center
            Press(start);
            Drag(end);

            Assert.That(row[1].ClassListContains(USS_CLASS_DRAGGING), Is.True, "The drag didn't start.");

            Release(end);

            Assert.That(moves, Is.Empty);
        }

        [UnityTest]
        public IEnumerator DragReorder_MoveWithoutButton_DoesNotStartDrag()
        {
            var row = CreateRow(out var moves);
            yield return WaitForLayout(row);

            // Released outside the row, so the row never sees it.
            var outside = new Vector2(row.worldBound.xMax + 50f, row.worldBound.yMax + 50f);
            var hoverEnd = new Vector2(row[2].worldBound.xMax - 1f, row[2].worldBound.center.y);
            Press(row[0].worldBound.center);
            Release(outside);
            Hover(hoverEnd);

            Assert.That(row.HasPointerCapture(PointerId.mousePointerId), Is.False);
            Assert.That(moves, Is.Empty);
        }
        #endregion
    }
}
