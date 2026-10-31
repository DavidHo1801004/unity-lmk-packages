using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Read-only display of a colour: a swatch and its hex value.
    /// </summary>
    [UxmlElement("ColorLabel", libraryPath = "LMK/Labels", visibility = LibraryVisibility.Visible)]
    public partial class ColorLabel : VisualElement
    {
        private Color value = Color.white;
        /// <summary>
        /// The displayed colour. Shown as <c>#RRGGBB</c>, or <c>#RRGGBBAA</c> when it isn't opaque.
        /// </summary>
        [UxmlAttribute(name = "value")]
        public Color Value
        {
            get => value;
            set
            {
                this.value = value;

                swatch.style.backgroundColor = value;
                hexLabel.text = GetHexText(value);
            }
        }



        private const string USS_CLASS = "color-label";

        private const string USS_CLASS_SWATCH = USS_CLASS + "__swatch";

        private const string USS_CLASS_HEX = USS_CLASS + "__hex";

        private const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/ColorLabelUSS.uss";

        private readonly VisualElement swatch;

        private readonly Label hexLabel;



        public ColorLabel()
            : this(Color.white) { }

        public ColorLabel(Color _Value)
        {
            LMKTheme.ApplyIfStandalone(this);
            LMKTheme.AddStyleSheet(this, USS_PATH);

            AddToClassList(USS_CLASS);

            swatch = new() { pickingMode = PickingMode.Ignore };
            swatch.AddToClassList(USS_CLASS_SWATCH);
            Add(swatch);

            hexLabel = new();
            hexLabel.AddToClassList(USS_CLASS_HEX);
            Add(hexLabel);

            Value = _Value;
        }



        // Compared as Color32 so the text only gains an alpha when it would differ from FF.
        private static string GetHexText(Color _Value)
        {
            return ((Color32)_Value).a < byte.MaxValue
                ? $"#{ColorUtility.ToHtmlStringRGBA(_Value)}"
                : $"#{ColorUtility.ToHtmlStringRGB(_Value)}";
        }
    }
}
