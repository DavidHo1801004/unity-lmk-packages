using UnityEditor;
using UnityEngine.UIElements;

using LMK.Editor.Utilities;

namespace LMK.Editor.Styles
{
    /// <summary>
    /// Applies the LMK colour palette to visual elements. <br/>
    /// The palette defines public <c>--lmk-color-*</c> USS variables in
    /// <c>Editor/Assets/LMK/LMKThemeDark.uss</c> and <c>Editor/Assets/LMK/LMKThemeLight.uss</c> of this package.
    /// </summary>
    /// <remarks>
    /// Elements created before the Editor skin changes keep the old palette until they are rebuilt.
    /// </remarks>
    public static class LMKTheme
    {
        /// <summary>
        /// USS class which holds the palette variables.
        /// </summary>
        public const string USS_CLASS = "lmk-theme";

        private const string DARK_THEME_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Assets/LMK/LMKThemeDark.uss";
        private const string LIGHT_THEME_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Assets/LMK/LMKThemeLight.uss";

        /// <summary>
        /// Add the palette for the current Editor skin to <paramref name="_Element"/>,
        /// so it and its children can use <c>var(--lmk-color-*)</c>. Safe to call more than once.
        /// </summary>
        public static void Apply(VisualElement _Element)
        {
            if (_Element == null) return;

            AddStyleSheet(_Element, EditorGUIUtility.isProSkin ? DARK_THEME_PATH : LIGHT_THEME_PATH);
            _Element.AddToClassList(USS_CLASS);
        }

        /// <summary>
        /// Apply the palette to <paramref name="_Element"/> when it is attached to a panel,
        /// only if no ancestor already provides it. <br/>
        /// Use for reusable controls, so they work standalone (e.g. in UI Builder)
        /// without adding a duplicate palette sheet under an already themed parent.
        /// </summary>
        public static void ApplyIfStandalone(VisualElement _Element)
        {
            if (_Element == null) return;

            _Element.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (_Element.ClassListContains(USS_CLASS)) return;

                for (var ancestor = _Element.hierarchy.parent; ancestor != null; ancestor = ancestor.hierarchy.parent)
                {
                    if (ancestor.ClassListContains(USS_CLASS)) return;
                }

                Apply(_Element);
            });
        }

        /// <summary>
        /// Load the style sheet at <paramref name="_AssetPath"/> and add it to <paramref name="_Element"/> if not already added.
        /// </summary>
        /// <param name="_AssetPath"> Full asset path, e.g. <c>"Packages/com.lmk.editor/Editor/Assets/LMK/LMKThemeDark.uss"</c>. </param>
        /// <returns> False if the style sheet can't be found. </returns>
        public static bool AddStyleSheet(VisualElement _Element, string _AssetPath)
        {
            if (_Element == null) return false;

            var styleSheet = LMKAssetUtility.Load<StyleSheet>(_AssetPath);
            if (!styleSheet) return false;

            if (!_Element.styleSheets.Contains(styleSheet))
                _Element.styleSheets.Add(styleSheet);

            return true;
        }
    }
}
