using System;

using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Titled panel with a header and a content area, which can be collapsed. <br/>
    /// With a horizontal <see cref="CollapseAxis"/> it collapses sideways to a thin strip showing its title rotated;
    /// with a vertical one it collapses to its header.
    /// </summary>
    /// <remarks>
    /// The collapsed content is hidden, not removed, so its state survives. <br/>
    /// The panel doesn't keep its expanded size: an owner such as <see cref="PanelLayout"/> does.
    /// </remarks>
    [UxmlElement("CollapsiblePanel", libraryPath = "LMK/Layout", visibility = LibraryVisibility.Visible)]
    public partial class CollapsiblePanel : VisualElement
    {
        /// <summary>
        /// Text shown in the header.
        /// </summary>
        [UxmlAttribute(name = "title")]
        public string Title
        {
            get => titleLabel.text;
            set
            {
                ((INotifyValueChanged<string>)titleLabel).SetValueWithoutNotify(value);
                titleLabel.tooltip = value;
            }
        }


        private bool collapsed;
        /// <summary>
        /// Whether only the header (or the collapsed strip) is shown. Raises <see cref="CollapsedChanged"/> when changed.
        /// </summary>
        [UxmlAttribute(name = "collapsed")]
        public bool Collapsed
        {
            get => collapsed;
            set
            {
                if (collapsed == value) return;

                SetCollapsedWithoutNotify(value);
                CollapsedChanged?.Invoke(collapsed);
            }
        }


        private bool collapsible = true;
        /// <summary>
        /// Whether the user can collapse the panel, with its button or by double-clicking its header. <br/>
        /// Doesn't change <see cref="Collapsed"/>, which code can still set.
        /// </summary>
        [UxmlAttribute(name = "collapsible")]
        public bool Collapsible
        {
            get => collapsible;
            set
            {
                collapsible = value;
                collapseButton.style.display = collapsible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>
        /// Smallest size along the owner's layout axis. Used by <see cref="PanelLayout"/>.
        /// </summary>
        [UxmlAttribute(name = "min-size")]
        public float MinSize { get; set; }

        /// <summary>
        /// Largest size along the owner's layout axis, or 0 for no limit. Used by <see cref="PanelLayout"/>.
        /// </summary>
        [UxmlAttribute(name = "max-size")]
        public float MaxSize { get; set; }


        private TwoPaneSplitViewOrientation collapseAxis = TwoPaneSplitViewOrientation.Vertical;
        /// <summary>
        /// <see cref="TwoPaneSplitViewOrientation.Horizontal"/> collapses sideways to a strip, for a panel in a row.
        /// <see cref="TwoPaneSplitViewOrientation.Vertical"/> collapses to the header, for a panel in a column. <br/>
        /// <see cref="PanelLayout"/> sets it to its own orientation.
        /// </summary>
        [UxmlAttribute(name = "orientation")]
        public TwoPaneSplitViewOrientation CollapseAxis
        {
            get => collapseAxis;
            set
            {
                collapseAxis = value;
                EnableInClassList(USS_CLASS_HORIZONTAL, IsHorizontal);
                UpdateChevron();
            }
        }

        /// <summary>
        /// The header: title, <see cref="HeaderActions"/> and collapse button. <see cref="PanelLayout"/> drags panels by it.
        /// </summary>
        public VisualElement Header { get; }

        /// <summary>
        /// Slot for extra header controls, right of the title. Empty by default, hidden while collapsed sideways.
        /// </summary>
        public VisualElement HeaderActions { get; }

        /// <summary>
        /// The content area. <see cref="VisualElement.Add"/> and UXML children go here.
        /// </summary>
        public override VisualElement contentContainer => content;

        /// <summary>
        /// Called with the new state when <see cref="Collapsed"/> changes, by code or by the user.
        /// </summary>
        public event Action<bool> CollapsedChanged;

        /// <summary>
        /// Side the panel collapses towards along a horizontal axis: false is the end (right), true the start (left).
        /// Set by <see cref="PanelLayout"/> from the panel's side of its fill child.
        /// </summary>
        internal bool CollapsesTowardsStart
        {
            get => collapsesTowardsStart;
            set
            {
                collapsesTowardsStart = value;
                UpdateChevron();
            }
        }

        internal ToolbarButton CollapseButton => collapseButton;

        private bool IsHorizontal => collapseAxis == TwoPaneSplitViewOrientation.Horizontal;

        internal const string USS_CLASS = "lmk-collapsible-panel";

        internal const string USS_CLASS_HEADER = USS_CLASS + "__header";

        internal const string USS_CLASS_TITLE = USS_CLASS + "__title";

        internal const string USS_CLASS_HEADER_ACTIONS = USS_CLASS + "__header-actions";

        internal const string USS_CLASS_COLLAPSE_BUTTON = USS_CLASS + "__collapse-button";

        internal const string USS_CLASS_CHEVRON = USS_CLASS + "__chevron";

        internal const string USS_CLASS_CONTENT = USS_CLASS + "__content";

        internal const string USS_CLASS_COLLAPSED = USS_CLASS + "--collapsed";

        internal const string USS_CLASS_HORIZONTAL = USS_CLASS + "--horizontal";

        private const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/CollapsiblePanelUSS.uss";

        private const string CHEVRON_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/Sprites/down-arrow-symbol-white.png";

        private readonly Label titleLabel;

        private readonly ToolbarButton collapseButton;

        private readonly Image chevron;

        private readonly VisualElement content;

        private bool collapsesTowardsStart;



        public CollapsiblePanel()
            : this(string.Empty) { }

        /// <param name="_Title"> Text shown in the header. </param>
        public CollapsiblePanel(string _Title)
        {
            LMKTheme.ApplyIfStandalone(this);
            LMKTheme.AddStyleSheet(this, USS_PATH);

            AddToClassList(USS_CLASS);

            Header = new();
            Header.AddToClassList(USS_CLASS_HEADER);
            Header.RegisterCallback<PointerDownEvent>(OnHeaderPointerDown);
            hierarchy.Add(Header);

            titleLabel = new();
            titleLabel.AddToClassList(USS_CLASS_TITLE);
            Header.Add(titleLabel);

            HeaderActions = new();
            HeaderActions.AddToClassList(USS_CLASS_HEADER_ACTIONS);
            Header.Add(HeaderActions);

            collapseButton = new(false, () => Collapsed = !Collapsed);
            collapseButton.AddToClassList(USS_CLASS_COLLAPSE_BUTTON);
            Header.Add(collapseButton);

            chevron = new()
            {
                image = LMKAssetUtility.Load<Texture2D>(CHEVRON_PATH),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore,
            };
            chevron.AddToClassList(USS_CLASS_CHEVRON);
            collapseButton.Add(chevron);

            content = new();
            content.AddToClassList(USS_CLASS_CONTENT);
            hierarchy.Add(content);

            Title = _Title;
            CollapseAxis = TwoPaneSplitViewOrientation.Vertical;
        }



        /// <summary>
        /// Set <see cref="Collapsed"/> without raising <see cref="CollapsedChanged"/>.
        /// </summary>
        public void SetCollapsedWithoutNotify(bool _Collapsed)
        {
            collapsed = _Collapsed;

            EnableInClassList(USS_CLASS_COLLAPSED, collapsed);
            content.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            UpdateChevron();
        }

        private void OnHeaderPointerDown(PointerDownEvent _Event)
        {
            if (_Event.button != 0 || _Event.clickCount != 2 || !collapsible) return;

            // Double-clicks on header controls belong to those controls.
            for (var element = _Event.target as VisualElement; element != null && element != Header; element = element.hierarchy.parent)
            {
                if (element == HeaderActions || element == collapseButton) return;
            }

            Collapsed = !Collapsed;
            _Event.StopPropagation();
        }

        // The chevron points the way the next click moves the panel: towards the collapse side while expanded.
        private void UpdateChevron()
        {
            if (chevron == null) return;

            float angle;
            if (IsHorizontal)
            {
                bool pointsToStart = collapsed != collapsesTowardsStart;
                angle = pointsToStart ? 90f : -90f; // the sprite points down; positive angles turn clockwise
            }
            else
            {
                angle = collapsed ? -90f : 0f;
            }

            chevron.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
            collapseButton.tooltip = collapsed ? "Expand" : "Collapse";
        }
    }
}
