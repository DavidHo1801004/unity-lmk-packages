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
    /// Custom inspector display for <see cref="GameplayTag"/>.
    /// </summary>
    /// <remarks>
    /// Shows the tag's current path, or "&lt;name&gt; (deleted)" when it doesn't resolve. While any selected
    /// object holds a missing tag, a click offers only "Clear missing tag". Otherwise it opens the picker.
    /// </remarks>
    [CustomPropertyDrawer(typeof(GameplayTag))]
    internal sealed class GameplayTagDrawer : PropertyDrawer
    {
        private const string UXML_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/GameplayTagUXML.uxml";



        public override VisualElement CreatePropertyGUI(SerializedProperty _Property)
        {
            // Drawers are shared between properties, so all state lives in this method.
            VisualTreeAsset visualTree = LMKAssetUtility.Load<VisualTreeAsset>(UXML_PATH);
            if (visualTree == null) return base.CreatePropertyGUI(_Property);

            var root = visualTree.CloneTree();
            LMKTheme.ApplyIfStandalone(root);

            string propertyPath = _Property.propertyPath;

            root.Q<Label>("property-label").text = _Property.displayName;

            var tagDisplay = root.Q<Label>("tag-label");
            var tagLabelSwitcher = root.Q<Switcher>("tag-label-switcher");

            void RefreshDisplay()
            {
                if (!GameplayTagDrawerHelper.IsAlive(_Property)) return;

                var tag = GameplayTagDrawerHelper.Read(_Property);

                GameplayTagDrawerHelper.ApplyDisplay(tagDisplay, tag);
                tagLabelSwitcher.SwitchActive(tag.IsNone ? 0 : 1);
            }

            tagLabelSwitcher.OnAttachedToPanel += RefreshDisplay;

            // Undo, prefab overrides, other editors and registry changes can all change what is shown.
            root.TrackPropertyValue(_Property, _ => RefreshDisplay());
            root.RegisterCallback<AttachToPanelEvent>(_ => TagRegistryService.OnSnapshotChanged += RefreshDisplay);
            root.RegisterCallback<DetachFromPanelEvent>(_ => TagRegistryService.OnSnapshotChanged -= RefreshDisplay);

            var mainContainer = root.Q<VisualElement>("main-container");
            mainContainer.RegisterCallback<PointerDownEvent>((e) =>
            {
                if (e.button != 0 || !GameplayTagDrawerHelper.IsAlive(_Property)) return;

                var targets = _Property.serializedObject.targetObjects;

                // While a missing tag is present, the only action is to clear it.
                int missing = GameplayTagDrawerHelper.CountMissing(targets, propertyPath, false);
                if (missing > 0)
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent(GameplayTagDrawerHelper.GetRemoveMissingLabel(false, missing)), false, () =>
                    {
                        GameplayTagDrawerHelper.RemoveMissing(targets, propertyPath, false);
                        _Property.serializedObject.Update();
                        RefreshDisplay();
                    });
                    menu.ShowAsContext();
                    return;
                }

                var current = GameplayTagDrawerHelper.Read(_Property);

                GameplayTagSelectionWindow.Open(
                    _GUILocation: new Vector2(tagLabelSwitcher.worldBound.xMin, mainContainer.worldBound.yMax),
                    _WindowLabel: "GAMEPLAY TAG",
                    _SingleSelection: true,
                    _SelectedTags: current.IsNone ? null : new[] { current },
                    _OnSelectionChanged: (tags) =>
                    {
                        if (!GameplayTagDrawerHelper.IsAlive(_Property)) return;

                        _Property.serializedObject.Update();
                        GameplayTagDrawerHelper.Write(_Property, tags.Length == 0 ? GameplayTag.None : tags[0]);
                        _Property.serializedObject.ApplyModifiedProperties();

                        RefreshDisplay();
                    });
            });

            return root;
        }
    }
}
