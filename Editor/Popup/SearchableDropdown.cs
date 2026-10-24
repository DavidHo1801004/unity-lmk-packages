using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.UIElements;
using LMK.Editor.Utilities;

namespace LMK.Editor.Windows
{
    /// <summary>
    /// Helper class which defines a dropdown list of items with search handling.
    /// </summary>
    public class SearchableDropdown : PopupWindowContent
    {
        private const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Popup/Assets/SearchableDropdownUSS.uss";
        private const string HIERARCHY_MENU_ICON_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/Sprites/down-arrow-symbol-white.png";

        public event Action<int> OnSelectedItemChanged;

        private List<GUIContent> items = new();

        private SearchBar searchBar;
        private ScrollView contentScrollView;
        private Label footer;

        private readonly StructuredMenuItem rootItem;
        private readonly Dictionary<string, VisualElement> menuItemLookup = new();
        private int filteredItems = 0;

        public int Index { get; private set; }



        public SearchableDropdown(params GUIContent[] _Items)
            : this()
        {
            AddItems(_Items);
        }

        /// <param name="_ItemNamespaces"> Optional namespace/assembly text per item, searched with <c>@</c> terms. </param>
        public SearchableDropdown(GUIContent[] _Items, IReadOnlyList<string> _ItemNamespaces)
            : this()
        {
            AddItems(_Items, _ItemNamespaces);
        }

        public SearchableDropdown(params string[] _Items)
            : this(_Items.Select(e => new GUIContent(e)).ToArray())
        { }

        public SearchableDropdown()
        {
            rootItem = new(null);
        }



        public override VisualElement CreateGUI()
        {
            VisualElement root = new()
            {
                name = "main-container",
            };

            LMKTheme.Apply(root);
            LMKTheme.AddStyleSheet(root, USS_PATH);

            // Search row: search bar, then the hierarchy control button on its right
            VisualElement searchRow = new()
            {
                name = "search-row",
            };
            root.Add(searchRow);

            searchBar = new()
            {
                name = "search-bar",
                tooltip = SearchQuery.SYNTAX_HELP,
            };
            searchBar.RegisterValueChangedCallback(OnSearchBarValueChanged);
            searchRow.Add(searchBar);

            Button hierarchyMenuButton = new()
            {
                name = "hierarchy-menu-button",
                tooltip = "Expand or collapse all folders",
            };
            hierarchyMenuButton.clicked += () => ShowHierarchyMenu(hierarchyMenuButton);
            hierarchyMenuButton.SetEnabled(rootItem.HasBranchItems);
            hierarchyMenuButton.Add(new Image
            {
                name = "hierarchy-menu-icon",
                image = LMKAssetUtility.Load<Texture2D>(HIERARCHY_MENU_ICON_PATH),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore,
            });
            searchRow.Add(hierarchyMenuButton);

            // Main content scroll view
            contentScrollView = new()
            {
                name = "popup-content",
            };
            contentScrollView.Add(rootItem);
            root.Add(contentScrollView);

            // Footer
            footer = new()
            {
                name = "footer",
                text = GetFooterText(),
            };
            root.Add(footer);

            return root;
        }



        /// <summary>
        /// Get menu item name at the given index.
        /// </summary>
        /// <returns>
        /// A string represents the item name if valid; <br/>
        /// otherwise, return <see langword="null"/>
        /// </returns>
        public string GetMenuItemNameAt(int _Index)
        {
            if (_Index < 0 || _Index >= items.Count) return null;

            return items[_Index].text;
        }

        /// <summary>
        /// Clear all exiting items.
        /// </summary>
        public void Clear()
        {
            items.Clear();
            menuItemLookup.Clear();
            rootItem.Reset();
        }

        /// <summary>
        /// Add new items to the dropdown selection window.
        /// </summary>
        public void AddItems(params GUIContent[] _Items)
        {
            AddItems(_Items, null);
        }

        /// <inheritdoc cref="AddItems(GUIContent[])"/>
        /// <param name="_ItemNamespaces"> Optional namespace/assembly text per item, searched with <c>@</c> terms. </param>
        public void AddItems(GUIContent[] _Items, IReadOnlyList<string> _ItemNamespaces)
        {
            for (int i = 0; i < _Items.Length; i++)
            {
                // Every item keeps its index, even if it can't be displayed, so indices match the caller's items.
                int itemIndex = items.Count;
                items.Add(_Items[i]);

                if (menuItemLookup.ContainsKey(_Items[i].text)) continue;

                var menuItem = rootItem.AddItem(_Items[i]);
                if (menuItem == null) continue; // invalid or duplicated path

                if (_ItemNamespaces != null && i < _ItemNamespaces.Count)
                    menuItem.SearchNamespace = _ItemNamespaces[i];

                menuItem.RegisterCallback<PointerDownEvent>((e) =>
                {
                    if (e.button != 0) return;

                    Index = itemIndex;

                    OnSelectedItemChanged?.Invoke(Index);
                    editorWindow.Close();
                });

                menuItemLookup.Add(_Items[i].text, menuItem);
            }
        }

        // UI Toolkit menu: it is drawn inside this popup, so opening it doesn't take focus and close the popup.
        private void ShowHierarchyMenu(VisualElement _Button)
        {
            var menu = new GenericDropdownMenu();
            menu.AddItem("Expand all", false, () => rootItem.DispatchHierarchyEvent(new SetExpandedEvent(true)));
            menu.AddItem("Collapse all", false, () => rootItem.DispatchHierarchyEvent(new SetExpandedEvent(false)));
            menu.DropDown(_Button.worldBound, _Button, DropdownMenuSizeMode.Content); // the button is too narrow to set the menu width
        }

        // Update visible items based on search bar value. Folders whose items are all filtered out are hidden too.
        private void OnSearchBarValueChanged(ChangeEvent<string> _Event)
        {
            var filterEvent = new FilterEvent(_Event.newValue);
            rootItem.DispatchHierarchyEvent(filterEvent);
            filteredItems = menuItemLookup.Count - filterEvent.MatchedCount;

            footer.text = GetFooterText();
        }

        // Counts displayed items only: invalid or duplicated paths are never shown.
        private string GetFooterText()
        {
            int shownItems = menuItemLookup.Count - filteredItems;
            return $"{shownItems} item{(shownItems == 1 ? "" : "s")} "
                + (filteredItems > 0 ? $"({filteredItems} filtered)" : "");
        }
    }
}
