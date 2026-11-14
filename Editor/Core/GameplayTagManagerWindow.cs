using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Registry;
using LMK.Editor.UIElements;
using LMK.Editor.Utilities;
using LMK.Editor.Styles;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Shows every loaded registry, and edits the main registry: a toolbar (Save, Discard, search, Add Tag,
    /// options), the tag tree, and an inspector panel for the selected tag. Edits are staged until saved, and Unity
    /// asks to save or discard them when the window closes (<see cref="RegistryWindow"/>). Offers to create the main
    /// registry when the project has none.
    /// </summary>
    internal sealed class GameplayTagManagerWindow : RegistryWindow
    {
        private const string UXML_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/GameplayTagManagerWindowUXML.uxml";

        private Switcher switcher;
        private GameplayTagControlPanel controlPanel;
        private GameplayTagManagerToolbar toolbar;
        private GameplayTagInspectorPanel inspector;

        // A registry to select once the GUI exists.
        private RegistryGuid pendingRegistrySelection;



        [MenuItem("Tools/LMK/Gameplay Tags/Gameplay Tag Editor", priority = 0)]
        public static void Open()
        {
            GetWindow<GameplayTagManagerWindow>("Gameplay Tags");
        }

        /// <summary>
        /// Open the window and select a registry's header.
        /// </summary>
        public static void OpenRegistry(RegistryGuid _RegistryId)
        {
            var window = GetWindow<GameplayTagManagerWindow>("Gameplay Tags");
            window.pendingRegistrySelection = _RegistryId;
            window.ApplyPendingSelection();
        }

        protected override void OnEnable()
        {
            TagRegistryService.OnSnapshotChanged += RefreshSwitcher;
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            TagRegistryService.OnSnapshotChanged -= RefreshSwitcher;
            base.OnDisable();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            LMKTheme.Apply(root);

            var container = LMKAssetUtility.Load<VisualTreeAsset>(UXML_PATH).CloneTree();
            container.style.flexGrow = 1;
            root.Add(container);

            switcher = root.Q<Switcher>("switcher");
            controlPanel = root.Q<GameplayTagControlPanel>("control-panel");

            toolbar = new GameplayTagManagerToolbar(controlPanel);
            root.Q("tree-panel").Insert(0, toolbar);

            inspector = new GameplayTagInspectorPanel();
            inspector.ParentClicked += parent => controlPanel.Select(parent.RegistryId, parent.Id);
            inspector.Report += controlPanel.ShowMessage;
            root.Q<CollapsiblePanel>("inspector").Add(inspector);

            controlPanel.SelectionChanged += inspector.SetNode;
            controlPanel.AddTagRequested += toolbar.ShowAddTag;

            root.Q<Button>("create-database-button").clicked += () =>
            {
                var result = TagOperations.CreateMainRegistry();
                if (!result.Success)
                    EditorUtility.DisplayDialog("Create Main Tag Registry", result.Error, "OK");

                RefreshSwitcher();
            };

            RefreshSwitcher();
            OnStateChanged();
            ApplyPendingSelection();
        }

        // The control panel is shown once the main registry exists.
        private void RefreshSwitcher()
        {
            switcher?.SwitchActive(TagRegistryService.Snapshot.Main == null ? 0 : 1);
        }

        protected override void OnStateChanged() => toolbar?.Refresh();

        private void ApplyPendingSelection()
        {
            if (pendingRegistrySelection.IsZero || controlPanel == null) return;

            var registryId = pendingRegistrySelection;
            pendingRegistrySelection = RegistryGuid.Zero;

            // A frame later: a window opened just now has no tree yet.
            controlPanel.schedule.Execute(() => controlPanel.SelectRegistry(registryId));
        }    }
}
