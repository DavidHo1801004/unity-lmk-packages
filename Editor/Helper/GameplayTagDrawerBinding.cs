using System;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.UIElements;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// UI wiring shared by the Gameplay Tag and Gameplay Tag Container drawers: the field, when its display refreshes,
    /// and what a click does. Each drawer has its own UXML and stylesheet, so how the fields look is not shared.
    /// </summary>
    internal static class GameplayTagDrawerBinding
    {
        private const string DARK_THEME_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/Theme/GameplayTagThemeDark.uss";
        private const string LIGHT_THEME_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/Theme/GameplayTagThemeLight.uss";

        /// <summary>
        /// Add the gameplay tags palette for the current Editor skin to a tag field: the <c>--gameplay-tag-color-*</c>
        /// variables the LMK palette doesn't have. Like the LMK palette, a field created before the skin changes keeps
        /// the old colours until it is rebuilt.
        /// </summary>
        public static void ApplyTheme(VisualElement _Field)
            => LMKTheme.AddStyleSheet(_Field, EditorGUIUtility.isProSkin ? DARK_THEME_PATH : LIGHT_THEME_PATH);

        /// <summary>
        /// An <see cref="AlignedField"/>, so the label lines up with the fields around it, holding
        /// <paramref name="_Content"/> as its input.
        /// </summary>
        /// <param name="_UssClass"> Class of the field, which the drawer's stylesheet styles. </param>
        /// <param name="_LabelToTop"> The content can be taller than one line: the label stays on the first line. </param>
        public static AlignedField CreateField(string _Label, VisualElement _Content, string _UssClass, bool _LabelToTop)
        {
            var field = new AlignedField(_Label) { AlignLabelToTop = _LabelToTop };
            field.AddToClassList(_UssClass);
            LMKTheme.ApplyIfStandalone(field);
            ApplyTheme(field);

            _Content.style.flexGrow = 1;
            _Content.style.flexShrink = 1;
            _Content.style.minWidth = 0;
            field.Add(_Content);

            return field;
        }

        /// <summary>
        /// Call <paramref name="_Refresh"/> when <paramref name="_Tracked"/> changes (Undo, prefab overrides, other
        /// editors) and, while <paramref name="_Root"/> is attached, when the tag registries change.
        /// </summary>
        public static void BindRefresh(VisualElement _Root, SerializedProperty _Tracked, Action _Refresh)
        {
            _Root.TrackPropertyValue(_Tracked, _ => _Refresh());

            // A static event would keep a detached field alive, so it is only handled while attached.
            _Root.RegisterCallback<AttachToPanelEvent>(_ => TagRegistryService.OnSnapshotChanged += _Refresh);
            _Root.RegisterCallback<DetachFromPanelEvent>(_ => TagRegistryService.OnSnapshotChanged -= _Refresh);
        }

        /// <summary>
        /// A left click on <paramref name="_Target"/>: while any selected object holds missing tags, the only action
        /// offered is to remove them, then <paramref name="_AfterRemoveMissing"/> is called. Otherwise
        /// <paramref name="_OpenPicker"/> is called. Clicks on buttons inside the target (a tag's ×) are ignored.
        /// </summary>
        /// <param name="_IsContainer"> Whether <paramref name="_Property"/> is a <c>GameplayTagContainer</c>, or a single <c>GameplayTag</c>. </param>
        public static void BindClick(VisualElement _Target, SerializedProperty _Property, bool _IsContainer,
                                     Action _AfterRemoveMissing, Action _OpenPicker)
        {
            string propertyPath = _Property.propertyPath;

            _Target.RegisterCallback<PointerDownEvent>((e) =>
            {
                if (e.button != 0 || !GameplayTagDrawerHelper.IsAlive(_Property)) return;
                if (e.target is Button) return;

                var targets = _Property.serializedObject.targetObjects;

                int missing = GameplayTagDrawerHelper.CountMissing(targets, propertyPath, _IsContainer);
                if (missing > 0)
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent(GameplayTagDrawerHelper.GetRemoveMissingLabel(_IsContainer, missing)), false, () =>
                    {
                        GameplayTagDrawerHelper.RemoveMissing(targets, propertyPath, _IsContainer);
                        _AfterRemoveMissing?.Invoke();
                    });
                    menu.ShowAsContext();
                    return;
                }

                _OpenPicker();
            });
        }
    }
}
