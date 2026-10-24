using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Alternate wrapper display of <see cref="PlaceholderTextField"/> for search fields.
    /// </summary>
    [UxmlElement("SearchBar", libraryPath = "LMK/Text Fields", visibility = LibraryVisibility.Visible)]
    public partial class SearchBar : PlaceholderTextField
    {
        private const string USS_CLASS = "search-bar";

        private const string USS_CLASS_ICON = USS_CLASS + "__icon";

        // Downscaled by the texture importer (max size + mipmaps), so it is sampled directly.
        private const string ICON_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/Sprites/search-interface-symbol-white.png";

        private const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/SearchBarUSS.uss";

        private readonly Image searchIcon;



        public SearchBar() : base()
        {
            LMKTheme.AddStyleSheet(this, USS_PATH);

            AddToClassList(USS_CLASS);

            // Search icon
            searchIcon = new();
            searchIcon.AddToClassList(USS_CLASS_ICON);
            searchIcon.image = LMKAssetUtility.Load<Texture2D>(ICON_PATH);
            searchIcon.scaleMode = ScaleMode.ScaleToFit;

            Add(searchIcon);
        }
    }
}
