using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Alternate wrapper display of <see cref="TextField"/> 
    /// which adds a placeholder text while input value is empty.
    /// </summary>
    [UxmlElement("PlaceholderTextField", libraryPath = "LMK/Text Fields", visibility = LibraryVisibility.Visible)]
    public partial class PlaceholderTextField : TextField
    {
        private string placeholderText = "Search";
        [UxmlAttribute(name = "placeholder-text")]
        public string PlaceholderText
        {
            get => placeholderText;
            set
            {
                placeholderText = value;
                placeholderLabel.text = value;
            }
        }



        private const string USS_CLASS = "placeholder-text-field";

        private const string USS_CLASS_PLACEHOLDER = USS_CLASS + "__placeholder";

        private readonly Label placeholderLabel;



        public PlaceholderTextField() : base()
        {
            LMKTheme.ApplyIfStandalone(this);
            LMKTheme.AddStyleSheet(this, LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/PlaceholderTextFieldUSS.uss");

            AddToClassList(USS_CLASS);

            // Input field
            RegisterCallback<FocusInEvent>((e) =>
            {
                placeholderLabel.style.display = DisplayStyle.None;
            });
            RegisterCallback<FocusOutEvent>((e) => {
                if (string.IsNullOrEmpty(value))
                    placeholderLabel.style.display = DisplayStyle.Flex;
            });

            // Placeholder label
            placeholderLabel = new()
            {
                pickingMode = PickingMode.Ignore,
                focusable = false,
                text = PlaceholderText,
            };
            placeholderLabel.AddToClassList(USS_CLASS_PLACEHOLDER);

            textInputBase.Add(placeholderLabel);
        }
    }
}
