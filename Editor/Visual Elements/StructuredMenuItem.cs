using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Visual element wrapper to display menu items using structured hierarchy.
    /// </summary>
    public class StructuredMenuItem : HierarchyItem, ISearchFieldProvider
    {
        /// <summary>
        /// The parent item containing this item. <br/>
        /// If empty, this item is the root element in the hierarchy.
        /// </summary>
        public StructuredMenuItem ContainerItem => ParentItem as StructuredMenuItem;

        private readonly Dictionary<string, StructuredMenuItem> subItems = new();
        /// <summary>
        /// Collection of all children of this item. <br/>
        /// If empty, this item is considered a "leaf item".
        /// </summary>
        public ICollection<StructuredMenuItem> SubItems => subItems.Values;

        /// <summary>
        /// Number of children of this item. <br/>
        /// If == 0, this item is considered a "leaf item".
        /// </summary>
        public int SubItemsCount => subItems.Count;

        /// <summary>
        /// Whether any direct child of this item is a branch, i.e. can be expanded or collapsed.
        /// </summary>
        public bool HasBranchItems
        {
            get
            {
                foreach (var item in subItems.Values)
                {
                    if (item.IsDisplayedAsBranch) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Full path which define the full hierarchy from root till the current level.
        /// </summary>
        public string FullPath { get; private set; }

        /// <summary>
        /// Name of this item at the current level.
        /// </summary>
        public string Label { get; private set; }

        /// <summary>
        /// Tooltip of this item if has. Only leaf items can contain tooltip.
        /// </summary>
        public string Tooltip { get; private set; }

        /// <summary>
        /// Custom icon of this item if has. Only leaf items can contain icon.
        /// </summary>
        public Texture Icon { get; private set; }

        private string searchNamespaceRaw;
        /// <summary>
        /// Namespace and/or assembly text searched by <c>@</c> terms, see <see cref="SearchQuery"/>.
        /// </summary>
        public string SearchNamespace
        {
            get => searchNamespaceRaw;
            set
            {
                searchNamespaceRaw = value;
                searchNamespace = null;
            }
        }

        private bool isDisplayedAsBranch = false;
        /// <summary>
        /// The current displayed element of this item, used to track and update the element structure properly.
        /// </summary>
        public bool IsDisplayedAsBranch
        {
            get => isDisplayedAsBranch;
            set
            {
                if (isDisplayedAsBranch == value) return;
                isDisplayedAsBranch = value;
                mainItemSwitch.SwitchActive(value ? GetOrCreateBranchRoot() : leafItemRoot);
            }
        }

        private const string USS_CLASS = "menu-item";

        private const string USS_CLASS_ROOT = USS_CLASS + "__root";

        private const string USS_CLASS_BRANCH = USS_CLASS + "__branch-container";

        private const string USS_CLASS_LEAF = USS_CLASS + "__leaf-container";

        private const string USS_CLASS_LABEL = USS_CLASS + "__label";

        private const string USS_CLASS_ICON = USS_CLASS + "__icon";

        private readonly Switcher mainItemSwitch;

        // Created on first sub item: most items are leaves, and a hidden Foldout still costs ~6 elements each.
        private Foldout branchItemRoot;

        private readonly VisualElement leafItemRoot;

        // Normalized search fields, built on first search.
        private string searchName;
        private string searchNamespace;
        private string searchTooltip;



        /// <summary>
        /// Create a new item with a given <see cref="GUIContent"/>.
        /// </summary>
        /// <param name="_Content"> Item content at the current level in the hierarchy. Label supports rich text. <br/>
        internal StructuredMenuItem(GUIContent _Content)
        {
            if (_Content != null)
            {
                name = $"StructuredMenuItem ({_Content.text})";

                Label = _Content.text;
                Tooltip = _Content.tooltip;
                Icon = _Content.image;

                tooltip = Tooltip;
            }

            // Leaf
            leafItemRoot = new();
            leafItemRoot.name = "structured_menu_item-leaf";
            leafItemRoot.AddToClassList(USS_CLASS_LEAF);

            Image icon = new();
            icon.image = Icon;
            icon.focusable = false;
            icon.AddToClassList(USS_CLASS_ICON);
            leafItemRoot.Add(icon);

            Label itemName = new();
            itemName.text = Label;
            itemName.tooltip = Tooltip;
            itemName.AddToClassList(USS_CLASS_LABEL);
            leafItemRoot.Add(itemName);

            // Switcher
            mainItemSwitch = new();
            mainItemSwitch.Add(leafItemRoot);
            Add(mainItemSwitch);

            mainItemSwitch.SwitchActive(0);

            AddToClassList(USS_CLASS_ROOT);

            // Hierarchy events
            RegisterHierarchyCallback<SetExpandedEvent>(OnSetExpanded);
            RegisterHierarchyCallback<FilterEvent>(OnFilterMatch);
            RegisterHierarchyCallback<FilterEvent>(OnFilterApply, HierarchyEventPhase.AfterChildren);
        }



        /// <summary>
        /// Add a new leaf item to this item using a given sub path.
        /// </summary>
        /// <param name="_Content"> <see cref="GUIContent"/> containing the path of a new leaf item to add. 
        ///                         Supports rich text. </param>
        /// <returns>
        /// The leaf item element if successful.
        /// </returns>
        public StructuredMenuItem AddItem(GUIContent _Content)
        {
            // Resolve invalid path names
            if (string.IsNullOrWhiteSpace(_Content.text)) return null;
            if (_Content.text.IndexOf('/') == 0) return null;

            string subPath = _Content.text; // remaining path name from this item to leaf item, including leaf item
            string subName = subPath; // name of the next item (branch or leaf)
            int separatorIndex = -1;
            bool isBranch = false;
            int searchStart = 0;

            while (true)
            {
                int idx = subPath.IndexOf('/', searchStart);
                if (idx < 0)
                    break;

                // Ignore '/' immediately after '<', since rich text close tags starts with "</...>"
                if (idx > 0 && subPath[idx - 1] == '<')
                {
                    searchStart = idx + 1;
                    continue;
                }

                separatorIndex = idx;
                isBranch = true;
                break;
            }

            if (isBranch) 
                subName = subPath[..separatorIndex];

            if (string.IsNullOrWhiteSpace(subName)) return null; // Invalid sub name

            // If sub path is a leaf item, add item directly and return
            if (!isBranch)
            {
                if (subItems.ContainsKey(subName)) return null;

                return AddNewItemElement(_Content);
            }

            // If sub path starts with a branch, add or fetch branch item and return added item in it
            if (!subItems.ContainsKey(subName))
            {
                AddNewItemElement(new GUIContent(subName));
            }

            var branchItem = subItems[subName];
            GUIContent subContent = new(
                text: subPath[(separatorIndex + 1)..],
                image: _Content.image, 
                tooltip: _Content.tooltip);
            return branchItem.AddItem(subContent);
        }

        public bool RemoveItem(string _SubPath)
        {
            if (string.IsNullOrWhiteSpace(_SubPath)) return false;

            return true;
        }

        public bool RemoveItem(int _Index)
        {
            if (_Index < 0 || _Index >= subItems.Count) return false;

            return true;
        }

        /// <summary>
        /// Remove all sub items of this item, so it is displayed as a leaf again.
        /// </summary>
        public void Reset()
        {
            subItems.Clear();
            ClearChildItems();
            branchItemRoot?.Clear(); // clears the foldout content only, the item's own elements are kept

            IsDisplayedAsBranch = false;
        }



        protected override void OnParentItemChanged()
        {
            EnableInClassList(USS_CLASS_ROOT, IsRoot);

            var containerItem = ContainerItem;
            FullPath = string.IsNullOrEmpty(containerItem?.FullPath)
                ? Label
                : $"{containerItem.FullPath}/{Label}";
            searchName = null;
        }

        /// <summary>
        /// Normalized text of <paramref name="_Field"/>, computed once. The name field is the full path.
        /// </summary>
        public string GetSearchField(SearchField _Field)
        {
            return _Field switch
            {
                SearchField.Name => searchName ??= SearchQuery.Normalize(FullPath),
                SearchField.Namespace => searchNamespace ??= SearchQuery.Normalize(SearchNamespace),
                SearchField.Tooltip => searchTooltip ??= SearchQuery.Normalize(Tooltip),
                _ => null,
            };
        }



        #region Hierarchy Events
        private void OnSetExpanded(SetExpandedEvent _Event)
        {
            // The root's foldout has no visible toggle, so it always stays expanded.
            if (!IsRoot && branchItemRoot != null)
                branchItemRoot.value = _Event.expanded;
        }

        // Leaves match against their whole path, so a folder name matches every item inside it.
        private void OnFilterMatch(FilterEvent _Event)
        {
            if (IsRoot || SubItemsCount > 0) return;

            if (_Event.query.Matches(this))
                _Event.MarkMatched(this);
        }

        // Runs after every descendant, so branches are hidden when none of their items matched.
        private void OnFilterApply(FilterEvent _Event)
        {
            if (IsRoot) return;

            style.display = _Event.IsKept(this) ? DisplayStyle.Flex : DisplayStyle.None;
        }
        #endregion

        private StructuredMenuItem AddNewItemElement(GUIContent _Content)
        {
            StructuredMenuItem itemElement = new(_Content);
            AddChildItem(itemElement);
            subItems.Add(_Content.text, itemElement);
            GetOrCreateBranchRoot().Add(itemElement);

            IsDisplayedAsBranch = SubItemsCount > 0;

            return itemElement;
        }

        private Foldout GetOrCreateBranchRoot()
        {
            if (branchItemRoot != null) return branchItemRoot;

            branchItemRoot = new();
            branchItemRoot.name = "structured_menu_item-branch";
            branchItemRoot.text = Label;
            branchItemRoot.AddToClassList(USS_CLASS_BRANCH);
            mainItemSwitch.Add(branchItemRoot);

            return branchItemRoot;
        }
    }
}
