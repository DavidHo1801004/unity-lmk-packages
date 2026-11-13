using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;
using LMK.Editor.Utilities;
using LMK.Editor.Styles;
using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Base control panel element for viewing and editing gameplay tags. <br/>
    /// Shows every loaded registry. With <see cref="Editable"/>, its context menus stage edits of the main registry
    /// through <see cref="TagOperations"/>.
    /// </summary>
    [UxmlElement("GameplayTagControlPanel", libraryPath = "GASU/Gameplay Tag Control Panel", visibility = LibraryVisibility.Visible)]
    public partial class GameplayTagControlPanel : VisualElement
    {
        private const string UXML_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/GameplayTagControlPanelUXML.uxml";

        private const string USS_CLASS_NO_HEADER = "gameplay-tag-control-panel--no-header";

        private const string RENAME_FIELD_CLASS = "tag-rename-field";

        private event Action<TagElementSelectCallback, bool> OnSelectBoxCallback;
        private event Action OnTagTreeRedrawn;

        /// <summary>
        /// Called when the selected row changes, or its node is rebuilt after the registries changed. The node is
        /// <see langword="null"/> when nothing is selected. Only with <see cref="RowSelection"/>.
        /// </summary>
        internal event Action<GameplayTagNode> SelectionChanged;

        /// <summary>
        /// Called when a context menu asks to add a tag, with the path to start from ("" or "Parent.").
        /// Only with <see cref="Editable"/>.
        /// </summary>
        internal event Action<string> AddTagRequested;



        private readonly VisualElement content;
        private readonly VisualElement header;
        private readonly SearchBar searchBar;
        private readonly Button generalControlsButton;

        private readonly ScrollView tagTreeScrollView;
        private readonly Label emptyDataDisplay;
        private readonly HelpBox helpBox;

        private readonly GameplayTagTreeOptions treeOptions = new();

        private string filter = string.Empty;
        private string selectedKey;

        /// <summary>
        /// The displayed tag tree. Replaced every time the tree is redrawn.
        /// </summary>
        internal GameplayTagTreeElement TagTree { get; private set; }

        /// <summary>
        /// The scroll view holding <see cref="TagTree"/>.
        /// </summary>
        internal VisualElement TagTreeView => tagTreeScrollView;

        /// <summary>
        /// The selected node, or <see langword="null"/>.
        /// </summary>
        internal GameplayTagNode SelectedNode { get; private set; }

        private bool showHeader = true;
        /// <summary>
        /// Whether the panel shows its own search bar and options button. Turn it off when the host has its own.
        /// </summary>
        [UxmlAttribute(name = "show-header")]
        public bool ShowHeader
        {
            get => showHeader;
            set
            {
                showHeader = value;
                header.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                content.EnableInClassList(USS_CLASS_NO_HEADER, !value);
            }
        }

        /// <summary>
        /// Whether context menus offer edits of the main registry. Edits are staged until they are saved in the
        /// Gameplay Tag Manager, so only the manager edits.
        /// </summary>
        [UxmlAttribute(name = "editable")]
        public bool Editable { get; set; }

        private bool rowSelection;
        /// <summary>
        /// Whether clicking a row selects it, see <see cref="SelectionChanged"/>.
        /// </summary>
        [UxmlAttribute(name = "row-selection")]
        public bool RowSelection
        {
            get => rowSelection;
            set
            {
                rowSelection = value;
                if (!value)
                    SetSelection(null);
            }
        }

        private MessageBoxDisplay showHelpBoxMessage;
        /// <summary>
        /// Should the control panel display a help box message under tag tree.
        /// </summary>
        internal MessageBoxDisplay ShowHelpBoxMessage
        {
            get => showHelpBoxMessage;
            set
            {
                if (value == showHelpBoxMessage) return;

                showHelpBoxMessage = value;
                UpdateHelpBoxVisibility();
            }
        }



        public GameplayTagControlPanel() : base()
        {
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            treeOptions.ContextMenu = ShowTagContextMenu;
            treeOptions.SelectBoxChanged = (e, value) => OnSelectBoxCallback?.Invoke(e, value);
            treeOptions.RowClicked = e =>
            {
                if (rowSelection)
                    SetSelection(e.element);
            };

            content = LMKAssetUtility.Load<VisualTreeAsset>(UXML_PATH).CloneTree();
            LMKTheme.ApplyIfStandalone(this);
            content.style.flexGrow = 1;

            // Search bar and options
            header = content.Q("search-bar-controls");

            searchBar = content.Q<SearchBar>();
            searchBar.RegisterValueChangedCallback(e => SetFilter(e.newValue));

            generalControlsButton = content.Q<Button>("general-controls");
            generalControlsButton.clicked += () => ShowGeneralMenu(generalControlsButton);

            // Scrollable tag tree
            tagTreeScrollView = content.Q<ScrollView>("tag-tree-scroll-view");
            emptyDataDisplay = content.Q<Label>("empty-data-label");

            // Help box message
            helpBox = content.Q<HelpBox>("help-box-message");

            Add(content);

            RefreshTagTree();
        }

        // Static events would keep a detached panel alive, so they are only handled while attached.
        private void OnAttachToPanel(AttachToPanelEvent _Event)
        {
            TagRegistryService.OnSnapshotChanged += OnSnapshotChanged;
            TagOperations.MarkersChanged += OnMarkersChanged;
            RefreshTagTree();
        }

        private void OnDetachFromPanel(DetachFromPanelEvent _Event)
        {
            TagRegistryService.OnSnapshotChanged -= OnSnapshotChanged;
            TagOperations.MarkersChanged -= OnMarkersChanged;
        }



        #region Display Update
        private void OnSnapshotChanged() => RefreshTagTree();

        private void OnMarkersChanged() => TagTree?.SyncMarkers();

        internal void RefreshTagTree()
        {
            var snapshot = TagRegistryService.Snapshot;

            tagTreeScrollView.Clear();

            treeOptions.ShowSelectBox = OnSelectBoxCallback != null;

            TagTree = new GameplayTagTreeElement(snapshot.Root, treeOptions);
            tagTreeScrollView.Add(TagTree);

            if (!string.IsNullOrWhiteSpace(filter))
                TagTree.SetFilter(filter);

            emptyDataDisplay.style.display = snapshot.IsEmpty ? DisplayStyle.Flex : DisplayStyle.None;

            ShowRegistryProblems(snapshot);

            // The rebuilt tree has new nodes: select the same tag again, if it still exists.
            if (selectedKey != null)
                SetSelection(TagTree.Find(selectedKey), true);

            OnTagTreeRedrawn?.Invoke();
        }

        /// <summary>
        /// Warn about registries edited outside the manager, rejected files, duplicate registry IDs, and a
        /// main registry changed on disk under staged edits.
        /// </summary>
        private void ShowRegistryProblems(TagRegistrySnapshot _Snapshot)
        {
            var problems = new List<string>(_Snapshot.Issues);
            foreach (var registry in _Snapshot.Registries)
            {
                if (registry.UsingLastGood)
                    problems.Add($"\"{registry.AssetPath}\" is invalid, so its last loaded version is shown. {string.Join(" ", registry.Errors)}");
                else if (registry.ChecksumMismatch)
                    problems.Add($"\"{registry.AssetPath}\" was edited outside the Gameplay Tag Manager (checksum mismatch).");
            }

            if (TagOperations.DiskChangedWhilePending)
                problems.Add($"\"{TagRegistryService.MainRegistryPath}\" changed on disk while you have unsaved changes. Discard them to load it.");

            // Packages and imports waiting for the lock.
            problems.AddRange(TagRegistryService.InactiveRegistries);

            foreach (var (registry, version) in PackageRegistryService.FindOutdated(_Snapshot))
                problems.Add($"\"{registry.Data.package}\" is now {version}, but \"{registry.AssetPath}\" was generated from {registry.Data.version}. " +
                             "Right-click the registry to regenerate it.");

            if (Editable && ProjectAssetImport.HasPending)
                problems.Add("Imported assets are waiting to have their tags mapped. Use Map Imported Assets in the options menu.");

            if (problems.Count > 0)
                ShowMessage(string.Join("\n", problems), HelpBoxMessageType.Warning);
        }
        #endregion

        #region Selection
        /// <summary>
        /// Select a tag, expand its parents and scroll to it. Only with <see cref="RowSelection"/>.
        /// </summary>
        /// <returns> Whether the tag is in the tree. </returns>
        internal bool Select(RegistryGuid _RegistryId, RegistryGuid _TagId)
            => SelectKey($"{_RegistryId}:{_TagId}");

        /// <summary>
        /// Select a registry's header row. Only with <see cref="RowSelection"/>.
        /// </summary>
        /// <returns> Whether the registry is in the tree. </returns>
        internal bool SelectRegistry(RegistryGuid _RegistryId)
            => SelectKey($"registry:{_RegistryId}");

        private bool SelectKey(string _Key)
        {
            if (!rowSelection || TagTree == null) return false;

            var element = TagTree.Find(_Key);
            if (element == null) return false;

            element.Reveal();
            SetSelection(element);

            // Scroll once the expanded rows have a layout.
            tagTreeScrollView.schedule.Execute(() =>
            {
                if (element.panel != null)
                    tagTreeScrollView.ScrollTo(element.Row);
            });

            return true;
        }

        private GameplayTagNodeElement selectedElement;

        private void SetSelection(GameplayTagNodeElement _Element, bool _Rebuilt = false)
        {
            if (_Element == selectedElement && !_Rebuilt) return;

            selectedElement?.SetRowSelected(false);
            selectedElement = _Element;
            selectedElement?.SetRowSelected(true);

            SelectedNode = _Element?.TagNode;
            selectedKey = SelectedNode?.Key;

            SelectionChanged?.Invoke(SelectedNode);
        }
        #endregion

        #region Callbacks
        /// <summary>
        /// Register a callback on any tag element select box value changed.
        /// </summary>
        internal void RegisterTagSelectBoxCallback(Action<TagElementSelectCallback, bool> _Callback)
        {
            OnSelectBoxCallback += _Callback;

            RefreshTagTree();
        }

        /// <summary>
        /// Unregister a callback on any tag element select box value changed.
        /// </summary>
        internal void UnregisterTagSelectBoxCallback(Action<TagElementSelectCallback, bool> _Callback)
        {
            OnSelectBoxCallback -= _Callback;

            RefreshTagTree();
        }

        /// <summary>
        /// Register a callback tag tree redrawn.
        /// </summary>
        internal void RegisterTagTreeRedrawnCallback(Action _Callback)
        {
            OnTagTreeRedrawn += _Callback;
        }

        /// <summary>
        /// Unregister a callback tag tree redrawn.
        /// </summary>
        internal void UnregisterTagTreeRedrawnCallback(Action _Callback)
        {
            OnTagTreeRedrawn -= _Callback;
        }

        /// <summary>
        /// Show only the tags matching <paramref name="_Filter"/>, see <see cref="SearchQuery"/> for the syntax.
        /// </summary>
        internal void SetFilter(string _Filter)
        {
            filter = _Filter ?? string.Empty;
            TagTree?.SetFilter(filter);
        }



        /// <summary>
        /// Context menu for tag element.
        /// </summary>
        private void ShowTagContextMenu(TagElementSelectCallback _Callback)
        {
            var node = _Callback.tagNode;
            bool canEdit = Editable && !node.IsReadOnly;
            GenericDropdownMenu menu = new();

            if (node.IsRegistryHeader)
            {
                if (canEdit)
                {
                    menu.AddItem("Add Tag", false, () => AddTagRequested?.Invoke(string.Empty));
                    menu.AddItem("Import Tag Files...", false, GameplayTagCommands.ImportTagFiles);
                }

                var registryId = node.RegistryId;
                menu.AddItem("Export To Tag File...", false, () => ExportRegistry(registryId));

                // A generated registry: regenerate it from its installed package.
                var registry = TagRegistryService.Snapshot.Find(registryId);
                if (Editable && registry?.Kind == TagRegistryKind.Generated && !string.IsNullOrEmpty(registry.Data.package))
                {
                    var package = RegistryPackage.From(UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                        .FirstOrDefault(e => e.name == registry.Data.package));

                    if (package != null)
                        menu.AddItem("Regenerate From Package...", false, () => GameplayTagCommands.GeneratePackageRegistryWithDialog(package));
                }
            }
            else
            {
                if (canEdit)
                {
                    menu.AddItem("Add Sub Tag", false, () => AddTagRequested?.Invoke(node.FullTag + TagPath.SEPARATOR));
                    if (!node.IsExplicit)
                        menu.AddItem("Make Explicit", false, () => MakeExplicit(node));
                    menu.AddSeparator(null);
                    menu.AddItem("Rename Tag", false, () => RenameTag(_Callback.element, node));
                    menu.AddItem(node.ChildCount > 0 ? "Delete Tag And Sub Tags" : "Delete Tag", false, () => DeleteTag(node));
                    menu.AddSeparator(null);
                }

                // Reference tools write assets, not registries, so they work for tags of any registry.
                if (Editable)
                {
                    var tag = node.Tag;
                    menu.AddItem("Find References", false, () => RegistryReferenceWindow.ShowReferences(TagKind.Instance, (tag.RegistryId, tag.Id)));
                    menu.AddItem("Replace References...", false, () => RegistryReferenceWindow.ShowReplace(TagKind.Instance, (tag.RegistryId, tag.Id)));
                    menu.AddSeparator(null);
                }

                menu.AddItem("Copy To Clipboard", false, () => GUIUtility.systemCopyBuffer = node.FullTag);
            }

            menu.DropDown(
                new Rect(_Callback.mousePosition, Vector2.zero),
                _Callback.element,
                DropdownMenuSizeMode.Auto);
        }

        /// <summary>
        /// The options menu, under <paramref name="_Anchor"/>. Tree commands, then registry commands when
        /// <see cref="Editable"/>, or a way to the Gameplay Tag Manager otherwise.
        /// </summary>
        internal void ShowGeneralMenu(VisualElement _Anchor)
        {
            GenericDropdownMenu menu = new();

            menu.AddItem("Collapse All", false, () => TagTree?.SetExpanded(false));
            menu.AddItem("Expand All", false, () => TagTree?.SetExpanded(true));
            menu.AddSeparator(null);

            if (Editable)
            {
                menu.AddItem("Import Tag Files...", false, GameplayTagCommands.ImportTagFiles);
                menu.AddItem("Export Main Registry To Tag File...", false, GameplayTagCommands.ExportMainRegistry);
                menu.AddSeparator(null);
                menu.AddItem("Generate Code", false, GameplayTagCommands.GenerateCode);
                menu.AddItem("Prune Unused Code Aliases", false, GameplayTagCommands.PruneCodeAliases);
                menu.AddSeparator(null);
                menu.AddItem("Relink Missing References...", false, GameplayTagCommands.RelinkMissingReferences);
                menu.AddItem("Rebuild Reference Index", false, GameplayTagCommands.RebuildReferenceIndex);

                if (ProjectAssetImport.HasPending)
                    menu.AddItem("Map Imported Assets", false, GameplayTagCommands.MapImportedAssets);
            }
            else
            {
                menu.AddItem("Open Tag Manager", false, GameplayTagManagerWindow.Open);
            }

            menu.DropDown(
                _Anchor.ChangeCoordinatesTo(_Anchor.panel.visualTree, _Anchor.contentRect),
                _Anchor,
                DropdownMenuSizeMode.Auto);
        }
        #endregion

        #region Tag Controls
        private void MakeExplicit(GameplayTagNode _Node)
        {
            Report(TagOperations.MakeExplicit(_Node.Id), $"Tag made explicit: \"{_Node.FullTag}\"");
        }

        /// <summary>
        /// Delete a tag. A tag with children is deleted with its whole subtree. Implicit parents left without
        /// children are removed too, unless the user keeps them. The dialog lists the assets that reference them.
        /// </summary>
        private void DeleteTag(GameplayTagNode _Node)
        {
            string path = _Node.FullTag;
            var result = GameplayTagCommands.DeleteWithDialog(_Node.Id);

            if (result.HasValue)
                Report(result.Value, $"Tag deleted: \"{path}\"");
        }

        private void ExportRegistry(RegistryGuid _RegistryId)
        {
            var registry = TagRegistryService.Snapshot.Find(_RegistryId);
            string path = TagFileExport.ExportWithDialog(registry);
            if (path != null)
                ShowMessage($"Exported \"{registry.Name}\" to \"{path}\".", HelpBoxMessageType.Info);
        }

        /// <summary>
        /// Show a text field over the tag element, and rename the tag's last segment with its value. <br/>
        /// Enter commits, Escape or clicking elsewhere cancels.
        /// </summary>
        /// <remarks>
        /// Enter and Escape are handled on key down, before the text field reacts: a single-line field can lose
        /// focus on Enter, and the focus-out would otherwise remove the field before a key up arrives. Focus is
        /// given one frame later, after the context menu that opened the field has closed and settled focus.
        /// </remarks>
        private void RenameTag(VisualElement _Element, GameplayTagNode _Node)
        {
            if (_Element == null || _Node == null) return;

            // Only one rename at a time.
            _Element.panel?.visualTree.Query<TextField>(className: RENAME_FIELD_CLASS).ForEach(e => e.RemoveFromHierarchy());

            TextField textField = new();
            textField.AddToClassList(RENAME_FIELD_CLASS);
            textField.value = _Node.ShortTag;

            bool hadFocus = false;
            bool finished = false;

            void Finish(bool _Commit)
            {
                if (finished) return;
                finished = true;

                string newSegment = textField.value;
                textField.RemoveFromHierarchy();

                if (!_Commit || newSegment == _Node.ShortTag) return;

                // Run outside the event: the operation reloads the registries and rebuilds this tree.
                string oldPath = _Node.FullTag;
                var id = _Node.Id;
                EditorApplication.delayCall += () => Report(
                    TagOperations.Rename(id, newSegment),
                    $"Tag renamed: \"{oldPath}\" to \"{TagPath.Combine(TagPath.GetParent(oldPath), newSegment)}\"");
            }

            textField.RegisterCallback<KeyDownEvent>((e) =>
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

                // Only accept characters valid in a segment.
                if (char.IsControl(e.character) || TagPath.IsValidSegmentChar(e.character)) return;

                e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);

            textField.RegisterCallback<FocusInEvent>(_ => hadFocus = true);

            // Clicking elsewhere cancels, but only once the field really had focus: the closing context menu
            // can move focus around before that.
            textField.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (hadFocus)
                    Finish(false);
            });

            _Element.Add(textField);
            textField.schedule.Execute(() =>
            {
                if (finished || textField.panel == null) return;

                textField.Focus();
                textField.SelectAll();
            });
        }


        /// <summary>
        /// Show the result of an operation.
        /// </summary>
        /// <returns> Whether the operation succeeded. </returns>
        private bool Report(RegistryResult _Result, string _SuccessMessage)
        {
            if (_Result.Success)
                ShowMessage(_SuccessMessage, HelpBoxMessageType.Info);
            else
                ShowMessage(_Result.Error, HelpBoxMessageType.Error);

            return _Result.Success;
        }
        #endregion

        #region Help Box Message
        internal void ShowMessage(string _Message, HelpBoxMessageType _Type)
        {
            helpBox.text = _Message;
            helpBox.messageType = _Type;

            UpdateHelpBoxVisibility();

            switch (_Type)
            {
                case HelpBoxMessageType.Error:
                    helpBox.RemoveFromClassList("warning");
                    helpBox.AddToClassList("error");
                    break;

                case HelpBoxMessageType.Warning:
                    helpBox.RemoveFromClassList("error");
                    helpBox.AddToClassList("warning");
                    break;

                case HelpBoxMessageType.Info:
                default:
                    helpBox.RemoveFromClassList("error");
                    helpBox.RemoveFromClassList("warning");
                    break;
            }
        }

        private void UpdateHelpBoxVisibility()
        {
            switch (showHelpBoxMessage)
            {
                case MessageBoxDisplay.Always:
                    helpBox.style.display = DisplayStyle.Flex;
                    break;

                case MessageBoxDisplay.Never:
                    helpBox.style.display = DisplayStyle.None;
                    break;

                case MessageBoxDisplay.IfValid:
                    helpBox.style.display = string.IsNullOrWhiteSpace(helpBox.text) ? DisplayStyle.None : DisplayStyle.Flex;
                    break;

                default: break;
            }
        }
        #endregion
    }



    /// <summary>
    /// Define how help box messages are displayed.
    /// </summary>
    public enum MessageBoxDisplay
    {
        // Invalid.
        // Set as default value to avoid update conflict.
        None,

        // Always display message box.
        Always,

        // Display message box when a valid message is present.
        IfValid,

        // Never display message box.
        Never,
    }
}
