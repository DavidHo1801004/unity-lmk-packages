using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Registry;
using LMK.Editor.UIElements;
using LMK.Editor.Utilities;
using LMK.Editor.Windows;
using LMK.Runtime.GameplayTags;

using ToolbarButton = LMK.Editor.UIElements.ToolbarButton;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Toolbar of the Gameplay Tag Manager, above the tag tree: the registry toolbar (Save, Discard, lock), with search,
    /// Add Tag and options as its controls.
    /// </summary>
    internal sealed class GameplayTagManagerToolbar : VisualElement
    {
        private const string USS_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/GameplayTagManagerWindowUSS.uss";
        private const string GEAR_ICON_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/Sprites/gear_white_32.png";

        private const string USS_CLASS = "gameplay-tag-toolbar";

        private const float POPUP_WIDTH = 260f;
        // Fallback offset from the Add Tag button, while the tree view has no layout.
        private static readonly Vector2 POPUP_OFFSET = new(0f, 2f);
        // Inset of the popup's top-right corner from the tree view's top-right corner: x to the left, y down.
        private static readonly Vector2 POPUP_TREE_INSET = new(8f, 8f);

        private readonly GameplayTagControlPanel controlPanel;

        private readonly RegistryToolbar registryToolbar;
        private readonly ToolbarButton addTagButton;
        private readonly ToolbarButton optionsButton;



        public GameplayTagManagerToolbar(GameplayTagControlPanel _ControlPanel)
        {
            controlPanel = _ControlPanel;
            name = "toolbar";
            AddToClassList(USS_CLASS);

            registryToolbar = new RegistryToolbar();
            registryToolbar.style.flexGrow = 1;
            Add(registryToolbar);

            // Unity's own toolbar search field.
            var search = new ToolbarSearchField { name = "search-field" };
            search.RegisterValueChangedCallback(e => controlPanel.SetFilter(e.newValue));
            registryToolbar.AddControl(search);

            addTagButton = new ToolbarButton(false) { name = "add-tag-button", text = "Add Tag", tooltip = "Add a tag to the main registry." };
            addTagButton.clicked += () => ShowAddTag(string.Empty);
            registryToolbar.AddControl(addTagButton);

            optionsButton = new ToolbarButton(false)
            {
                name = "options-button",
                iconImage = LMKAssetUtility.Load<Texture2D>(GEAR_ICON_PATH),
                tooltip = "Options",
            };
            optionsButton.clicked += () => controlPanel.ShowGeneralMenu(optionsButton);
            registryToolbar.AddControl(optionsButton);

            Refresh();
        }

        /// <summary>
        /// Enable Save and Discard while there are unsaved changes, and show the lock.
        /// </summary>
        public void Refresh() => registryToolbar.Refresh();



        #region Add Tag
        /// <summary>
        /// Open the Add Tag popup in the top-right corner of the tag tree. Enter adds the tag; Esc or clicking elsewhere closes it.
        /// </summary>
        /// <param name="_Prefix"> Text to start with, e.g. "Parent." for a sub tag. </param>
        public void ShowAddTag(string _Prefix)
        {
            var content = new VisualElement { name = "add-tag-popup" };
            var styleSheet = LMKAssetUtility.Load<StyleSheet>(USS_PATH);
            if (styleSheet != null)
                content.styleSheets.Add(styleSheet);

            var field = new PlaceholderTextField { name = "add-tag-field", label = "Name", PlaceholderText = "X.Y.Z" };
            field.SetValueWithoutNotify(_Prefix ?? string.Empty);
            content.Add(field);

            var error = new Label { name = "add-tag-error" };
            error.style.display = DisplayStyle.None;
            content.Add(error);

            var popup = AnchoredPopup.Show(addTagButton, content, GetPopupOffset(), POPUP_WIDTH);
            if (popup == null) return;

            // Keep a prefix, with the caret after it, when the popup focuses the field.
            if (!string.IsNullOrEmpty(_Prefix))
            {
                field.textSelection.selectAllOnFocus = false;
                field.textSelection.selectAllOnMouseUp = false;
                field.schedule.Execute(() => field.SelectRange(field.value.Length, field.value.Length)).ExecuteLater(50);
            }

            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    e.StopImmediatePropagation();
                    if (TryAdd(field.value, error))
                        popup.Close();
                    return;
                }

                // Only characters valid in a tag path.
                if (e.character == '\0' || char.IsControl(e.character) || e.character == TagPath.SEPARATOR || TagPath.IsValidSegmentChar(e.character))
                    return;

                e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);

            field.RegisterValueChangedCallback(_ =>
            {
                error.style.display = DisplayStyle.None;
                field.RemoveFromClassList("warning");
            });
        }

        /// <summary>
        /// Offset from the Add Tag button's bottom-left corner (where <see cref="AnchoredPopup"/> places the popup)
        /// that puts the popup's top-right corner at the tree view's top-right corner, inset by <see cref="POPUP_TREE_INSET"/>.
        /// </summary>
        private Vector2 GetPopupOffset()
        {
            var tree = controlPanel.TagTreeView.worldBound;
            var button = addTagButton.worldBound;
            if (float.IsNaN(tree.width) || float.IsNaN(button.width) || tree.width <= 0f)
                return POPUP_OFFSET;

            float x = tree.xMax - POPUP_TREE_INSET.x - POPUP_WIDTH;
            float y = tree.yMin + POPUP_TREE_INSET.y;
            return new Vector2(x - button.xMin, y - button.yMax);
        }

        private bool TryAdd(string _Path, Label _Error)
        {
            _Path = _Path?.Trim() ?? string.Empty;
            var result = TagOperations.Add(_Path);
            if (!result.Success)
            {
                _Error.text = string.IsNullOrEmpty(_Path) ? "Enter a tag path, e.g. Status.Debuff.Stun." : result.Error;
                _Error.style.display = DisplayStyle.Flex;
                _Error.parent?.Q<PlaceholderTextField>()?.AddToClassList("warning");
                return false;
            }

            controlPanel.ShowMessage($"New tag added: \"{_Path}\". Not saved yet.", HelpBoxMessageType.Info);

            var main = TagRegistryService.Snapshot.Main;
            if (main != null)
                controlPanel.Select(main.Id, result.Id);

            return true;
        }
        #endregion
    }
}
