using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Display a menu item separator with a prefix label.
    /// </summary>
    [UxmlElement("NamedSeparator", libraryPath = "LMK/Menu Items", visibility = LibraryVisibility.Visible)]
    public partial class NamedSeparator : VisualElement
    {
        private string text;
        /// <summary>
        /// The display name for the separator. Leave empty for a single line separator.
        /// </summary>
        [UxmlAttribute(name = "text")]
        public string Text
        {
            get => text;
            set
            {
                text = value;
                label.text = text;
                label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }


        private Color color = Color.clear;
        /// <summary>
        /// Optional color override for the entire element. <br/>
        /// <see cref="Color.clear"/> uses the theme color <c>--lmk-color-separator</c>.
        /// </summary>
        [UxmlAttribute(name = "color")]
        public Color Color
        {
            get => color;
            set
            {
                color = value;

                bool useTheme = color == Color.clear;
                label.style.color = useTheme ? new StyleColor(StyleKeyword.Null) : new StyleColor(color);
                separator.style.backgroundColor = useTheme ? new StyleColor(StyleKeyword.Null) : new StyleColor(color);
            }
        }



        private const string USS_CLASS = "named_separator";

        private const string USS_CLASS_LABEL = USS_CLASS + "__label";

        private const string USS_CLASS_SEPARATOR = USS_CLASS + "__separator";

        private readonly Label label;

        private readonly VisualElement separator;



        public NamedSeparator() 
            : this(string.Empty) { }

        public NamedSeparator(string _Text)
        {
            LMKTheme.ApplyIfStandalone(this);
            LMKTheme.AddStyleSheet(this, LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/NamedSeparatorUSS.uss");

            AddToClassList(USS_CLASS);

            label = new Label();
            label.AddToClassList(USS_CLASS_LABEL);
            label.text = _Text;

            separator = new VisualElement();
            separator.AddToClassList(USS_CLASS_SEPARATOR);

            Add(label);
            Add(separator);

            Text = _Text;
        }
    }
}
