using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;
using LMK.Runtime.GameplayTags;
using LMK.Editor.Utilities;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Custom inspector display for <see cref="GameplayTag"/>.
    /// </summary>
    /// <remarks>
    /// Shows the tag's current path, "&lt;name&gt; (deleted)" when it doesn't resolve, or "- Mixed -" when the selected
    /// objects hold different tags. While any selected object holds a missing tag, a click offers only
    /// "Clear missing tag". Otherwise it opens the picker.
    /// </remarks>
    [CustomPropertyDrawer(typeof(GameplayTag))]
    internal sealed class GameplayTagDrawer : PropertyDrawer
    {
        private const string UXML_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/GameplayTagUXML.uxml";

        private const string USS_CLASS = "gameplay-tag-field";

        private const string NONE_TEXT = "None";



        public override VisualElement CreatePropertyGUI(SerializedProperty _Property)
        {
            // Drawers are shared between properties, so all state lives in this method.
            VisualTreeAsset visualTree = LMKAssetUtility.Load<VisualTreeAsset>(UXML_PATH);
            if (visualTree == null) return base.CreatePropertyGUI(_Property);

            var root = GameplayTagDrawerBinding.CreateField(_Property.displayName, visualTree.CloneTree(), USS_CLASS, false);

            // The box and the arrow after it are clicked together. The picker opens under the box.
            var clickArea = root.Q<VisualElement>("main-container");
            var box = root.Q<VisualElement>("tag-box");
            var tagDisplay = root.Q<Label>("tag-label");
            var placeholder = root.Q<Label>("empty-tag-label");
            var tagLabelSwitcher = root.Q<Switcher>("tag-label-switcher");

            void RefreshDisplay()
            {
                if (!GameplayTagDrawerHelper.IsAlive(_Property)) return;

                if (_Property.hasMultipleDifferentValues)
                {
                    placeholder.text = GameplayTagDrawerHelper.MIXED_TEXT;
                    placeholder.tooltip = GameplayTagDrawerHelper.MIXED_TOOLTIP;
                    tagLabelSwitcher.SwitchActive(0);
                    return;
                }

                var tag = GameplayTagDrawerHelper.Read(_Property);

                placeholder.text = NONE_TEXT;
                placeholder.tooltip = string.Empty;
                GameplayTagDrawerHelper.ApplyDisplay(tagDisplay, tag);
                tagLabelSwitcher.SwitchActive(tag.IsNone ? 0 : 1);
            }

            tagLabelSwitcher.OnAttachedToPanel += RefreshDisplay;
            GameplayTagDrawerBinding.BindRefresh(root, _Property, RefreshDisplay);

            GameplayTagDrawerBinding.BindClick(clickArea, _Property, false,
                _AfterRemoveMissing: () =>
                {
                    _Property.serializedObject.Update();
                    RefreshDisplay();
                },
                _OpenPicker: () =>
                {
                    // Mixed values: nothing is selected, and a pick sets every target.
                    var current = _Property.hasMultipleDifferentValues ? GameplayTag.None : GameplayTagDrawerHelper.Read(_Property);

                    GameplayTagSelectionWindow.Open(
                        _GUILocation: new Vector2(box.worldBound.xMin, box.worldBound.yMax),
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
