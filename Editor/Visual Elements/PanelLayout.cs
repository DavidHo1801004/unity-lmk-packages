using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Row (or column) of panels: one child marked with <see cref="SetFill"/> takes the remaining space,
    /// the others can be resized with a handle on their side facing the fill child,
    /// and <see cref="CollapsiblePanel"/>s can be dragged by their header to change order and collapsed. <br/>
    /// With a <see cref="PersistenceKey"/>, order, sizes and collapsed states are kept per user and per project.
    /// </summary>
    /// <remarks>
    /// Saved state identifies children by <see cref="VisualElement.name"/>, so with a key each child needs a unique name. <br/>
    /// Elements other than <see cref="CollapsiblePanel"/> can be dragged by a descendant with
    /// <see cref="dragHandleUssClassName"/>.
    /// </remarks>
    [UxmlElement("PanelLayout", libraryPath = "LMK/Layout", visibility = LibraryVisibility.Visible)]
    public partial class PanelLayout : VisualElement
    {
        /// <summary>
        /// USS class of the child that fills the remaining space. Set it with <see cref="SetFill"/> or in UXML.
        /// </summary>
        public static readonly string fillUssClassName = USS_CLASS + "__fill";

        /// <summary>
        /// USS class of an element that drags its panel, for children that aren't <see cref="CollapsiblePanel"/>s.
        /// </summary>
        public static readonly string dragHandleUssClassName = USS_CLASS + "__drag-handle";


        private TwoPaneSplitViewOrientation orientation;
        /// <summary>
        /// <see cref="TwoPaneSplitViewOrientation.Horizontal"/> lays panels out in a row,
        /// <see cref="TwoPaneSplitViewOrientation.Vertical"/> in a column. <br/>
        /// Changing it resets panel sizes, since they were set along the other axis.
        /// </summary>
        [UxmlAttribute(name = "orientation")]
        public TwoPaneSplitViewOrientation Orientation
        {
            get => orientation;
            set
            {
                bool changed = orientation != value;
                orientation = value;

                container.style.flexDirection = IsHorizontal ? FlexDirection.Row : FlexDirection.Column;
                CreateReorderManipulator();

                foreach (var handle in handles)
                    handle.Orientation = orientation;

                if (changed)
                {
                    sizes.Clear();
                    foreach (var (child, defaultSize) in defaultSizes)
                    {
                        child.style.width = defaultSize.Width;
                        child.style.height = defaultSize.Height;
                    }
                    defaultSizes.Clear();

                    foreach (var child in tracked)
                        ApplySize(child);
                }

                Refresh();
            }
        }


        private string persistenceKey = string.Empty;
        /// <summary>
        /// Key of the saved layout in <see cref="EditorUserSettings"/>, e.g. <c>"MyPackage.MyWindowLayout"</c>.
        /// Empty means the layout isn't saved.
        /// </summary>
        [UxmlAttribute(name = "persistence-key")]
        public string PersistenceKey
        {
            get => persistenceKey;
            set
            {
                persistenceKey = value ?? string.Empty;
                warnedChildren.Clear();

                savedState = LoadState(persistenceKey);
                if (tracked.Count > 0)
                    ApplySavedState(tracked);

                foreach (var child in tracked)
                    WarnIfNameNotUnique(child);
            }
        }

        /// <summary>
        /// Whether panels can be dragged by their header to change order.
        /// </summary>
        [UxmlAttribute(name = "reorderable")]
        public bool Reorderable { get; set; } = true;

        /// <summary>
        /// The panel row. <see cref="VisualElement.Add"/> and UXML children go here.
        /// </summary>
        public override VisualElement contentContainer => container;

        /// <summary>
        /// Called when the user or code changes the order, a size or a collapsed state.
        /// The layout is already saved when it's called.
        /// </summary>
        public event Action LayoutChanged;

        private bool IsHorizontal => orientation == TwoPaneSplitViewOrientation.Horizontal;

        internal const string USS_CLASS = "lmk-panel-layout";

        internal const string USS_CLASS_CONTAINER = USS_CLASS + "__container";

        internal const string USS_CLASS_PANEL = USS_CLASS + "__panel";

        internal const string USS_CLASS_HANDLE = USS_CLASS + "__handle";

        private const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/PanelLayoutUSS.uss";

        // Half the handle thickness, so the handle is centred on the edge between two panels.
        private const float HANDLE_HALF_SIZE = 3f;

        // Checks for children added or removed while no layout change reports it, e.g. without a fill child.
        private const long CHILD_CHECK_INTERVAL_MS = 250;

        private readonly VisualElement container;

        private DragReorderManipulator reorderManipulator;

        private readonly List<ResizeHandle> handles = new();

        private readonly List<VisualElement> tracked = new();

        // Children in UXML/code order, for ResetLayout.
        private readonly List<VisualElement> defaultOrder = new();

        // Collapsed states when first seen, for ResetLayout.
        private readonly Dictionary<VisualElement, bool> defaultCollapsed = new();

        // Inline sizes from before the layout first overrode them, for ResetLayout.
        private readonly Dictionary<VisualElement, DefaultSize> defaultSizes = new();

        // Sizes set by the user or by saved state. Children without one keep their own width or height.
        private readonly Dictionary<VisualElement, float> sizes = new();

        private readonly Dictionary<CollapsiblePanel, Action<bool>> collapseHandlers = new();

        private readonly HashSet<VisualElement> warnedChildren = new();

        private readonly List<VisualElement> visibleChildren = new();

        private LayoutState savedState;

        private IVisualElementScheduledItem childCheck;

        // Drag state of the active resize handle.
        private ResizeHandle activeHandle;

        private float dragStartSize;

        private float dragGrowLimit;



        public PanelLayout()
        {
            LMKTheme.ApplyIfStandalone(this);
            LMKTheme.AddStyleSheet(this, USS_PATH);

            AddToClassList(USS_CLASS);

            container = new();
            container.AddToClassList(USS_CLASS_CONTAINER);
            container.RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            hierarchy.Add(container);

            RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            Orientation = TwoPaneSplitViewOrientation.Horizontal;
        }



        /// <summary>
        /// Mark <paramref name="_Panel"/> as the child that fills the remaining space, or unmark it.
        /// The fill child never gets a fixed size and can't be dragged, but other panels can move to either side of it.
        /// </summary>
        public static void SetFill(VisualElement _Panel, bool _Fill)
        {
            if (_Panel == null) return;

            _Panel.EnableInClassList(fillUssClassName, _Fill);
            _Panel.GetFirstAncestorOfType<PanelLayout>()?.Refresh();
        }

        /// <summary>
        /// Restore the UXML/code order, default sizes and default collapsed states, then save that layout.
        /// </summary>
        public void ResetLayout()
        {
            Refresh();

            for (int i = 0; i < defaultOrder.Count; i++)
            {
                if (container.IndexOf(defaultOrder[i]) != i)
                    container.Insert(i, defaultOrder[i]);
            }

            sizes.Clear();
            foreach (var child in tracked)
            {
                if (child is CollapsiblePanel panel)
                    panel.SetCollapsedWithoutNotify(defaultCollapsed[child]);

                ApplySize(child);
            }

            Refresh();
            RaiseLayoutChanged();
        }



        #region Children
        private void OnAttachToPanel(AttachToPanelEvent _Event)
        {
            Refresh();

            childCheck ??= schedule.Execute(Refresh).Every(CHILD_CHECK_INTERVAL_MS);
            childCheck.Resume();
        }

        private void OnDetachFromPanel(DetachFromPanelEvent _Event)
        {
            childCheck?.Pause();
        }

        /// <summary>
        /// Track added and removed children, then update classes and handles.
        /// </summary>
        internal void Refresh()
        {
            SyncChildren();
            UpdateChildren();
            UpdateHandles();
        }

        private void SyncChildren()
        {
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                if (tracked[i].hierarchy.parent != container)
                    Untrack(tracked[i]);
            }

            List<VisualElement> added = null;
            foreach (var child in container.Children())
            {
                if (IsIndicator(child) || defaultCollapsed.ContainsKey(child)) continue;

                Track(child);
                (added ??= new()).Add(child);
            }

            if (added != null)
                ApplySavedState(added);
        }

        private void Track(VisualElement _Child)
        {
            tracked.Add(_Child);

            defaultCollapsed[_Child] = _Child is CollapsiblePanel { Collapsed: true };

            // Inserted after the closest earlier child that is already known, so later additions keep their place.
            int index = container.IndexOf(_Child);
            int defaultIndex = 0;
            for (int i = index - 1; i >= 0; i--)
            {
                int known = defaultOrder.IndexOf(container[i]);
                if (known >= 0)
                {
                    defaultIndex = known + 1;
                    break;
                }
            }
            defaultOrder.Insert(defaultIndex, _Child);

            _Child.RegisterCallback<GeometryChangedEvent>(OnChildGeometryChanged);

            if (_Child is CollapsiblePanel panel)
            {
                Action<bool> handler = _Collapsed => OnPanelCollapsedChanged(panel, _Collapsed);
                panel.CollapsedChanged += handler;
                collapseHandlers[panel] = handler;
            }

            WarnIfNameNotUnique(_Child);
        }

        private void Untrack(VisualElement _Child)
        {
            tracked.Remove(_Child);
            defaultOrder.Remove(_Child);
            defaultCollapsed.Remove(_Child);
            defaultSizes.Remove(_Child);
            sizes.Remove(_Child);
            warnedChildren.Remove(_Child);

            _Child.UnregisterCallback<GeometryChangedEvent>(OnChildGeometryChanged);

            if (_Child is CollapsiblePanel panel && collapseHandlers.Remove(panel, out var handler))
                panel.CollapsedChanged -= handler;
        }

        private void OnChildGeometryChanged(GeometryChangedEvent _Event)
        {
            Refresh();
        }

        private void UpdateChildren()
        {
            var fill = GetFill();
            int fillIndex = fill != null ? container.IndexOf(fill) : int.MaxValue;

            foreach (var child in tracked)
            {
                child.EnableInClassList(USS_CLASS_PANEL, child != fill);

                if (child is not CollapsiblePanel panel) continue;

                if (panel.CollapseAxis != orientation)
                    panel.CollapseAxis = orientation;

                bool towardsStart = container.IndexOf(panel) < fillIndex;
                if (panel.CollapsesTowardsStart != towardsStart)
                    panel.CollapsesTowardsStart = towardsStart;
            }
        }

        private VisualElement GetFill()
        {
            foreach (var child in container.Children())
            {
                if (child.ClassListContains(fillUssClassName))
                    return child;
            }
            return null;
        }

        private static bool IsIndicator(VisualElement _Element)
        {
            return _Element.ClassListContains(DragReorderManipulator.USS_CLASS_INDICATOR);
        }

        private static bool IsHidden(VisualElement _Element)
        {
            // Inline display is read too, since it only reaches resolvedStyle on the next style update.
            return _Element.style.display == DisplayStyle.None || _Element.resolvedStyle.display == DisplayStyle.None;
        }

        private void WarnIfNameNotUnique(VisualElement _Child)
        {
            if (string.IsNullOrEmpty(persistenceKey) || warnedChildren.Contains(_Child)) return;

            bool isUnique = !string.IsNullOrEmpty(_Child.name);
            foreach (var other in tracked)
            {
                if (other != _Child && other.name == _Child.name)
                    isUnique = false;
            }

            if (isUnique) return;

            warnedChildren.Add(_Child);
            Debug.LogWarning($"PanelLayout \"{persistenceKey}\": a {_Child.GetType().Name} child has no unique name, so its layout isn't saved. " +
                "Give every child of a PanelLayout with a persistence key a unique name.");
        }
        #endregion



        #region Sizes
        private float GetAxisSize(VisualElement _Element)
        {
            float size = IsHorizontal ? _Element.layout.width : _Element.layout.height;
            return float.IsNaN(size) ? 0f : size;
        }

        private float GetCurrentSize(VisualElement _Panel)
        {
            return sizes.TryGetValue(_Panel, out float size) ? size : GetAxisSize(_Panel);
        }

        private float GetFillMinSize(VisualElement _Fill)
        {
            float minSize = IsHorizontal ? _Fill.resolvedStyle.minWidth.value : _Fill.resolvedStyle.minHeight.value;
            return float.IsNaN(minSize) ? 0f : minSize;
        }

        /// <summary>
        /// Clamp <paramref name="_Size"/> to the panel's limits and to <paramref name="_GrowLimit"/>.
        /// The panel's <see cref="CollapsiblePanel.MinSize"/> wins over both maxima.
        /// </summary>
        internal static float ClampSize(VisualElement _Panel, float _Size, float _GrowLimit = float.PositiveInfinity)
        {
            float minSize = 0f;
            float maxSize = float.PositiveInfinity;
            if (_Panel is CollapsiblePanel panel)
            {
                minSize = panel.MinSize;
                if (panel.MaxSize > 0f)
                    maxSize = panel.MaxSize;
            }

            return Mathf.Max(Mathf.Min(_Size, maxSize, _GrowLimit), minSize);
        }

        // Collapsed panels get no inline size along the axis, so their USS decides it.
        // Others use their stored size, or the inline size they had before the layout first overrode it.
        private void ApplySize(VisualElement _Panel)
        {
            bool isCollapsed = _Panel is CollapsiblePanel { Collapsed: true };
            bool hasSize = sizes.TryGetValue(_Panel, out float size);
            bool hasDefault = defaultSizes.TryGetValue(_Panel, out var defaultSize);

            // Never overridden: leave the panel's own size alone.
            if (!isCollapsed && !hasSize && !hasDefault) return;

            if (!hasDefault)
            {
                defaultSize = new DefaultSize(_Panel.style.width, _Panel.style.height);
                defaultSizes[_Panel] = defaultSize;
            }

            StyleLength axisSize = isCollapsed ? StyleKeyword.Null
                : hasSize ? size
                : IsHorizontal ? defaultSize.Width : defaultSize.Height;

            if (IsHorizontal)
                _Panel.style.width = axisSize;
            else
                _Panel.style.height = axisSize;
        }

        private void SetSize(VisualElement _Panel, float _Size)
        {
            sizes[_Panel] = _Size;
            ApplySize(_Panel);
        }

        private void OnPanelCollapsedChanged(CollapsiblePanel _Panel, bool _Collapsed)
        {
            // Expanding takes the stored size back, as far as the fill child can give it up.
            if (!_Collapsed && sizes.TryGetValue(_Panel, out float size))
            {
                // Only once laid out: before that, there is no space to measure.
                var fill = GetFill();
                if (fill != null && fill != _Panel && !float.IsNaN(fill.layout.width) && fill.layout.width + fill.layout.height > 0f)
                {
                    float available = GetAxisSize(fill) - GetFillMinSize(fill) + GetAxisSize(_Panel);
                    sizes[_Panel] = ClampSize(_Panel, size, available);
                }
            }

            ApplySize(_Panel);
            Refresh();
            RaiseLayoutChanged();
        }
        #endregion



        #region Resize Handles
        private void UpdateHandles()
        {
            visibleChildren.Clear();
            foreach (var child in container.Children())
            {
                if (!IsIndicator(child) && !IsHidden(child))
                    visibleChildren.Add(child);
            }

            // Assignments stay fixed during a drag, so the dragged handle keeps resizing the same panel.
            if (activeHandle == null)
                AssignHandles();

            PositionHandles();
        }

        // One handle per gap, on the side of the non-fill neighbour that faces the fill child.
        private void AssignHandles()
        {
            var fill = GetFill();
            int fillPosition = visibleChildren.IndexOf(fill);
            if (fillPosition < 0)
                fillPosition = visibleChildren.Count;

            int handleCount = 0;
            for (int gap = 0; gap < visibleChildren.Count - 1; gap++)
            {
                bool beforeFill = gap < fillPosition;
                var panel = beforeFill ? visibleChildren[gap] : visibleChildren[gap + 1];

                if (panel is CollapsiblePanel { Collapsed: true }) continue;

                var handle = GetHandle(handleCount++);
                handle.userData = new HandleTarget(panel, gap, beforeFill ? 1f : -1f);
                handle.style.display = DisplayStyle.Flex;
            }

            for (int i = handleCount; i < handles.Count; i++)
            {
                handles[i].userData = null;
                handles[i].style.display = DisplayStyle.None;
            }
        }

        private void PositionHandles()
        {
            foreach (var handle in handles)
            {
                if (handle.userData is not HandleTarget target || target.Gap + 1 >= visibleChildren.Count) continue;

                var edge = this.WorldToLocal(visibleChildren[target.Gap].worldBound);
                if (IsHorizontal)
                {
                    handle.style.left = edge.xMax - HANDLE_HALF_SIZE;
                    handle.style.top = 0f;
                    handle.style.bottom = 0f;
                    handle.style.right = StyleKeyword.Null;
                }
                else
                {
                    handle.style.top = edge.yMax - HANDLE_HALF_SIZE;
                    handle.style.left = 0f;
                    handle.style.right = 0f;
                    handle.style.bottom = StyleKeyword.Null;
                }
            }
        }

        private ResizeHandle GetHandle(int _Index)
        {
            while (handles.Count <= _Index)
            {
                var handle = new ResizeHandle(orientation);
                handle.AddToClassList(USS_CLASS_HANDLE);
                handle.DragStarted += () => OnHandleDragStarted(handle);
                handle.Dragged += _Offset => OnHandleDragged(handle, _Offset);
                handle.DragFinished += () => OnHandleDragFinished(handle);

                // After the panel row, so handles are drawn and picked on top of it.
                hierarchy.Add(handle);
                handles.Add(handle);
            }
            return handles[_Index];
        }

        private void OnHandleDragStarted(ResizeHandle _Handle)
        {
            if (_Handle.userData is not HandleTarget target) return;

            activeHandle = _Handle;
            dragStartSize = GetCurrentSize(target.Panel);

            // The fill child gives up space down to its own minimum.
            var fill = GetFill();
            dragGrowLimit = fill != null
                ? dragStartSize + GetAxisSize(fill) - GetFillMinSize(fill)
                : float.PositiveInfinity;
        }

        private void OnHandleDragged(ResizeHandle _Handle, float _Offset)
        {
            if (_Handle != activeHandle || _Handle.userData is not HandleTarget target) return;

            SetSize(target.Panel, ClampSize(target.Panel, dragStartSize + target.Sign * _Offset, dragGrowLimit));
        }

        private void OnHandleDragFinished(ResizeHandle _Handle)
        {
            if (_Handle != activeHandle) return;

            activeHandle = null;

            if (_Handle.userData is HandleTarget target && !Mathf.Approximately(GetCurrentSize(target.Panel), dragStartSize))
                RaiseLayoutChanged();

            Refresh();
        }
        #endregion



        #region Reorder
        private void CreateReorderManipulator()
        {
            if (reorderManipulator != null)
                container.RemoveManipulator(reorderManipulator);

            reorderManipulator = new DragReorderManipulator(IsHorizontal ? FlexDirection.Row : FlexDirection.Column)
            {
                CanStartDrag = CanStartDrag,
            };
            reorderManipulator.Reordered += OnReordered;
            container.AddManipulator(reorderManipulator);
        }

        /// <summary>
        /// Whether a press on <paramref name="_Pressed"/> starts dragging a panel:
        /// it must be on a header or drag handle of a child that isn't the fill child, and not on a button.
        /// </summary>
        internal bool CanStartDrag(VisualElement _Pressed)
        {
            if (!Reorderable) return false;

            bool isOnHandle = false;
            for (var element = _Pressed; element != null && element != container; element = element.hierarchy.parent)
            {
                if (element.hierarchy.parent == container)
                    return isOnHandle && !element.ClassListContains(fillUssClassName) && !IsIndicator(element);

                if (element is Button || element.ClassListContains(CollapsiblePanel.USS_CLASS_HEADER_ACTIONS))
                    return false;

                if (element.ClassListContains(dragHandleUssClassName))
                    isOnHandle = true;

                // Only the header of a direct child, not of a panel nested in its content.
                if (element.hierarchy.parent is CollapsiblePanel owner && owner.Header == element && owner.hierarchy.parent == container)
                    isOnHandle = true;
            }
            return false;
        }

        private void OnReordered(int _From, int _To)
        {
            var child = container[_From];
            container.RemoveAt(_From);
            container.Insert(_To, child);

            Refresh();
            RaiseLayoutChanged();
        }
        #endregion



        #region Persistence
        private void RaiseLayoutChanged()
        {
            SaveState();
            LayoutChanged?.Invoke();
        }

        /// <summary>
        /// The current layout as JSON: children in order, with their stored size (-1 for none) and collapsed state.
        /// Children without a name are left out.
        /// </summary>
        internal string GetStateJson()
        {
            return JsonUtility.ToJson(CaptureState());
        }

        /// <summary>
        /// Apply a layout from <see cref="GetStateJson"/>. Unknown names are ignored,
        /// and children missing from it keep their position.
        /// </summary>
        internal void ApplyStateJson(string _Json)
        {
            savedState = ParseState(_Json);
            Refresh();
            ApplySavedState(tracked);
            Refresh();
        }

        private LayoutState CaptureState()
        {
            LayoutState state = new();
            foreach (var child in container.Children())
            {
                if (IsIndicator(child) || string.IsNullOrEmpty(child.name)) continue;

                state.panels.Add(new PanelState
                {
                    name = child.name,
                    size = sizes.TryGetValue(child, out float size) ? size : -1f,
                    collapsed = child is CollapsiblePanel { Collapsed: true },
                });
            }
            return state;
        }

        private void SaveState()
        {
            if (string.IsNullOrEmpty(persistenceKey)) return;

            // Kept as the saved state, so children added later are placed against the current layout.
            savedState = CaptureState();
            EditorUserSettings.SetConfigValue(persistenceKey, JsonUtility.ToJson(savedState));
        }

        private static LayoutState LoadState(string _Key)
        {
            return string.IsNullOrEmpty(_Key) ? null : ParseState(EditorUserSettings.GetConfigValue(_Key));
        }

        private static LayoutState ParseState(string _Json)
        {
            if (string.IsNullOrEmpty(_Json)) return null;

            try
            {
                return JsonUtility.FromJson<LayoutState>(_Json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private void ApplySavedState(IReadOnlyList<VisualElement> _Children)
        {
            if (savedState?.panels == null) return;

            Dictionary<string, int> savedIndices = new();
            for (int i = 0; i < savedState.panels.Count; i++)
                savedIndices.TryAdd(savedState.panels[i].name, i);

            foreach (var child in _Children)
            {
                if (string.IsNullOrEmpty(child.name) || !savedIndices.TryGetValue(child.name, out int index)) continue;

                var panelState = savedState.panels[index];
                if (panelState.size >= 0f)
                    sizes[child] = ClampSize(child, panelState.size);
                else
                    sizes.Remove(child);

                if (child is CollapsiblePanel panel)
                    panel.SetCollapsedWithoutNotify(panelState.collapsed);

                ApplySize(child);
            }

            ApplySavedOrder(savedIndices);
        }

        // Saved children take the slots that saved children hold now, in saved order. Other children keep their index.
        private void ApplySavedOrder(Dictionary<string, int> _SavedIndices)
        {
            List<VisualElement> order = new();
            List<int> slots = new();
            List<VisualElement> saved = new();

            int index = 0;
            foreach (var child in container.Children())
            {
                if (IsIndicator(child)) continue;

                order.Add(child);
                if (!string.IsNullOrEmpty(child.name) && _SavedIndices.ContainsKey(child.name))
                {
                    slots.Add(index);
                    saved.Add(child);
                }
                index++;
            }

            saved.Sort((_A, _B) => _SavedIndices[_A.name].CompareTo(_SavedIndices[_B.name]));
            for (int i = 0; i < slots.Count; i++)
                order[slots[i]] = saved[i];

            for (int i = 0; i < order.Count; i++)
            {
                if (container.IndexOf(order[i]) != i)
                    container.Insert(i, order[i]);
            }
        }
        #endregion



        private readonly struct DefaultSize
        {
            public readonly StyleLength Width;

            public readonly StyleLength Height;

            public DefaultSize(StyleLength _Width, StyleLength _Height)
            {
                Width = _Width;
                Height = _Height;
            }
        }

        private sealed class HandleTarget
        {
            public readonly VisualElement Panel;

            // Index of the gap in the visible children: the gap after visibleChildren[Gap].
            public readonly int Gap;

            // +1 when the panel is before the gap, so dragging right grows it; -1 when it is after.
            public readonly float Sign;

            public HandleTarget(VisualElement _Panel, int _Gap, float _Sign)
            {
                Panel = _Panel;
                Gap = _Gap;
                Sign = _Sign;
            }
        }

        [Serializable]
        private class LayoutState
        {
            public List<PanelState> panels = new();
        }

        [Serializable]
        private class PanelState
        {
            public string name;

            public float size = -1f;

            public bool collapsed;
        }
    }
}
