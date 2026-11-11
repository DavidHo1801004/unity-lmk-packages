using System.Collections.Generic;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;
using LMK.Runtime.GameplayTags;
using LMK.Editor.Utilities;
using LMK.Editor.Styles;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Custom inspector display for <see cref="GameplayTagContainer"/>.
    /// </summary>
    /// <remarks>
    /// While any selected object holds missing tags, a click offers only "Remove N missing tag(s)".
    /// Otherwise it opens the picker. The picker's changes are applied as a diff (append added tags, remove
    /// removed ones), so untouched entries keep their indices.
    /// </remarks>
    [CustomPropertyDrawer(typeof(GameplayTagContainer))]
    internal sealed class GameplayTagContainerDrawer : PropertyDrawer
    {
        private const string UXML_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/GameplayTagContainerUXML.uxml";



        public override VisualElement CreatePropertyGUI(SerializedProperty _Property)
        {
            // Drawers are shared between properties, so all state lives in this method.
            SerializedProperty tagsProperty = _Property.FindPropertyRelative(GameplayTagDrawerHelper.CONTAINER_TAGS_PROPERTY);
            if (tagsProperty == null || !tagsProperty.isArray) return base.CreatePropertyGUI(_Property);

            VisualTreeAsset visualTree = LMKAssetUtility.Load<VisualTreeAsset>(UXML_PATH);
            if (visualTree == null) return base.CreatePropertyGUI(_Property);

            var root = visualTree.CloneTree();
            LMKTheme.ApplyIfStandalone(root);

            string propertyPath = _Property.propertyPath;

            root.Q<Label>("property-label").text = _Property.displayName;

            var contentSwitcher = root.Q<Switcher>("tag-container-switcher");
            var contentContainer = root.Q<VisualElement>("tag-container-content");

            void RefreshDisplay()
            {
                if (!GameplayTagDrawerHelper.IsAlive(tagsProperty)) return;

                PopulateTagDisplays(tagsProperty, contentSwitcher, contentContainer);
            }

            contentSwitcher.OnAttachedToPanel += RefreshDisplay;

            // Undo, prefab overrides, other editors and registry changes can all change what is shown.
            root.TrackPropertyValue(tagsProperty, _ => RefreshDisplay());
            root.RegisterCallback<AttachToPanelEvent>(_ => TagRegistryService.OnSnapshotChanged += RefreshDisplay);
            root.RegisterCallback<DetachFromPanelEvent>(_ => TagRegistryService.OnSnapshotChanged -= RefreshDisplay);

            var tagContainer = root.Q<VisualElement>("tag-container");
            var mainContainer = root.Q<VisualElement>("main-container");

            mainContainer.RegisterCallback<PointerDownEvent>((e) =>
            {
                if (e.button != 0 || !GameplayTagDrawerHelper.IsAlive(tagsProperty)) return;

                // Clicking a tag's remove button should not open the window.
                if (e.target is Button) return;

                var targets = _Property.serializedObject.targetObjects;

                // While missing tags are present, the only action is to remove them.
                int missing = GameplayTagDrawerHelper.CountMissing(targets, propertyPath, true);
                if (missing > 0)
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent(GameplayTagDrawerHelper.GetRemoveMissingLabel(true, missing)), false, () =>
                    {
                        GameplayTagDrawerHelper.RemoveMissing(targets, propertyPath, true);
                        _Property.serializedObject.Update();
                        RefreshDisplay();
                    });
                    menu.ShowAsContext();
                    return;
                }

                GameplayTagSelectionWindow.Open(
                    _GUILocation: new Vector2(tagContainer.worldBound.xMin, mainContainer.worldBound.yMax),
                    _WindowLabel: "GAMEPLAY TAG CONTAINER",
                    _SingleSelection: false,
                    _SelectedTags: ReadAssigned(tagsProperty),
                    _OnSelectionChanged: (tags) =>
                    {
                        if (!GameplayTagDrawerHelper.IsAlive(tagsProperty)) return;

                        tagsProperty.serializedObject.Update();
                        ApplySelection(tagsProperty, tags);
                        RefreshDisplay();
                    });
            });

            // Compact list: only offered when None entries exist (left by prefab-safe removal).
            mainContainer.RegisterCallback<ContextClickEvent>((e) =>
            {
                if (!GameplayTagDrawerHelper.IsAlive(_Property)) return;

                _Property.serializedObject.Update();
                if (!GameplayTagDrawerHelper.HasNoneEntries(_Property)) return;

                var targets = _Property.serializedObject.targetObjects;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Compact List"), false, () => CompactWithWarning(targets, propertyPath));
                menu.ShowAsContext();

                e.StopPropagation();
            });

            return root;
        }



        #region Serialized Tags
        private static List<GameplayTag> ReadAssigned(SerializedProperty _TagsProperty)
        {
            var result = new List<GameplayTag>();
            foreach (var tag in GameplayTagDrawerHelper.ReadList(_TagsProperty))
                if (!tag.IsNone)
                    result.Add(tag);

            return result;
        }

        /// <summary>
        /// Apply the picker's selection as a diff: remove deselected entries (highest index first) and append
        /// newly selected ones. Missing and None entries are never touched here.
        /// </summary>
        private static void ApplySelection(SerializedProperty _TagsProperty, IReadOnlyList<GameplayTag> _Selection)
        {
            var selected = new HashSet<GameplayTag>(_Selection);
            var stored = GameplayTagDrawerHelper.ReadList(_TagsProperty);

            for (int i = stored.Count - 1; i >= 0; i--)
                if (!stored[i].IsNone && stored[i].IsLive && !selected.Contains(stored[i]))
                    _TagsProperty.DeleteArrayElementAtIndex(i);

            var present = new HashSet<GameplayTag>(stored);
            foreach (var tag in _Selection)
            {
                if (tag.IsNone || present.Contains(tag)) continue;

                _TagsProperty.arraySize++;
                GameplayTagDrawerHelper.Write(_TagsProperty.GetArrayElementAtIndex(_TagsProperty.arraySize - 1), tag);
            }

            _TagsProperty.serializedObject.ApplyModifiedProperties();
        }

        private static void RemoveTagAt(SerializedProperty _TagsProperty, int _Index)
        {
            _TagsProperty.serializedObject.Update();
            if (_Index < 0 || _Index >= _TagsProperty.arraySize) return;

            _TagsProperty.DeleteArrayElementAtIndex(_Index);
            _TagsProperty.serializedObject.ApplyModifiedProperties();
        }

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
        private static void PopulateTagDisplays(SerializedProperty _TagsProperty, Switcher _ContentSwitcher, VisualElement _ContentContainer)
        {
            _ContentContainer.Clear();

            var tags = GameplayTagDrawerHelper.ReadList(_TagsProperty);

            bool any = false;
            for (int i = 0; i < tags.Count; i++)
            {
                // None entries are placeholders kept for prefab overrides: not shown.
                if (tags[i].IsNone) continue;

                _ContentContainer.Add(CreateTagValueElement(_TagsProperty, tags[i], i));
                any = true;
            }

            _ContentSwitcher.SwitchActive(any ? 1 : 0);
        }

        private static VisualElement CreateTagValueElement(SerializedProperty _TagsProperty, GameplayTag _Tag, int _Index)
        {
            VisualElement container = new();
            container.name = "gameplay-tag__tag-value-container";
            container.style.flexDirection = FlexDirection.Row;

            var tagValueClearButton = new Button()
            {
                name = "gameplay-tag__tag-value-clear-button",
                text = "x"
            };
            tagValueClearButton.AddToClassList("gameplay-tag__tag-value-clear-button");
            tagValueClearButton.clicked += () =>
            {
                if (GameplayTagDrawerHelper.IsAlive(_TagsProperty))
                    RemoveTagAt(_TagsProperty, _Index);
            };
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
