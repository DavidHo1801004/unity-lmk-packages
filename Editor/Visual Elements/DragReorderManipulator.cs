using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Drag to reorder the children of the target container. <br/>
    /// Raises <see cref="Reordered"/> and moves nothing itself, so owners can keep the order in their own model.
    /// </summary>
    /// <remarks>
    /// Children that capture the pointer themselves, e.g. buttons, don't start a drag.
    /// </remarks>
    public class DragReorderManipulator : PointerManipulator
    {
        /// <summary>
        /// Whether a child can be dragged and used as a drop slot. Null accepts every child.
        /// </summary>
        public Func<VisualElement, bool> CanReorder { get; set; }

        /// <summary>
        /// Called on a drop that changes the order, with the child index to move from and to. <br/>
        /// <c>to</c> is the child's index after the move: remove at <c>from</c>, then insert at <c>to</c>.
        /// </summary>
        public event Action<int, int> Reordered;

        // Pointer travel before a press becomes a drag, so plain clicks pass through to the children.
        private const float DRAG_THRESHOLD = 5f;

        private const int NO_POINTER = -1;

        private const string USS_CLASS = "drag-reorder";

        private const string USS_CLASS_DRAGGING = USS_CLASS + "__item--dragging";

        private const string USS_CLASS_INDICATOR = USS_CLASS + "__indicator";

        private const string USS_CLASS_INDICATOR_HORIZONTAL = USS_CLASS_INDICATOR + "--horizontal";

        private const string USS_CLASS_INDICATOR_VERTICAL = USS_CLASS_INDICATOR + "--vertical";

        private const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/DragReorderUSS.uss";

        private readonly bool isHorizontal;

        // -1 for reversed directions, so a single comparison handles both.
        private readonly float axisSign;

        private readonly List<VisualElement> slots = new();

        private readonly List<float> slotCenters = new();

        private VisualElement pressedChild;

        private Vector2 pressPosition;

        private int activePointerId = NO_POINTER;

        private bool isDragging;

        private int draggedSlot;

        private int dropSlot;

        private VisualElement indicator;



        /// <param name="_Direction"> The direction the target lays out its children. </param>
        public DragReorderManipulator(FlexDirection _Direction = FlexDirection.Row)
        {
            isHorizontal = _Direction is FlexDirection.Row or FlexDirection.RowReverse;
            axisSign = _Direction is FlexDirection.RowReverse or FlexDirection.ColumnReverse ? -1f : 1f;

            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }



        protected override void RegisterCallbacksOnTarget()
        {
            LMKTheme.AddStyleSheet(target, USS_PATH);

            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            CancelDrag();

            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }



        #region Pointer Events
        private void OnPointerDown(PointerDownEvent _Event)
        {
            if (isDragging || !CanStartManipulation(_Event)) return;

            pressedChild = FindChildAt(_Event.target as VisualElement);
            if (pressedChild == null) return;

            pressPosition = _Event.position;
            activePointerId = _Event.pointerId;
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
                if (!TryBeginDrag()) return;
            }

            UpdateDropSlot(_Event.position);
            _Event.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent _Event)
        {
            if (_Event.pointerId != activePointerId) return;

            if (isDragging)
            {
                FinishDrag();
                _Event.StopPropagation();
            }
            else
            {
                ResetPress();
            }
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent _Event)
        {
            CancelDrag();
        }
        #endregion



        #region Drag
        private bool TryBeginDrag()
        {
            CollectSlots(target, CanReorder, slots);

            draggedSlot = slots.IndexOf(pressedChild);
            if (draggedSlot < 0 || slots.Count < 2)
            {
                ResetPress();
                return false;
            }

            isDragging = true;
            target.CapturePointer(activePointerId);

            pressedChild.AddToClassList(USS_CLASS_DRAGGING);
            ShowIndicator();

            return true;
        }

        private void UpdateDropSlot(Vector2 _Position)
        {
            slotCenters.Clear();
            foreach (var slot in slots)
                slotCenters.Add(isHorizontal ? slot.worldBound.center.x : slot.worldBound.center.y);

            dropSlot = GetDropSlot(slotCenters, isHorizontal ? _Position.x : _Position.y, axisSign);
            UpdateIndicator();
        }

        private void FinishDrag()
        {
            int from = target.IndexOf(pressedChild);
            int to = GetTargetIndex(GetSlotChildIndices(), draggedSlot, dropSlot);

            // Cleaned up first, so handlers see the target without the indicator.
            CancelDrag();

            if (to >= 0)
                Reordered?.Invoke(from, to);
        }

        private void CancelDrag()
        {
            if (isDragging)
            {
                isDragging = false;

                pressedChild?.RemoveFromClassList(USS_CLASS_DRAGGING);
                indicator?.RemoveFromHierarchy();

                if (target.HasPointerCapture(activePointerId))
                    target.ReleasePointer(activePointerId);
            }

            ResetPress();
        }

        private void ResetPress()
        {
            pressedChild = null;
            activePointerId = NO_POINTER;
            slots.Clear();
        }

        // The direct child of the target containing _Element.
        private VisualElement FindChildAt(VisualElement _Element)
        {
            for (var element = _Element; element != null && element != target; element = element.hierarchy.parent)
            {
                if (element.parent == target)
                    return element;
            }
            return null;
        }

        private List<int> GetSlotChildIndices()
        {
            List<int> indices = new(slots.Count);
            foreach (var slot in slots)
                indices.Add(target.IndexOf(slot));
            return indices;
        }
        #endregion



        #region Indicator
        private void ShowIndicator()
        {
            if (indicator == null)
            {
                indicator = new() { pickingMode = PickingMode.Ignore };
                indicator.AddToClassList(USS_CLASS_INDICATOR);
                indicator.AddToClassList(isHorizontal ? USS_CLASS_INDICATOR_HORIZONTAL : USS_CLASS_INDICATOR_VERTICAL);

                // Applied here, not on the target: it waits for the attach event, and the target may already be attached.
                LMKTheme.ApplyIfStandalone(indicator);
            }

            target.hierarchy.Add(indicator);
        }

        private void UpdateIndicator()
        {
            // The edge of the slot after the gap, or the trailing edge of the last slot.
            var bound = slots[Mathf.Min(dropSlot, slots.Count - 1)].worldBound;
            bool isLeadingEdge = dropSlot < slots.Count;
            bool useMinEdge = isLeadingEdge == axisSign > 0f;

            var localEdge = target.WorldToLocal(useMinEdge ? bound.min : bound.max);
            if (isHorizontal)
                indicator.style.left = localEdge.x;
            else
                indicator.style.top = localEdge.y;
        }
        #endregion



        #region Slot Maths
        /// <summary>
        /// Fill <paramref name="_Slots"/> with the children that can be dragged and dropped between.
        /// </summary>
        internal static void CollectSlots(VisualElement _Container, Func<VisualElement, bool> _CanReorder, List<VisualElement> _Slots)
        {
            _Slots.Clear();

            foreach (var child in _Container.Children())
            {
                // Inline display is read too, since it only reaches resolvedStyle on the next style update.
                bool isHidden = child.style.display == DisplayStyle.None || child.resolvedStyle.display == DisplayStyle.None;

                if (!isHidden && (_CanReorder == null || _CanReorder(child)))
                    _Slots.Add(child);
            }
        }

        /// <summary>
        /// The gap between slots that <paramref name="_Position"/> falls in: 0 is before the first slot,
        /// <c>Count</c> is after the last one.
        /// </summary>
        /// <param name="_Centers"> Slot centers along the drag axis, in child order. </param>
        /// <param name="_Position"> Pointer position along the drag axis. </param>
        /// <param name="_AxisSign"> -1 for reversed flex directions, otherwise 1. </param>
        internal static int GetDropSlot(IReadOnlyList<float> _Centers, float _Position, float _AxisSign = 1f)
        {
            for (int i = 0; i < _Centers.Count; i++)
            {
                if (_Position * _AxisSign < _Centers[i] * _AxisSign)
                    return i;
            }
            return _Centers.Count;
        }

        /// <summary>
        /// The child index for <see cref="Reordered"/>, or -1 if the drop keeps the order.
        /// </summary>
        /// <param name="_SlotChildIndices"> Child index of each slot. </param>
        /// <param name="_DraggedSlot"> Slot being dragged. </param>
        /// <param name="_DropSlot"> Gap from <see cref="GetDropSlot"/>. </param>
        internal static int GetTargetIndex(IReadOnlyList<int> _SlotChildIndices, int _DraggedSlot, int _DropSlot)
        {
            // The gaps on either side of the dragged slot are where it already is.
            if (_DropSlot == _DraggedSlot || _DropSlot == _DraggedSlot + 1) return -1;

            // Moving forward: removing the dragged child shifts later children back by one.
            return _DropSlot > _DraggedSlot
                ? _SlotChildIndices[_DropSlot - 1]
                : _SlotChildIndices[_DropSlot];
        }
        #endregion
    }
}
