using System.Collections.Generic;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;
using LMK.Runtime.GameplayTags;
using LMK.Editor.Utilities;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Custom inspector display for <see cref="GameplayTagContainer"/>.
    /// </summary>
    /// <remarks>
    /// While any selected object holds missing tags, a click offers only "Remove N missing tag(s)".
    /// Otherwise it opens the picker. The picker's changes are applied to each selected object as a diff (append added
    /// tags, remove removed ones), so untouched entries keep their indices. When the selected objects hold different
    /// tags, the field shows "- Mixed -" and the picker starts with the tags they all hold.
    /// </remarks>
    [CustomPropertyDrawer(typeof(GameplayTagContainer))]
    internal sealed class GameplayTagContainerDrawer : PropertyDrawer
    {
        private const string UXML_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/GameplayTagContainerUXML.uxml";

        private const string USS_CLASS = "gameplay-tag-container-field";

        private const string EMPTY_TEXT = "Empty";
        private const string UNDO_EDIT = "Edit Gameplay Tag Container";
        private const string UNDO_CLEAR = "Clear Gameplay Tag Container";



        public override VisualElement CreatePropertyGUI(SerializedProperty _Property)
        {
            // Drawers are shared between properties, so all state lives in this method.
            SerializedProperty tagsProperty = _Property.FindPropertyRelative(GameplayTagDrawerHelper.CONTAINER_TAGS_PROPERTY);
            if (tagsProperty == null || !tagsProperty.isArray) return base.CreatePropertyGUI(_Property);

            VisualTreeAsset visualTree = LMKAssetUtility.Load<VisualTreeAsset>(UXML_PATH);
            if (visualTree == null) return base.CreatePropertyGUI(_Property);

            var root = GameplayTagDrawerBinding.CreateField(_Property.displayName, visualTree.CloneTree(), USS_CLASS, true);

            string propertyPath = _Property.propertyPath;
            var serializedObject = _Property.serializedObject;

            var box = root.Q<VisualElement>("main-container");
            var contentSwitcher = root.Q<Switcher>("tag-container-switcher");
            var contentContainer = root.Q<VisualElement>("tag-container-content");
            var placeholder = root.Q<Label>("empty-tag-label");

            void RefreshDisplay()
            {
                if (!GameplayTagDrawerHelper.IsAlive(tagsProperty)) return;

                PopulateTagDisplays(serializedObject.targetObjects, propertyPath, contentSwitcher, contentContainer, placeholder, OnRemoveTag);
            }

            // Writes go to each target on its own, then the inspector's serialized object reads them back.
            void AfterWrite()
            {
                if (!GameplayTagDrawerHelper.IsAlive(tagsProperty)) return;

                serializedObject.Update();
                RefreshDisplay();
            }

            void OnRemoveTag(GameplayTag _Tag)
            {
                if (!GameplayTagDrawerHelper.IsAlive(tagsProperty)) return;

                GameplayTagDrawerHelper.ApplyChange(serializedObject.targetObjects, propertyPath, null, new[] { _Tag }, UNDO_EDIT);
                AfterWrite();
            }

            contentSwitcher.OnAttachedToPanel += RefreshDisplay;

            GameplayTagDrawerBinding.BindRefresh(root, tagsProperty, RefreshDisplay);

            GameplayTagDrawerBinding.BindClick(box, _Property, true, AfterWrite, () =>
            {
                var targets = serializedObject.targetObjects;

                // The picker shows the tags every target holds. Each change is applied to every target as a diff from
                // the previous selection, so tags only some targets hold stay as they are.
                var lastSelection = GameplayTagDrawerHelper.CommonTags(targets, propertyPath);

                // Mixed, Clear must also remove the tags only some targets hold, which the picker doesn't show.
                bool mixed = targets.Length > 1 && GameplayTagDrawerHelper.HasMixedTags(targets, propertyPath);

                GameplayTagSelectionWindow.Open(
                    _GUILocation: new Vector2(box.worldBound.xMin, box.worldBound.yMax),
                    _WindowLabel: "GAMEPLAY TAG CONTAINER",
                    _SingleSelection: false,
                    _SelectedTags: lastSelection,
                    _OnSelectionChanged: (tags) =>
                    {
                        if (!GameplayTagDrawerHelper.IsAlive(tagsProperty)) return;

                        var selection = new List<GameplayTag>(tags);
                        var added = selection.FindAll(t => !lastSelection.Contains(t));
                        var removed = lastSelection.FindAll(t => !selection.Contains(t));
                        lastSelection = selection;

                        GameplayTagDrawerHelper.ApplyChange(targets, propertyPath, added, removed, UNDO_EDIT);
                        AfterWrite();
                    },
                    _OnCleared: !mixed ? null : () =>
                    {
                        if (!GameplayTagDrawerHelper.IsAlive(tagsProperty)) return;

                        lastSelection = new List<GameplayTag>();

                        GameplayTagDrawerHelper.RemoveAllLive(targets, propertyPath, UNDO_CLEAR);
                        AfterWrite();
                    });
            });

            // Compact list: only offered when None entries exist (left by prefab-safe removal).
            box.RegisterCallback<ContextClickEvent>((e) =>
            {
                if (!GameplayTagDrawerHelper.IsAlive(_Property)) return;

                serializedObject.Update();
                if (!GameplayTagDrawerHelper.HasNoneEntries(_Property)) return;

                var targets = serializedObject.targetObjects;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Compact List"), false, () => CompactWithWarning(targets, propertyPath));
                menu.ShowAsContext();

                e.StopPropagation();
            });

            return root;
        }



        #region Serialized Tags
        private static void CompactWithWarning(Object[] _Targets, string _PropertyPath)
        {
            bool prefabAsset = false;
            foreach (var target in _Targets)
                prefabAsset |= GameplayTagDrawerHelper.IsPrefabAssetContext(target);

            if (prefabAsset && !EditorUtility.DisplayDialog(
                    "Compact Gameplay Tag List",
                    "This list belongs to a prefab asset. Removing entries shifts the indices that prefab instances " +
                    "and variants use for their overrides, so those overrides may land on different tags.",
                    "Compact Anyway",
                    "Cancel"))
                return;

            GameplayTagDrawerHelper.Compact(_Targets, _PropertyPath);
        }
        #endregion

        #region Tag Value Display
        /// <summary>
        /// Show the targets' tags, "Empty", or "- Mixed -" when the targets hold different tags. Missing tags are
        /// shown too, as "(deleted)".
        /// </summary>
        private static void PopulateTagDisplays(
            Object[] _Targets,
            string _PropertyPath,
            Switcher _ContentSwitcher,
            VisualElement _ContentContainer,
            Label _Placeholder,
            System.Action<GameplayTag> _OnRemove)
        {
            _ContentContainer.Clear();

            if (_Targets.Length > 1 && GameplayTagDrawerHelper.HasMixedTags(_Targets, _PropertyPath))
            {
                _Placeholder.text = GameplayTagDrawerHelper.MIXED_TEXT;
                _Placeholder.tooltip = GameplayTagDrawerHelper.MIXED_TOOLTIP;
                _ContentSwitcher.SwitchActive(0);
                return;
            }

            _Placeholder.text = EMPTY_TEXT;
            _Placeholder.tooltip = string.Empty;

            // None entries are placeholders kept for prefab overrides: not shown.
            var tags = _Targets.Length > 0 ? GameplayTagDrawerHelper.ReadAssigned(_Targets[0], _PropertyPath) : new List<GameplayTag>();
            foreach (var tag in tags)
                _ContentContainer.Add(CreateTagValueElement(tag, _OnRemove));

            _ContentSwitcher.SwitchActive(tags.Count > 0 ? 1 : 0);
        }

        private static VisualElement CreateTagValueElement(GameplayTag _Tag, System.Action<GameplayTag> _OnRemove)
        {
            VisualElement container = new() { name = "gameplay-tag__tag-value-container" };
            container.AddToClassList("gameplay-tag__tag-value-container");

            var tagValueClearButton = new Button()
            {
                name = "gameplay-tag__tag-value-clear-button",
                text = "x",
                tooltip = "Remove",
            };
            tagValueClearButton.AddToClassList("gameplay-tag__tag-value-clear-button");
            tagValueClearButton.clicked += () => _OnRemove(_Tag);
            container.Add(tagValueClearButton);

            var tagDisplay = new Label() { name = "gameplay-tag__tag-value" };
            tagDisplay.AddToClassList("gameplay-tag__tag-value");
            GameplayTagDrawerHelper.ApplyDisplay(tagDisplay, _Tag);
            container.Add(tagDisplay);

            return container;
        }
        #endregion
    }
}
