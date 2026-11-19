using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Registry;
using LMK.Editor.UIElements;
using LMK.Editor.Utilities;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Callbacks shared by every element of an attribute tree.
    /// </summary>
    internal sealed class AttributeTreeOptions
    {
        /// <summary>
        /// Called when a row's context menu is requested, after <see cref="RowClicked"/>.
        /// </summary>
        public Action<AttributeTreeEvent> ContextMenu;

        /// <summary>
        /// Called when the user clicks a row, or opens its context menu.
        /// </summary>
        public Action<AttributeTreeEvent> RowClicked;
    }



    /// <summary>
    /// A row event of an attribute tree.
    /// </summary>
    internal sealed class AttributeTreeEvent
    {
        public AttributeNodeElement element;

        public AttributeTreeNode node;

        public Vector2 mousePosition;
    }



    /// <summary>
    /// Root of a displayed attribute tree: draws the children of a root <see cref="AttributeTreeNode"/>, and
    /// distributes hierarchy events to all of them.
    /// </summary>
    internal sealed class AttributeTreeElement : HierarchyItem
    {
        public IEnumerable<AttributeNodeElement> NodeElements => ChildItems.OfType<AttributeNodeElement>();

        public AttributeTreeElement(AttributeTreeNode _Root, AttributeTreeOptions _Options)
        {
            name = "attribute-tree";

            // Once for the whole tree: it styles every row below.
            var styleSheet = LMKAssetUtility.Load<StyleSheet>(AttributeNodeElement.ELEMENT_USS_PATH);
            if (styleSheet != null)
                styleSheets.Add(styleSheet);

            if (_Root == null) return;

            foreach (var child in _Root.Children)
            {
                var element = new AttributeNodeElement(child, _Options);
                AddChildItem(element);
                Add(element);
            }
        }

        /// <summary>
        /// Show only the nodes matching <paramref name="_Filter"/>, see <see cref="SearchQuery"/> for the syntax.
        /// </summary>
        public void SetFilter(string _Filter) => DispatchHierarchyEvent(new FilterEvent(_Filter));

        /// <summary>
        /// Expand or collapse every branch.
        /// </summary>
        public void SetExpanded(bool _Expanded) => DispatchHierarchyEvent(new SetExpandedEvent(_Expanded));

        /// <summary>
        /// The element of a node, by <see cref="AttributeTreeNode.Key"/>, or <see langword="null"/>.
        /// </summary>
        public AttributeNodeElement Find(string _Key)
        {
            var pending = new Stack<AttributeNodeElement>(NodeElements);
            while (pending.Count > 0)
            {
                var element = pending.Pop();
                if (element.Node.Key == _Key) return element;

                foreach (var child in element.ChildElements)
                    pending.Push(child);
            }

            return null;
        }
    }



    /// <summary>
    /// One node of the attribute tree: a registry header or a group as a foldout, or an attribute as a leaf row with
    /// a summary of its values. <br/>
    /// Uses the element names of the gameplay tag tree, so the tag tree's stylesheet draws it.
    /// </summary>
    internal sealed class AttributeNodeElement : HierarchyItem, ISearchFieldProvider
    {
        /// <summary>
        /// The tag tree's row stylesheet: the attribute tree looks the same.
        /// </summary>
        public const string ELEMENT_USS_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/GameplayTagElementUSS.uss";

        private const string USS_CLASS = "attribute-row";
        private const string USS_CLASS_SUMMARY = USS_CLASS + "__summary";
        private const string USS_CLASS_ROW = "tag-row";
        private const string USS_CLASS_ROW_SELECTED = "tag-row--selected";

        // Foldout state of each branch, kept between tree redraws.
        private static readonly Dictionary<string, bool> foldoutLookup = new();

        private readonly AttributeTreeOptions options;
        private readonly Foldout foldout;

        // Normalized search fields, built on first search.
        private string searchName;
        private string searchDescription;

        public AttributeTreeNode Node { get; }

        /// <summary>
        /// The node's own row: the leaf container, or the foldout header of a branch.
        /// </summary>
        public VisualElement Row { get; }

        public AttributeNodeElement ParentElement => ParentItem as AttributeNodeElement;

        public IEnumerable<AttributeNodeElement> ChildElements => ChildItems.OfType<AttributeNodeElement>();

        public bool IsBranch => !Node.IsAttribute;



        public AttributeNodeElement(AttributeTreeNode _Node, AttributeTreeOptions _Options)
        {
            Node = _Node;
            options = _Options;

            var container = new VisualElement { name = "container" };
            var switcher = new Switcher { name = "switcher" };
            container.Add(switcher);

            // Leaf
            var leafContainer = new VisualElement { name = "leaf-tag-container" };
            leafContainer.AddToClassList(USS_CLASS);
            var leafLabel = new Label(_Node.ShortName) { name = "leaf-tag" };
            leafContainer.Add(leafLabel);
            switcher.Add(leafContainer);

            // Branch
            foldout = new Foldout { name = "branch-tag", text = _Node.ShortName, toggleOnLabelClick = false };
            switcher.Add(foldout);
            foldout.AddManipulator(new FoldoutArrowManipulator());
            var foldoutToggle = foldout.Q<Toggle>(className: Foldout.toggleUssClassName);
            foldout.RegisterValueChangedCallback(OnFoldoutValueChanged);

            bool isBranch = IsBranch;
            switcher.OnAttachedToPanel += () => switcher.SwitchActive(isBranch ? 1 : 0);

            Row = isBranch ? foldoutToggle : leafContainer;
            Row.AddToClassList(USS_CLASS_ROW);
            Row.RegisterCallback<ClickEvent>(OnRowClicked);
            Row.RegisterCallback<ContextClickEvent>(OnContextClick);
            Row.tooltip = BuildTooltip(_Node);

            if (_Node.IsAttribute)
            {
                var summary = new Label(Summary(_Node.Row)) { name = "summary" };
                summary.AddToClassList(USS_CLASS_SUMMARY);
                summary.pickingMode = PickingMode.Ignore;
                leafContainer.Add(summary);
            }

            // Registry badge, on registry headers only.
            var badge = new Label { name = "sources-label" };
            badge.style.display = _Node.IsRegistryHeader ? DisplayStyle.Flex : DisplayStyle.None;
            badge.text = _Node.IsReadOnly ? $"{OriginLabel(_Node.Origin)}, read-only" : OriginLabel(_Node.Origin);
            badge.tooltip = $"Registry {_Node.RegistryId}";
            badge.pickingMode = PickingMode.Ignore;
            container.Add(badge);

            if (foldoutLookup.TryGetValue(_Node.Key, out bool expanded))
                foldout.SetValueWithoutNotify(expanded);
            else
                foldoutLookup.Add(_Node.Key, foldout.value);

            foreach (var child in _Node.Children)
            {
                var element = new AttributeNodeElement(child, _Options);
                AddChildItem(element);
                foldout.Add(element);
            }

            Add(container);

            RegisterHierarchyCallback<SetExpandedEvent>(OnSetExpanded);
            RegisterHierarchyCallback<FilterEvent>(OnFilterMatch);
            RegisterHierarchyCallback<FilterEvent>(OnFilterApply, HierarchyEventPhase.AfterChildren);
        }



        /// <summary>
        /// Highlight the row as the tree's selection.
        /// </summary>
        public void SetRowSelected(bool _Selected) => Row.EnableInClassList(USS_CLASS_ROW_SELECTED, _Selected);

        /// <summary>
        /// Expand every ancestor, so this row is shown.
        /// </summary>
        public void Reveal()
        {
            for (var parent = ParentElement; parent != null; parent = parent.ParentElement)
            {
                parent.foldout.SetValueWithoutNotify(true);
                foldoutLookup[parent.Node.Key] = true;
            }
        }

        /// <summary>
        /// Normalized text of <paramref name="_Field"/>: the full name, or the description for <c>#</c> terms.
        /// </summary>
        public string GetSearchField(SearchField _Field) => _Field switch
        {
            SearchField.Name => searchName ??= SearchQuery.Normalize(Node.FullName),
            SearchField.Tooltip => searchDescription ??= SearchQuery.Normalize(Node.Row.description),
            _ => null,
        };



        #region Text
        /// <summary>
        /// "100  [0, MaxHealth]  meta  integer": the default, the bounds when there are any, and the flags.
        /// </summary>
        internal static string Summary(in AttributeRegistryRow _Row)
        {
            var parts = new List<string> { AttributeKind.FormatNumber(_Row.defaultValue) };
            if (!_Row.min.IsNone || !_Row.max.IsNone)
                parts.Add($"[{BoundText(_Row.min)}, {BoundText(_Row.max)}]");
            if ((_Row.flags & AttributeFlags.Meta) != 0) parts.Add("meta");
            if ((_Row.flags & AttributeFlags.Integer) != 0) parts.Add("integer");

            return string.Join("  ", parts);
        }

        /// <summary>
        /// A bound as text: a number, an attribute's name, or "–" for none.
        /// </summary>
        internal static string BoundText(in AttributeBound _Bound) => _Bound.Kind switch
        {
            AttributeBoundKind.Constant => AttributeKind.FormatNumber(_Bound.ConstantValue),
            AttributeBoundKind.Attribute => AttributeKind.Instance.GetRowName(_Bound.RegistryId, _Bound.AttributeId) ?? "(deleted)",
            _ => "–",
        };

        internal static string OriginLabel(RegistryOrigin _Origin) => _Origin switch
        {
            RegistryOrigin.Main => "main",
            RegistryOrigin.Package => "package",
            RegistryOrigin.Generated => "generated",
            _ => string.Empty,
        };

        private static string BuildTooltip(AttributeTreeNode _Node)
        {
            if (_Node.IsRegistryHeader)
                return $"{_Node.RegistryName} ({OriginLabel(_Node.Origin)} registry{(_Node.IsReadOnly ? ", read-only" : string.Empty)})";

            if (_Node.IsGroup)
                return $"Group \"{_Node.FullName}\"\nRegistry: {_Node.RegistryName}";

            var builder = new StringBuilder(_Node.FullName);
            builder.Append('\n').Append("Registry: ").Append(_Node.RegistryName);
            if (!string.IsNullOrWhiteSpace(_Node.Row.description))
                builder.Append('\n').Append(_Node.Row.description);

            return builder.ToString();
        }
        #endregion



        #region Events
        private void OnSetExpanded(SetExpandedEvent _Event)
        {
            if (!IsBranch) return;

            foldout.SetValueWithoutNotify(_Event.expanded);
            foldoutLookup[Node.Key] = _Event.expanded;
        }

        // Attributes match on their full name, so a group's name matches every attribute in it. Registry headers and
        // groups are kept when something under them matched.
        private void OnFilterMatch(FilterEvent _Event)
        {
            if (!Node.IsAttribute) return;

            if (_Event.IsEmpty || _Event.query.Matches(this))
                _Event.MarkMatched(this);
        }

        // Runs after every descendant.
        private void OnFilterApply(FilterEvent _Event)
        {
            style.display = _Event.IsEmpty || _Event.IsKept(this) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnRowClicked(ClickEvent _Event)
        {
            options.RowClicked?.Invoke(new AttributeTreeEvent { element = this, node = Node });

            // Nested foldouts would otherwise select every ancestor too.
            _Event.StopPropagation();
        }

        private void OnContextClick(ContextClickEvent _Event)
        {
            // The menu acts on this row, so it becomes the selected one.
            options.RowClicked?.Invoke(new AttributeTreeEvent { element = this, node = Node });
            options.ContextMenu?.Invoke(new AttributeTreeEvent { element = this, node = Node, mousePosition = _Event.originalMousePosition });

            // Nested foldouts would otherwise open a menu for every ancestor.
            _Event.StopPropagation();
        }

        private void OnFoldoutValueChanged(ChangeEvent<bool> _Event)
        {
            // Value changes of child foldouts bubble up through this foldout.
            if (_Event.target != foldout) return;

            foldoutLookup[Node.Key] = _Event.newValue;
        }
        #endregion
    }
}
