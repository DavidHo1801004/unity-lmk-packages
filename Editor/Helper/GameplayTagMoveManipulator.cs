using System;

using UnityEngine;
using UnityEngine.UIElements;

using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Drag a tag of an editable registry onto another tag of the same registry to move it there, or onto the
    /// registry's header to move it to the top level. <br/>
    /// While the drop target is valid (<see cref="TagOperations.CheckMove(RegistryGuid, RegistryGuid)"/>), a
    /// see-through copy of the tag shows where it would go. Raises <see cref="Moved"/> and moves nothing itself.
    /// </summary>
    /// <remarks>
    /// Attach it to a <see cref="GameplayTagTreeElement"/>. Drags start on a tag's row only, so a click still
    /// selects it, and the rename field keeps its own pointer. Escape cancels. The scroll view holding the tree
    /// scrolls while the pointer is near its top or bottom edge.
    /// </remarks>
    internal sealed class GameplayTagMoveManipulator : PointerManipulator
    {
        /// <summary>
        /// Called on a drop onto a valid target, with the dragged tag and the target: a tag, or a registry header
        /// for the top level. The tree is cleaned up first.
        /// </summary>
        public event Action<GameplayTagNode, GameplayTagNode> Moved;

        // Pointer travel before a press becomes a drag, so plain clicks pass through to the rows.
        private const float DRAG_THRESHOLD = 5f;

        private const int NO_POINTER = -1;

        private const string USS_CLASS_ROW = "tag-row";

        // Distance from the scroll view's edge that scrolls it, and the pixels scrolled per tick.
        private const float AUTO_SCROLL_EDGE = 20f;
        private const float AUTO_SCROLL_SPEED = 8f;
        private const long AUTO_SCROLL_INTERVAL_MS = 16;

        private GameplayTagNodeElement source;

        // The row pressed to start a drag, listened to until the drag starts or the press ends.
        private VisualElement pressedRow;

        private Vector2 pressPosition;

        private Vector2 lastPosition;

        private int activePointerId = NO_POINTER;

        private bool isDragging;

        // The hovered target, and whether a drop there would move the tag.
        private GameplayTagNodeElement hovered;
        private bool hoveredIsValid;

        private IVisualElementScheduledItem autoScroll;

        // The click that follows the release of a drag must not select the row under it.
        private bool suppressClick;



        public GameplayTagMoveManipulator()
        {
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }



        // Pointer events are handled while they trickle down, before the rows react. A branch row is a foldout
        // toggle, whose Clickable captures the pointer on press: until a drag takes the capture over, that
        // row's events reach only the row, so the pressed row is listened to as well (see OnPointerDown).
        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            target.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            target.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            CancelDrag();

            target.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            target.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
        }



        #region Pointer Events
        private void OnPointerDown(PointerDownEvent _Event)
        {
            suppressClick = false;
            if (isDragging || !CanStartManipulation(_Event)) return;

            var element = FindRowOwner(_Event.target as VisualElement);
            if (element == null || element.TagNode.IsRegistryHeader || element.TagNode.IsReadOnly) return;

            source = element;
            pressPosition = _Event.position;
            activePointerId = _Event.pointerId;

            pressedRow = element.Row;
            pressedRow.RegisterCallback<PointerMoveEvent>(OnRowPointerMove);
            pressedRow.RegisterCallback<PointerUpEvent>(OnRowPointerUp);
        }

        // Moves while the pressed row holds the capture, which only it receives. A row that doesn't capture
        // (a leaf) leaves them to the tree, so they aren't handled twice.
        private void OnRowPointerMove(PointerMoveEvent _Event)
        {
            if (pressedRow != null && pressedRow.HasPointerCapture(_Event.pointerId))
                OnPointerMove(_Event);
        }

        // A release on the captured row without a drag: a plain click, which the row handles itself.
        private void OnRowPointerUp(PointerUpEvent _Event)
        {
            if (!isDragging && _Event.pointerId == activePointerId)
                ResetPress();
        }

        private void OnPointerMove(PointerMoveEvent _Event)
        {
            if (_Event.pointerId != activePointerId) return;

            if (!isDragging)
            {
                // The release can happen outside the target, where this manipulator doesn't see it.
                if (_Event.pressedButtons == 0)
                {
                    ResetPress();
                    return;
                }

                if (Vector2.Distance(_Event.position, pressPosition) < DRAG_THRESHOLD) return;

                BeginDrag();
            }

            lastPosition = _Event.position;
            UpdateHovered();
            _Event.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent _Event)
        {
            if (_Event.pointerId != activePointerId) return;

            if (!isDragging)
            {
                ResetPress();
                return;
            }

            var moved = source.TagNode;
            var dropTarget = hoveredIsValid ? hovered.TagNode : null;

            // Cleaned up first, so the handler sees the tree as it was.
            CancelDrag();
            suppressClick = true;
            _Event.StopPropagation();

            if (dropTarget != null)
                Moved?.Invoke(moved, dropTarget);
        }

        // Only the tree losing the capture ends a drag: taking it from the row's toggle notifies that toggle too.
        private void OnPointerCaptureOut(PointerCaptureOutEvent _Event)
        {
            if (_Event.target == target)
                CancelDrag();
        }

        private void OnKeyDown(KeyDownEvent _Event)
        {
            if (!isDragging || _Event.keyCode != KeyCode.Escape) return;

            CancelDrag();
            _Event.StopImmediatePropagation();
        }

        private void OnClick(ClickEvent _Event)
        {
            if (!suppressClick) return;

            suppressClick = false;
            _Event.StopImmediatePropagation();
        }
        #endregion



        #region Drag
        private void BeginDrag()
        {
            isDragging = true;
            target.CapturePointer(activePointerId);

            // Focused, so Escape reaches it.
            target.focusable = true;
            target.Focus();

            source.SetMoveSource(true);

            autoScroll ??= target.schedule.Execute(AutoScroll).Every(AUTO_SCROLL_INTERVAL_MS);
            autoScroll.Resume();
        }

        private void UpdateHovered()
        {
            var element = target.panel == null ? null : FindRowOwner(target.panel.Pick(lastPosition));
            if (element == hovered) return;

            hovered?.HideMovePreview();
            hovered = element;
            hoveredIsValid = IsValidTarget(element);

            if (hoveredIsValid)
                hovered.ShowMovePreview(source.TagNode);
        }

        /// <summary>
        /// A tag or the header of the dragged tag's registry, where <see cref="TagOperations.CheckMove(RegistryGuid, RegistryGuid)"/> allows a move.
        /// </summary>
        private bool IsValidTarget(GameplayTagNodeElement _Element)
        {
            if (_Element == null || _Element == source) return false;

            var node = _Element.TagNode;
            var moved = source.TagNode;
            if (!node.RegistryId.Equals(moved.RegistryId)) return false;

            var newParent = node.IsRegistryHeader ? RegistryGuid.Zero : node.Id;
            return TagOperations.CheckMove(moved.Id, newParent) == null;
        }

        private void AutoScroll()
        {
            if (!isDragging) return;

            var scrollView = target.GetFirstAncestorOfType<ScrollView>();
            if (scrollView == null) return;

            var bound = scrollView.worldBound;
            float delta = 0f;
            if (lastPosition.y < bound.yMin + AUTO_SCROLL_EDGE) delta = -AUTO_SCROLL_SPEED;
            else if (lastPosition.y > bound.yMax - AUTO_SCROLL_EDGE) delta = AUTO_SCROLL_SPEED;

            if (delta == 0f) return;

            // Only within the scrollable range: nothing when the tags fit the view.
            float maxOffset = scrollView.contentContainer.layout.height - scrollView.contentViewport.layout.height;
            if (float.IsNaN(maxOffset) || maxOffset <= 0f) return;

            var offset = scrollView.scrollOffset;
            float y = Mathf.Clamp(offset.y + delta, 0f, maxOffset);
            if (Mathf.Approximately(y, offset.y)) return;

            scrollView.scrollOffset = new Vector2(offset.x, y);

            // The rows moved under a still pointer.
            UpdateHovered();
        }

        private void CancelDrag()
        {
            if (isDragging)
            {
                isDragging = false;
                autoScroll?.Pause();

                hovered?.HideMovePreview();
                hovered = null;
                hoveredIsValid = false;

                source?.SetMoveSource(false);

                if (target.HasPointerCapture(activePointerId))
                    target.ReleasePointer(activePointerId);
            }

            ResetPress();
        }

        private void ResetPress()
        {
            if (pressedRow != null)
            {
                pressedRow.UnregisterCallback<PointerMoveEvent>(OnRowPointerMove);
                pressedRow.UnregisterCallback<PointerUpEvent>(OnRowPointerUp);
                pressedRow = null;
            }

            source = null;
            activePointerId = NO_POINTER;
        }
        #endregion



        /// <summary>
        /// The tag element whose own row holds <paramref name="_Element"/>, or <see langword="null"/>: elements
        /// between rows, in a rename field, or in a move preview don't count.
        /// </summary>
        internal static GameplayTagNodeElement FindRowOwner(VisualElement _Element)
        {
            bool inRow = false;
            for (var element = _Element; element != null; element = element.hierarchy.parent)
            {
                if (element.ClassListContains(USS_CLASS_ROW))
                    inRow = true;

                if (element is GameplayTagNodeElement tagElement)
                    return inRow && !tagElement.ClassListContains(GameplayTagNodeElement.USS_CLASS_MOVE_PREVIEW) ? tagElement : null;
            }

            return null;
        }
    }
}
