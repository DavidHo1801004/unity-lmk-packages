using System;

using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Drag handle for resizing an element. <br/>
    /// With <see cref="ResizeParent"/> it resizes its parent directly. Otherwise it only raises events,
    /// for owners that keep the size in their own model.
    /// </summary>
    /// <remarks>
    /// Position the handle from the owner, e.g. absolutely on the parent's right or bottom edge.
    /// </remarks>
    [UxmlElement("ResizeHandle", libraryPath = "LMK/Layout", visibility = LibraryVisibility.Visible)]
    public partial class ResizeHandle : VisualElement
    {
        private TwoPaneSplitViewOrientation orientation;
        /// <summary>
        /// The drag axis: <see cref="TwoPaneSplitViewOrientation.Horizontal"/> resizes width,
        /// <see cref="TwoPaneSplitViewOrientation.Vertical"/> resizes height.
        /// </summary>
        [UxmlAttribute(name = "orientation")]
        public TwoPaneSplitViewOrientation Orientation
        {
            get => orientation;
            set
            {
                orientation = value;
                EnableInClassList(USS_CLASS_HORIZONTAL, IsHorizontal);
                EnableInClassList(USS_CLASS_VERTICAL, !IsHorizontal);
            }
        }

        /// <summary>
        /// If true, dragging sets the parent's width or height, clamped to <see cref="MinSize"/> and <see cref="MaxSize"/>.
        /// </summary>
        [UxmlAttribute(name = "resize-parent")]
        public bool ResizeParent { get; set; }

        /// <summary>
        /// Smallest parent size. Only applies with <see cref="ResizeParent"/>.
        /// </summary>
        [UxmlAttribute(name = "min-size")]
        public float MinSize { get; set; }

        /// <summary>
        /// Largest parent size, or 0 for no limit. Only applies with <see cref="ResizeParent"/>.
        /// </summary>
        [UxmlAttribute(name = "max-size")]
        public float MaxSize { get; set; }

        /// <summary>
        /// Called when a drag starts, so owners can store the size they will resize from.
        /// </summary>
        public event Action DragStarted;

        /// <summary>
        /// Called when the pointer moves during a drag, with the distance from the drag start. <br/>
        /// Positive is right or down. The value is an offset, not a step,
        /// so a size clamped by its owner follows the pointer again as soon as the pointer returns.
        /// </summary>
        public event Action<float> Dragged;

        /// <summary>
        /// Called when a drag ends, including when the pointer capture is lost.
        /// </summary>
        public event Action DragFinished;

        private bool IsHorizontal => orientation == TwoPaneSplitViewOrientation.Horizontal;

        private const string USS_CLASS = "resize-handle";

        private const string USS_CLASS_HORIZONTAL = USS_CLASS + "--horizontal";

        private const string USS_CLASS_VERTICAL = USS_CLASS + "--vertical";

        private const string USS_CLASS_DRAGGING = USS_CLASS + "--dragging";

        private const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/ResizeHandleUSS.uss";

        private const int NO_POINTER = -1;

        private int activePointerId = NO_POINTER;

        private float dragStartPosition;

        private float dragStartSize;



        public ResizeHandle()
            : this(TwoPaneSplitViewOrientation.Horizontal) { }

        /// <param name="_Orientation"> The drag axis. </param>
        public ResizeHandle(TwoPaneSplitViewOrientation _Orientation)
        {
            LMKTheme.ApplyIfStandalone(this);
            LMKTheme.AddStyleSheet(this, USS_PATH);

            AddToClassList(USS_CLASS);

            Orientation = _Orientation;

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }



        #region Pointer Events
        private void OnPointerDown(PointerDownEvent _Event)
        {
            if (_Event.button != 0 || activePointerId != NO_POINTER) return;

            activePointerId = _Event.pointerId;
            this.CapturePointer(activePointerId);
            BeginDrag(GetAxisValue(_Event.position), GetParentSize());

            // Stopped so a drag manipulator on an ancestor doesn't start as well.
            _Event.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent _Event)
        {
            if (_Event.pointerId != activePointerId) return;

            // Panel space: the handle moves with its parent, so local positions would jitter.
            UpdateDrag(GetAxisValue(_Event.position));
            _Event.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent _Event)
        {
            if (_Event.pointerId != activePointerId) return;

            int pointerId = activePointerId;
            EndDrag();
            this.ReleasePointer(pointerId);
            _Event.StopPropagation();
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent _Event)
        {
            EndDrag();
        }
        #endregion



        #region Drag
        internal void BeginDrag(float _Position, float _StartSize)
        {
            dragStartPosition = _Position;
            dragStartSize = _StartSize;

            AddToClassList(USS_CLASS_DRAGGING);
            DragStarted?.Invoke();
        }

        internal void UpdateDrag(float _Position)
        {
            float offset = _Position - dragStartPosition;

            if (ResizeParent && parent != null)
                SetParentSize(ClampSize(dragStartSize + offset));

            Dragged?.Invoke(offset);
        }

        internal void EndDrag()
        {
            if (!ClassListContains(USS_CLASS_DRAGGING)) return;

            activePointerId = NO_POINTER;
            RemoveFromClassList(USS_CLASS_DRAGGING);
            DragFinished?.Invoke();
        }
        #endregion



        private float ClampSize(float _Size)
        {
            float size = Mathf.Max(_Size, MinSize);
            return MaxSize > 0f ? Mathf.Min(size, MaxSize) : size;
        }

        private float GetAxisValue(Vector2 _Position) => IsHorizontal ? _Position.x : _Position.y;

        private float GetParentSize()
        {
            if (parent == null) return 0f;

            float size = IsHorizontal ? parent.resolvedStyle.width : parent.resolvedStyle.height;
            return float.IsNaN(size) ? 0f : size;
        }

        private void SetParentSize(float _Size)
        {
            if (IsHorizontal)
                parent.style.width = _Size;
            else
                parent.style.height = _Size;
        }
    }
}
