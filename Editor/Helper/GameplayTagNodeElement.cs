using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;
using LMK.Editor.Utilities;
using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Internal visual element class which displays a tag node within a tag tree. <br/>
    /// Child nodes are linked through <see cref="HierarchyItem"/>, and receive the tree's hierarchy events.
    /// </summary>
    internal sealed class GameplayTagNodeElement : HierarchyItem, ISearchFieldProvider
    {
        private const string UXML_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/GameplayTagElementUXML.uxml";

        private const string USS_CLASS_IMPLICIT = "implicit-tag";

        private const string USS_CLASS_SELECT_BOX_ACTIVE = "select-box-active";

        private const string USS_CLASS_DESCENDANT_SELECTED = "descendant-selected";

        private const string USS_CLASS_ROW = "tag-row";

        private const string USS_CLASS_ROW_SELECTED = "tag-row--selected";

        private const string USS_CLASS_MARKER = "tag-marker";

        private const string IMPLICIT_TOOLTIP = "Implicit tag: exists only as the parent of added tags.";

        // Foldout state of each full tag, kept between tree redraws.
        private static readonly Dictionary<string, bool> tagFoldoutLookup = new();



        private readonly GameplayTagTreeOptions options;

        private readonly Foldout foldout;
        private readonly Label label;
        private readonly Toggle selectBox;
        private readonly VisualElement marker;

        // Normalized search fields, built on first search.
        private string searchName;
        private string searchComment;

        internal GameplayTagNode TagNode { get; }

        /// <summary>
        /// The parent tag element, or <see langword="null"/> for top level tags.
        /// </summary>
        internal GameplayTagNodeElement ParentTagElement => ParentItem as GameplayTagNodeElement;

        /// <summary>
        /// The row of this tag alone: the label of a leaf, or the foldout header of a branch.
        /// </summary>
        internal VisualElement Row { get; }

        /// <summary>
        /// Child tag elements.
        /// </summary>
        internal IEnumerable<GameplayTagNodeElement> ChildTagElements => ChildItems.OfType<GameplayTagNodeElement>();

        /// <summary>
        /// Whether the select box is checked.
        /// </summary>
        internal bool Selected => selectBox.value;

        /// <summary>
        /// Whether any descendant's select box is checked, as of the last <see cref="TagSelectionSyncEvent"/>.
        /// </summary>
        internal bool DescendantSelected { get; private set; }



        public GameplayTagNodeElement(GameplayTagNode _NodeData, GameplayTagTreeOptions _Options)
        {
            TagNode = _NodeData;
            options = _Options;

            var root = LMKAssetUtility.Load<VisualTreeAsset>(UXML_PATH).CloneTree();

            bool isBranch = _NodeData.ChildCount > 0;

            // Main tag label display
            var switcher = root.Q<Switcher>("switcher");
            switcher.OnAttachedToPanel += () => switcher.SwitchActive(isBranch ? 1 : 0);

            label = root.Q<Label>("leaf-tag");
            label.text = _NodeData.ShortTag;
            label.RegisterCallback<ContextClickEvent>(OnContextClick);

            foldout = root.Q<Foldout>("branch-tag");
            foldout.text = _NodeData.ShortTag;
            foldout.toggleOnLabelClick = false;
            var foldoutToggle = foldout.Q<Toggle>(className: Foldout.toggleUssClassName);
            foldoutToggle.RegisterCallback<ContextClickEvent>(OnContextClick);
            foldout.RegisterValueChangedCallback(OnFoldoutExpandValueChanged);

            Row = isBranch ? foldoutToggle : root.Q("leaf-tag-container");
            Row.AddToClassList(USS_CLASS_ROW);
            Row.RegisterCallback<ClickEvent>(OnRowClicked);

            // Marker strip at the end of the row, in the tag's marker colour. Tags only.
            marker = new VisualElement { name = "marker", pickingMode = PickingMode.Ignore };
            marker.AddToClassList(USS_CLASS_MARKER);
            Row.Add(marker);
            SyncMarker();

            // Update foldout value.
            if (tagFoldoutLookup.TryGetValue(_NodeData.Key, out bool expanded))
                foldout.SetValueWithoutNotify(expanded);
            else
                tagFoldoutLookup.Add(_NodeData.Key, foldout.value);

            // Registry badge, on registry headers only.
            var registryBadge = root.Q<Label>("sources-label");
            registryBadge.style.display = _NodeData.IsRegistryHeader ? DisplayStyle.Flex : DisplayStyle.None;
            registryBadge.text = _NodeData.IsReadOnly ? $"{KindLabel(_NodeData.RegistryKind)}, read-only" : KindLabel(_NodeData.RegistryKind);
            registryBadge.tooltip = $"Registry {_NodeData.RegistryId}";

            // Select toggle box. Registry headers can't be selected.
            bool showSelectBox = _Options.ShowSelectBox && !_NodeData.IsRegistryHeader;
            selectBox = root.Q<Toggle>("select-box");
            selectBox.style.display = showSelectBox ? DisplayStyle.Flex : DisplayStyle.None;
            selectBox.RegisterValueChangedCallback(OnSelectBoxValueChanged);
            label.EnableInClassList(USS_CLASS_SELECT_BOX_ACTIVE, _Options.ShowSelectBox);
            foldout.EnableInClassList(USS_CLASS_SELECT_BOX_ACTIVE, _Options.ShowSelectBox);

            // Explicit state and description
            root.Q("container").EnableInClassList(USS_CLASS_IMPLICIT, !_NodeData.IsExplicit);
            label.tooltip = foldoutToggle.tooltip = BuildTooltip(_NodeData);

            // Child tags. Added after every query above: root.Q searches depth first, so it would otherwise
            // find the first child's elements inside the foldout before this element's own.
            foreach (var childNode in _NodeData.Children)
            {
                var childElement = new GameplayTagNodeElement(childNode, _Options);
                AddChildItem(childElement);
                foldout.Add(childElement);
            }

            Add(root);

            // Hierarchy events
            RegisterHierarchyCallback<SetExpandedEvent>(OnSetExpanded);
            RegisterHierarchyCallback<FilterEvent>(OnFilterMatch);
            RegisterHierarchyCallback<FilterEvent>(OnFilterApply, HierarchyEventPhase.AfterChildren);
            RegisterHierarchyCallback<TagSelectionSyncEvent>(OnSelectionSync);
            RegisterHierarchyCallback<TagSelectionSyncEvent>(OnSelectionSyncApply, HierarchyEventPhase.AfterChildren);
            RegisterHierarchyCallback<TagMarkerSyncEvent>(_ => SyncMarker());
        }



        /// <summary>
        /// Highlight the row as the tree's selected tag.
        /// </summary>
        internal void SetRowSelected(bool _Selected) => Row.EnableInClassList(USS_CLASS_ROW_SELECTED, _Selected);

        /// <summary>
        /// Expand every ancestor, so this tag's row is shown.
        /// </summary>
        internal void Reveal()
        {
            for (var parent = ParentTagElement; parent != null; parent = parent.ParentTagElement)
            {
                parent.foldout.SetValueWithoutNotify(true);
                tagFoldoutLookup[parent.TagNode.Key] = true;
            }
        }

        private void SyncMarker()
        {
            var color = Color.clear;
            bool hasMarker = !TagNode.IsRegistryHeader && TagOperations.TryGetMarker(TagNode.RegistryId, TagNode.Id, out color);
            marker.style.display = hasMarker ? DisplayStyle.Flex : DisplayStyle.None;
            marker.style.backgroundColor = hasMarker ? color : StyleKeyword.Null;
        }



        /// <summary>
        /// Normalized text of <paramref name="_Field"/>, computed once. The name field is the full tag,
        /// the tooltip field is the developer comment.
        /// </summary>
        public string GetSearchField(SearchField _Field)
        {
            return _Field switch
            {
                SearchField.Name => searchName ??= SearchQuery.Normalize(TagNode.IsRegistryHeader ? TagNode.ShortTag : TagNode.FullTag),
                SearchField.Tooltip => searchComment ??= SearchQuery.Normalize(TagNode.Comment),
                _ => null,
            };
        }

        private static string KindLabel(TagRegistryKind _Kind) => _Kind switch
        {
            TagRegistryKind.Main => "main",
            TagRegistryKind.Package => "package",
            TagRegistryKind.Generated => "generated",
            _ => string.Empty,
        };

        private static string BuildTooltip(GameplayTagNode _NodeData)
        {
            if (_NodeData.IsRegistryHeader)
                return $"{_NodeData.RegistryName} ({KindLabel(_NodeData.RegistryKind)} registry{(_NodeData.IsReadOnly ? ", read-only" : string.Empty)})";

            StringBuilder builder = new(_NodeData.FullTag);
            builder.Append('\n').Append("Registry: ").Append(_NodeData.RegistryName);

            if (!string.IsNullOrWhiteSpace(_NodeData.Comment))
                builder.Append('\n').Append(_NodeData.Comment);

            if (!_NodeData.IsExplicit)
                builder.Append('\n').Append(IMPLICIT_TOOLTIP);

            return builder.ToString();
        }



        #region Hierarchy Events
        private void OnSetExpanded(SetExpandedEvent _Event)
        {
            if (TagNode.ChildCount == 0) return;

            foldout.SetValueWithoutNotify(_Event.expanded);
            tagFoldoutLookup[TagNode.Key] = _Event.expanded;
        }

        // Every node can match, since parent tags are valid tags too.
        private void OnFilterMatch(FilterEvent _Event)
        {
            if (_Event.IsEmpty || _Event.query.Matches(this))
                _Event.MarkMatched(this);
        }

        // Runs after every descendant, so branches are hidden when none of their tags matched.
        private void OnFilterApply(FilterEvent _Event)
        {
            style.display = _Event.IsKept(this) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnSelectionSync(TagSelectionSyncEvent _Event)
        {
            selectBox.SetValueWithoutNotify(!TagNode.IsRegistryHeader && _Event.IsSelected(TagNode.Key));
        }

        // Runs after every descendant, so their selection state is up to date.
        private void OnSelectionSyncApply(TagSelectionSyncEvent _Event)
        {
            DescendantSelected = false;
            foreach (var child in ChildItems)
            {
                if (child is not GameplayTagNodeElement childElement) continue;
                if (!childElement.Selected && !childElement.DescendantSelected) continue;

                DescendantSelected = true;
                break;
            }

            foldout.EnableInClassList(USS_CLASS_DESCENDANT_SELECTED, DescendantSelected);
        }
        #endregion

        #region Callbacks
        private void OnSelectBoxValueChanged(ChangeEvent<bool> _Event)
        {
            options.SelectBoxChanged?.Invoke(new TagElementSelectCallback()
            {
                element = this,
                tagNode = TagNode
            }, _Event.newValue);
        }

        private void OnRowClicked(ClickEvent _Event)
        {
            options.RowClicked?.Invoke(new TagElementSelectCallback()
            {
                element = this,
                tagNode = TagNode
            });

            // Nested foldouts would otherwise select every ancestor too.
            _Event.StopPropagation();
        }

        private void OnContextClick(ContextClickEvent _Event)
        {
            // The menu acts on this tag, so it becomes the selected one.
            options.RowClicked?.Invoke(new TagElementSelectCallback()
            {
                element = this,
                tagNode = TagNode
            });

            options.ContextMenu?.Invoke(new TagElementSelectCallback()
            {
                element = this,
                mousePosition = _Event.originalMousePosition,
                tagNode = TagNode
            });

            // Nested foldouts would otherwise open a menu for every ancestor.
            _Event.StopPropagation();
        }

        private void OnFoldoutExpandValueChanged(ChangeEvent<bool> _Event)
        {
            // Value changes of child foldouts bubble up through this foldout.
            if (_Event.target != foldout) return;

            tagFoldoutLookup[TagNode.Key] = _Event.newValue;
        }
        #endregion
    }



    /// <summary>
    /// Display options and callbacks shared by all elements of a tag tree.
    /// </summary>
    internal sealed class GameplayTagTreeOptions
    {
        public bool ShowSelectBox;

        /// <summary>
        /// Called when a tag element context menu is requested.
        /// </summary>
        public Action<TagElementSelectCallback> ContextMenu;

        /// <summary>
        /// Called when the user changes a tag select box.
        /// </summary>
        public Action<TagElementSelectCallback, bool> SelectBoxChanged;

        /// <summary>
        /// Called when the user clicks a row, or opens its context menu.
        /// </summary>
        public Action<TagElementSelectCallback> RowClicked;
    }



    /// <summary>
    /// Root of a displayed tag tree. Draws the children of a root <see cref="GameplayTagNode"/>,
    /// and distributes hierarchy events to all of them.
    /// </summary>
    internal sealed class GameplayTagTreeElement : HierarchyItem
    {
        /// <summary>
        /// Top level tag elements.
        /// </summary>
        internal IEnumerable<GameplayTagNodeElement> TagElements => ChildItems.OfType<GameplayTagNodeElement>();

        public GameplayTagTreeElement(GameplayTagNode _RootNode, GameplayTagTreeOptions _Options)
        {
            name = "gameplay-tag-tree";

            if (_RootNode == null) return;

            foreach (var childNode in _RootNode.Children)
            {
                var childElement = new GameplayTagNodeElement(childNode, _Options);
                AddChildItem(childElement);
                Add(childElement);
            }
        }

        /// <summary>
        /// Show only the tags matching <paramref name="_Filter"/>, see <see cref="SearchQuery"/> for the syntax. <br/>
        /// '#' terms search the tags' developer comments.
        /// </summary>
        public void SetFilter(string _Filter)
            => DispatchHierarchyEvent(new FilterEvent(_Filter));

        /// <summary>
        /// Expand or collapse every tag.
        /// </summary>
        public void SetExpanded(bool _Expanded)
            => DispatchHierarchyEvent(new SetExpandedEvent(_Expanded));

        /// <summary>
        /// Check the select box of every tag in <paramref name="_SelectedTags"/>, and uncheck the others.
        /// </summary>
        public void SyncSelection(IEnumerable<GameplayTag> _SelectedTags)
            => DispatchHierarchyEvent(new TagSelectionSyncEvent(_SelectedTags));

        /// <summary>
        /// Show every tag's current marker colour.
        /// </summary>
        public void SyncMarkers()
            => DispatchHierarchyEvent(new TagMarkerSyncEvent());

        /// <summary>
        /// The element of a node, by <see cref="GameplayTagNode.Key"/>, or <see langword="null"/>.
        /// </summary>
        public GameplayTagNodeElement Find(string _Key)
        {
            var pending = new Stack<GameplayTagNodeElement>(TagElements);
            while (pending.Count > 0)
            {
                var element = pending.Pop();
                if (element.TagNode.Key == _Key) return element;

                foreach (var child in element.ChildTagElements)
                    pending.Push(child);
            }

            return null;
        }
    }



    /// <summary>
    /// Ask tag elements to show their current marker colour.
    /// </summary>
    internal sealed class TagMarkerSyncEvent : HierarchyItemEvent<TagMarkerSyncEvent> { }



    /// <summary>
    /// Ask tag elements to update their select box from a set of selected tags.
    /// </summary>
    internal sealed class TagSelectionSyncEvent : HierarchyItemEvent<TagSelectionSyncEvent>
    {
        private readonly HashSet<string> selectedKeys = new(StringComparer.Ordinal);

        public TagSelectionSyncEvent(IEnumerable<GameplayTag> _SelectedTags)
        {
            if (_SelectedTags == null) return;

            foreach (var tag in _SelectedTags)
                if (!tag.IsNone)
                    selectedKeys.Add(KeyOf(tag));
        }

        /// <summary>
        /// Same key as <see cref="GameplayTagNode.Key"/> for a tag node.
        /// </summary>
        public static string KeyOf(GameplayTag _Tag) => $"{_Tag.RegistryId}:{_Tag.Id}";

        public bool IsSelected(string _Key) => selectedKeys.Contains(_Key);
    }



    /// <summary>
    /// Contains callback data from context menu action of tags' visual element.
    /// </summary>
    internal class TagElementSelectCallback
    {
        public Vector2 mousePosition;

        public GameplayTagNode tagNode;

        public GameplayTagNodeElement element;
    }
}
