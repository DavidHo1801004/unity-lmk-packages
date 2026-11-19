using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Registry;
using LMK.Editor.Styles;
using LMK.Editor.UIElements;
using LMK.Editor.Utilities;
using LMK.Editor.Windows;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

using ToolbarButton = LMK.Editor.UIElements.ToolbarButton;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Shows every loaded attribute registry and edits the main one: a toolbar (Save, Discard, search, Add, options),
    /// the attribute tree (registry > group > attribute), and an inspector panel for the selected attribute. Laid out
    /// and styled like the Gameplay Tag Manager. Edits are staged until saved, and Unity asks to save or discard them
    /// when the window closes (<see cref="RegistryWindow"/>). Offers to create the main registry when the project has
    /// none.
    /// </summary>
    internal sealed class AttributeManagerWindow : RegistryWindow
    {
        public const string TITLE = "Gameplay Attributes";
        private const string MENU = "Tools/LMK/Ability System/";

        private const string USS_PATH = "Packages/com.lmk.abilitysystem/Editor/Assets/AttributeManagerUSS.uss";
        private const string USS_ROOT = "attribute-manager";

        // The Gameplay Tag Manager's stylesheets: this window uses its element names and classes, so both look the same.
        private const string TAG_ASSETS = "Packages/com.lmk.gameplaytags/Editor/Assets/";
        private const string TAG_GENERAL_USS = TAG_ASSETS + "GeneralGameplayTagUSS.uss";
        private const string TAG_WINDOW_USS = TAG_ASSETS + "GameplayTagManagerWindowUSS.uss";
        private const string TAG_PANEL_USS = TAG_ASSETS + "GameplayTagControlPanelUSS.uss";
        private const string GEAR_ICON_PATH = TAG_ASSETS + "Sprites/gear_white_32.png";

        private const string RENAME_FIELD_CLASS = "tag-rename-field";

        // The Add popup's size and place, as the Add Tag popup: at the tree's top-right corner, inset.
        private const float ADD_POPUP_WIDTH = 260f;
        private static readonly Vector2 ADD_POPUP_OFFSET = new(0f, 2f);
        private static readonly Vector2 ADD_POPUP_TREE_INSET = new(8f, 8f);

        /// <summary>
        /// The search syntax, for the search field's tooltip. <c>#</c> terms search descriptions.
        /// </summary>
        internal const string SEARCH_HELP =
            "a b: both terms\n" +
            "a|b: either term\n" +
            "-a: exclude term\n" +
            "\"a b\": exact phrase\n" +
            "#a: description";

        // Kept across domain reloads.
        [SerializeField] private string selectedKey = string.Empty;
        [SerializeField] private string search = string.Empty;

        private RegistryToolbar registryToolbar;
        private ToolbarButton addButton;
        private ToolbarButton optionsButton;
        private VisualElement createPanel;
        private PanelLayout layout;
        private ScrollView treeScrollView;
        private Label emptyLabel;
        private HelpBox helpBox;
        private AttributeInspectorPanel inspector;

        private readonly AttributeTreeOptions treeOptions = new();
        private AttributeNodeElement selectedElement;

        // A selection to reveal and scroll to once the tree has a layout.
        private bool revealPending;

        /// <summary>
        /// The displayed tree. Replaced every time the tree is rebuilt.
        /// </summary>
        internal AttributeTreeElement Tree { get; private set; }

        /// <summary>
        /// The scroll view holding <see cref="Tree"/>.
        /// </summary>
        internal VisualElement TreeView => treeScrollView;

        /// <summary>
        /// The selected node, or <see langword="null"/>.
        /// </summary>
        internal AttributeTreeNode SelectedNode => selectedElement?.Node;

        private static AttributeKind Kind => AttributeKind.Instance;



        [MenuItem(MENU + "Attribute Manager", priority = 0)]
        public static AttributeManagerWindow Open() => GetWindow<AttributeManagerWindow>(TITLE);

        /// <summary>
        /// Open the window with a registry's header selected.
        /// </summary>
        public static void OpenRegistry(RegistryGuid _RegistryId)
        {
            var window = Open();
            window.SelectKey(AttributeTreeNode.KeyOfRegistry(_RegistryId));
        }

        /// <summary>
        /// Open the window with an attribute selected.
        /// </summary>
        public static void OpenAttribute(RegistryGuid _RegistryId, RegistryGuid _Id)
        {
            var window = Open();
            window.SelectKey(AttributeTreeNode.KeyOf(_RegistryId, _Id));
        }

        protected override void OnEnable()
        {
            Kind.SnapshotChanged += Rebuild;
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            Kind.SnapshotChanged -= Rebuild;
            base.OnDisable();
        }

        protected override void OnStateChanged() => registryToolbar?.Refresh();



        #region Layout
        private void CreateGUI()
        {
            var root = rootVisualElement;
            LMKTheme.Apply(root);
            root.AddToClassList(USS_ROOT);
            AddStyleSheet(root, TAG_GENERAL_USS);
            AddStyleSheet(root, TAG_WINDOW_USS);
            AddStyleSheet(root, USS_PATH);

            treeOptions.RowClicked = e => SetSelection(e.element);
            treeOptions.ContextMenu = ShowContextMenu;

            // Shown while the project has no main registry. Package registries are still listed under it.
            createPanel = new VisualElement { name = "create-panel" };
            createPanel.AddToClassList(USS_ROOT + "__create");
            createPanel.Add(new Label("This project has no main attribute registry yet.") { name = "create-label" });
            createPanel.Add(new Button(CreateMainRegistry) { name = "create-registry-button", text = "Create Main Attribute Registry" });
            root.Add(createPanel);

            layout = new PanelLayout { name = "layout", PersistenceKey = "LMK.AbilitySystem.AttributeManagerLayout" };
            layout.AddToClassList("fill");
            root.Add(layout);

            layout.Add(CreateTreePanel());

            var inspectorPanel = new CollapsiblePanel("Inspector") { name = "inspector", MinSize = 200, MaxSize = 640 };
            inspectorPanel.style.width = 280;
            inspector = new AttributeInspectorPanel();
            inspector.Report += ShowMessage;
            inspector.AttributeClicked += (registry, id) => SelectKey(AttributeTreeNode.KeyOf(registry, id));
            inspectorPanel.Add(inspector);
            layout.Add(inspectorPanel);

            Rebuild();
        }

        /// <summary>
        /// The toolbar, the scrollable tree and the message box under it.
        /// </summary>
        private VisualElement CreateTreePanel()
        {
            var treePanel = new VisualElement { name = "tree-panel" };
            PanelLayout.SetFill(treePanel, true);
            treePanel.Add(CreateToolbar());

            var content = new VisualElement { name = "tree-content" };
            content.AddToClassList("fill");
            content.AddToClassList("gameplay-tag-control-panel--no-header");
            AddStyleSheet(content, TAG_PANEL_USS);

            var treeArea = new VisualElement();
            treeArea.AddToClassList("fill");

            // Named like the tag tree's scroll view, which the tag stylesheets style.
            treeScrollView = new ScrollView(ScrollViewMode.Vertical) { name = "tag-tree-scroll-view" };
            treeScrollView.AddToClassList("fill-background");
            treeArea.Add(treeScrollView);

            emptyLabel = new Label("No Gameplay Attribute Registry") { name = "empty-data-label" };
            emptyLabel.pickingMode = PickingMode.Ignore;
            treeArea.Add(emptyLabel);
            content.Add(treeArea);

            helpBox = new HelpBox(string.Empty, HelpBoxMessageType.Info) { name = "help-box-message" };
            helpBox.style.display = DisplayStyle.None;
            content.Add(helpBox);

            treePanel.Add(content);
            return treePanel;
        }

        private VisualElement CreateToolbar()
        {
            var toolbar = new VisualElement { name = "toolbar" };
            toolbar.AddToClassList("gameplay-tag-toolbar");

            registryToolbar = new RegistryToolbar();
            registryToolbar.style.flexGrow = 1;
            toolbar.Add(registryToolbar);

            var searchField = new ToolbarSearchField { name = "search-field", value = search, tooltip = SEARCH_HELP };
            searchField.RegisterValueChangedCallback(e =>
            {
                search = e.newValue ?? string.Empty;
                Tree?.SetFilter(search);
            });
            registryToolbar.AddControl(searchField);

            addButton = new ToolbarButton(false) { name = "add-button", text = "Add Attribute", tooltip = "Add an attribute to the main registry." };
            addButton.clicked += () => ShowAdd(string.Empty);
            registryToolbar.AddControl(addButton);

            optionsButton = new ToolbarButton(false)
            {
                name = "options-button",
                iconImage = LMKAssetUtility.Load<Texture2D>(GEAR_ICON_PATH),
                tooltip = "Options",
            };
            optionsButton.clicked += () => ShowOptionsMenu(optionsButton);
            registryToolbar.AddControl(optionsButton);

            return toolbar;
        }

        private static void AddStyleSheet(VisualElement _Element, string _Path)
        {
            var styleSheet = LMKAssetUtility.Load<StyleSheet>(_Path);
            if (styleSheet != null)
                _Element.styleSheets.Add(styleSheet);
        }

        /// <summary>
        /// Build the tree again from the current snapshot, and select the same node if it still exists.
        /// </summary>
        private void Rebuild()
        {
            if (treeScrollView == null) return;

            var snapshot = Kind.Snapshot;
            createPanel.style.display = snapshot.Main == null ? DisplayStyle.Flex : DisplayStyle.None;
            layout.style.display = snapshot.IsEmpty ? DisplayStyle.None : DisplayStyle.Flex;
            addButton.SetEnabled(snapshot.Main != null && !snapshot.Main.IsReadOnly);

            treeScrollView.Clear();
            Tree = new AttributeTreeElement(AttributeTreeNode.Build(snapshot), treeOptions);
            treeScrollView.Add(Tree);

            if (!string.IsNullOrWhiteSpace(search))
                Tree.SetFilter(search);

            emptyLabel.style.display = snapshot.IsEmpty ? DisplayStyle.Flex : DisplayStyle.None;

            ShowRegistryProblems(snapshot);
            registryToolbar.Refresh();

            selectedElement = null;
            var element = string.IsNullOrEmpty(selectedKey) ? null : Tree.Find(selectedKey);
            SetSelection(element, true);

            if (element != null && revealPending)
                Reveal(element);
        }

        /// <summary>
        /// Warn about registries edited outside the manager, rejected files, duplicate registry IDs, inactive and
        /// outdated package registries, and a main registry changed on disk under staged edits.
        /// </summary>
        private void ShowRegistryProblems(RegistrySnapshot<AttributeRegistryRow> _Snapshot)
        {
            var problems = new List<string>(_Snapshot.Issues);
            foreach (var registry in _Snapshot.Registries)
            {
                if (registry.UsingLastGood)
                    problems.Add($"\"{registry.AssetPath}\" is invalid, so its last loaded version is shown. {string.Join(" ", registry.Errors)}");
                else if (registry.ChecksumMismatch)
                    problems.Add($"\"{registry.AssetPath}\" was edited outside the Attribute Manager (checksum mismatch).");
            }

            if (Kind.DiskChangedWhilePending)
                problems.Add($"\"{Kind.MainRegistryPath}\" changed on disk while you have unsaved changes. Discard them to load it.");

            problems.AddRange(Kind.InactiveRegistries);

            foreach (var (package, assetPath, generated, installed) in Kind.FindOutdatedRegistries())
                problems.Add($"\"{package}\" is now {installed}, but \"{assetPath}\" was generated from {generated}.");

            if (problems.Count > 0)
                ShowMessage(string.Join("\n", problems), HelpBoxMessageType.Warning);
        }
        #endregion



        #region Selection
        /// <summary>
        /// Select a node by <see cref="AttributeTreeNode.Key"/>, expand its parents and scroll to it.
        /// </summary>
        private void SelectKey(string _Key)
        {
            selectedKey = _Key ?? string.Empty;
            revealPending = true;

            if (Tree == null) return;

            var element = Tree.Find(selectedKey);
            SetSelection(element);
            if (element != null)
                Reveal(element);
        }

        private void Reveal(AttributeNodeElement _Element)
        {
            revealPending = false;
            _Element.Reveal();

            // Scroll once the expanded rows have a layout.
            treeScrollView.schedule.Execute(() =>
            {
                if (_Element.panel != null)
                    treeScrollView.ScrollTo(_Element.Row);
            });
        }

        private void SetSelection(AttributeNodeElement _Element, bool _Rebuilt = false)
        {
            if (_Element == selectedElement && !_Rebuilt) return;

            selectedElement?.SetRowSelected(false);
            selectedElement = _Element;
            selectedElement?.SetRowSelected(true);

            // A rebuild keeps the key of a node that disappeared, e.g. while its registry reloads.
            if (_Element != null || !_Rebuilt)
                selectedKey = _Element?.Node.Key ?? string.Empty;

            inspector?.SetNode(_Element?.Node);
        }
        #endregion



        #region Menus
        /// <summary>
        /// The context menu of a row. Items are grouped by what they do: add, edit, references, copy, then delete.
        /// </summary>
        private void ShowContextMenu(AttributeTreeEvent _Event)
        {
            var node = _Event.node;
            var menu = new GenericDropdownMenu();
            bool canEdit = !node.IsReadOnly;

            switch (node.Kind)
            {
                case AttributeTreeNodeKind.RegistryHeader:
                {
                    if (canEdit)
                    {
                        menu.AddItem("Add Attribute", false, () => ShowAdd(string.Empty));
                        menu.AddSeparator(null);
                    }

                    string assetPath = Kind.Snapshot.Find(node.RegistryId)?.AssetPath;
                    menu.AddItem("Select Registry Asset", false, () => SelectAsset(assetPath));
                    break;
                }

                case AttributeTreeNodeKind.Group:
                {
                    if (canEdit)
                    {
                        menu.AddItem("Add Attribute To Group", false, () => ShowAdd(node.FullName + Separator));
                        menu.AddSeparator(null);
                    }

                    string group = node.FullName;
                    menu.AddItem("Copy Group Name", false, () => EditorGUIUtility.systemCopyBuffer = group);
                    break;
                }

                case AttributeTreeNodeKind.Attribute:
                {
                    if (canEdit)
                    {
                        menu.AddItem("Add Sibling Attribute", false, () => ShowAdd(string.IsNullOrEmpty(node.Group) ? string.Empty : node.Group + Separator));
                        menu.AddSeparator(null);
                        menu.AddItem("Rename Attribute", false, () => Rename(_Event.element, node));
                        menu.AddSeparator(null);
                    }

                    // Reference tools write assets, not registries, so they work for attributes of any registry.
                    var row = (node.RegistryId, node.Id);
                    menu.AddItem("Find References", false, () => RegistryReferenceWindow.ShowReferences(Kind, row));
                    menu.AddItem("Replace References...", false, () => RegistryReferenceWindow.ShowReplace(Kind, row));
                    menu.AddSeparator(null);

                    string name = node.FullName;
                    string id = node.Id.ToString();
                    menu.AddItem("Copy Name", false, () => EditorGUIUtility.systemCopyBuffer = name);
                    menu.AddItem("Copy ID", false, () => EditorGUIUtility.systemCopyBuffer = id);

                    if (canEdit)
                    {
                        menu.AddSeparator(null);
                        menu.AddItem("Delete Attribute...", false, () => DeleteWithDialog(node.Id));
                    }
                    break;
                }
            }

            menu.DropDown(new Rect(_Event.mousePosition, Vector2.zero), _Event.element, DropdownMenuSizeMode.Auto);
        }

        /// <summary>
        /// The options menu, under the gear: tree commands, code generation, then reference repair.
        /// </summary>
        private void ShowOptionsMenu(VisualElement _Anchor)
        {
            var menu = new GenericDropdownMenu();

            menu.AddItem("Collapse All", false, () => Tree?.SetExpanded(false));
            menu.AddItem("Expand All", false, () => Tree?.SetExpanded(true));
            menu.AddSeparator(null);
            menu.AddItem("Generate Code", false, AttributeCommands.GenerateCode);
            menu.AddItem("Prune Unused Code Aliases", false, AttributeCommands.PruneCodeAliases);
            menu.AddSeparator(null);
            menu.AddItem("Relink Missing References...", false, AttributeCommands.RelinkMissingReferences);

            menu.DropDown(_Anchor.ChangeCoordinatesTo(_Anchor.panel.visualTree, _Anchor.contentRect), _Anchor, DropdownMenuSizeMode.Auto);
        }

        private static string Separator => GameplayAttribute.SEPARATOR.ToString();
        #endregion



        #region Commands
        /// <summary>
        /// Ask for a new attribute's name, starting with <paramref name="_Prefix"/>, e.g. "Vitals." for a group.
        /// </summary>
        private void ShowAdd(string _Prefix)
        {
            if (Kind.Snapshot.Main == null)
            {
                ShowMessage("Create the main attribute registry first.", HelpBoxMessageType.Error);
                return;
            }

            AddAttributePopup.Show(addButton, GetAddPopupOffset(), ADD_POPUP_WIDTH, _Prefix, name =>
            {
                // A refused name is shown in the popup, which stays open.
                var result = AttributeOperations.Add(name);
                if (result.Success)
                {
                    ShowMessage($"New attribute added: \"{name}\". Not saved yet.", HelpBoxMessageType.Info);
                    SelectKey(AttributeTreeNode.KeyOf(Kind.Snapshot.Main.Id, result.Id));
                }

                return result;
            });
        }

        /// <summary>
        /// Offset from the Add button's bottom-left corner (where <see cref="AnchoredPopup"/> places the popup) that
        /// puts the popup's top-right corner at the tree's top-right corner, inset by <see cref="ADD_POPUP_TREE_INSET"/>.
        /// </summary>
        private Vector2 GetAddPopupOffset()
        {
            var tree = treeScrollView.worldBound;
            var button = addButton.worldBound;
            if (float.IsNaN(tree.width) || float.IsNaN(button.width) || tree.width <= 0f)
                return ADD_POPUP_OFFSET;

            float x = tree.xMax - ADD_POPUP_TREE_INSET.x - ADD_POPUP_WIDTH;
            float y = tree.yMin + ADD_POPUP_TREE_INSET.y;
            return new Vector2(x - button.xMin, y - button.yMax);
        }

        private void DeleteWithDialog(RegistryGuid _Id)
        {
            var plan = AttributeOperations.PlanDelete(_Id, true);
            if (plan == null) return;

            string ok = plan.ReferencingAssets.Count > 0 || plan.Dependents.Count > 0 ? "Delete Anyway" : "Delete";
            if (!EditorUtility.DisplayDialog("Delete Gameplay Attribute", plan.Message, ok, "Cancel")) return;

            string name = plan.Name;
            EditorApplication.delayCall += () => Report(AttributeOperations.Delete(_Id), $"Attribute deleted: \"{name}\". Not saved yet.");
        }

        /// <summary>
        /// Show a text field over the row, and rename the attribute's last segment with its value. <br/>
        /// Enter commits, Escape or clicking elsewhere cancels.
        /// </summary>
        /// <remarks>
        /// As the tag tree's rename: keys are handled on key down, and focus is given a frame later, after the
        /// context menu that opened the field has closed.
        /// </remarks>
        private void Rename(VisualElement _Element, AttributeTreeNode _Node)
        {
            if (_Element == null || _Node == null) return;

            // Only one rename at a time.
            _Element.panel?.visualTree.Query<TextField>(className: RENAME_FIELD_CLASS).ForEach(e => e.RemoveFromHierarchy());

            var field = new TextField { value = _Node.ShortName };
            field.AddToClassList(RENAME_FIELD_CLASS);

            bool hadFocus = false;
            bool finished = false;

            void Finish(bool _Commit)
            {
                if (finished) return;
                finished = true;

                string segment = field.value;
                field.RemoveFromHierarchy();
                if (!_Commit || segment == _Node.ShortName) return;

                var id = _Node.Id;
                string oldName = _Node.FullName;
                string newName = AttributeInspectorPanel.Combine(_Node.Group, segment);

                // Run outside the event: the operation reloads the registries and rebuilds the tree.
                EditorApplication.delayCall += () => Report(AttributeOperations.Rename(id, newName), $"Attribute renamed: \"{oldName}\" to \"{newName}\". Not saved yet.");
            }

            field.RegisterCallback<KeyDownEvent>(e =>
            {
                switch (e.keyCode)
                {
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        Finish(true);
                        e.StopImmediatePropagation();
                        return;

                    case KeyCode.Escape:
                        Finish(false);
                        e.StopImmediatePropagation();
                        return;
                }

                // Only characters valid in a segment.
                if (e.character == '\0' || char.IsControl(e.character) || AttributeInspectorPanel.IsSegmentChar(e.character)) return;

                e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);

            // Pasted text keeps only the characters of a segment.
            field.RegisterValueChangedCallback(e =>
            {
                string filtered = new(e.newValue.Where(AttributeInspectorPanel.IsSegmentChar).ToArray());
                if (filtered != e.newValue)
                    field.SetValueWithoutNotify(filtered);
            });

            field.RegisterCallback<FocusInEvent>(_ => hadFocus = true);

            // Clicking elsewhere cancels, once the field really had focus: the closing menu can move focus first.
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (hadFocus)
                    Finish(false);
            });

            _Element.Add(field);
            field.schedule.Execute(() =>
            {
                if (finished || field.panel == null) return;

                field.Focus();
                field.SelectAll();
            });
        }

        private void CreateMainRegistry()
        {
            var result = AttributeOperations.CreateMainRegistry();
            if (result.Success)
                SelectKey(AttributeTreeNode.KeyOfRegistry(result.Id));
            else
                ShowMessage(result.Error, HelpBoxMessageType.Error);

            Rebuild();
        }

        private static void SelectAsset(string _AssetPath)
        {
            var asset = string.IsNullOrEmpty(_AssetPath) ? null : AssetDatabase.LoadMainAssetAtPath(_AssetPath);
            if (asset == null) return;

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>
        /// Show the outcome of an operation.
        /// </summary>
        /// <returns> Whether the operation succeeded. </returns>
        private bool Report(RegistryResult _Result, string _SuccessMessage)
        {
            ShowMessage(_Result.Success ? _SuccessMessage : _Result.Error, _Result.Success ? HelpBoxMessageType.Info : HelpBoxMessageType.Error);
            return _Result.Success;
        }

        /// <summary>
        /// Show a message under the tree. An empty message hides the box.
        /// </summary>
        internal void ShowMessage(string _Message, HelpBoxMessageType _Type)
        {
            if (helpBox == null) return;

            helpBox.text = _Message ?? string.Empty;
            helpBox.messageType = _Type;
            helpBox.style.display = string.IsNullOrWhiteSpace(helpBox.text) ? DisplayStyle.None : DisplayStyle.Flex;
            helpBox.EnableInClassList("error", _Type == HelpBoxMessageType.Error);
            helpBox.EnableInClassList("warning", _Type == HelpBoxMessageType.Warning);
        }
        #endregion
    }
}
