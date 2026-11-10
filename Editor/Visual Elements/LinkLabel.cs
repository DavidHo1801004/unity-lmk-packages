using System;

using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Text that acts as a link: link colour, underline and link cursor on hover,
    /// and <see cref="clicked"/> on a left click or on Enter or Space while focused.
    /// </summary>
    /// <remarks>
    /// Disabled, it shows as plain muted text and raises nothing.
    /// </remarks>
    [UxmlElement("LinkLabel", libraryPath = "LMK/Labels", visibility = LibraryVisibility.Visible)]
    public partial class LinkLabel : Label
    {
        /// <summary>
        /// Called on a left click, or on Enter or Space while focused.
        /// </summary>
        public event Action clicked;

        internal const string USS_CLASS = "lmk-link-label";

        private const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/LinkLabelUSS.uss";



        public LinkLabel()
            : this(string.Empty) { }

        /// <param name="_Text"> The link text. </param>
        public LinkLabel(string _Text)
            : base(_Text)
        {
            LMKTheme.ApplyIfStandalone(this);
            LMKTheme.AddStyleSheet(this, USS_PATH);

            AddToClassList(USS_CLASS);
            focusable = true;

            this.AddManipulator(new Clickable(Click));
            RegisterCallback<KeyDownEvent>(OnKeyDown);
        }



        private void Click()
        {
            if (enabledInHierarchy)
                clicked?.Invoke();
        }

        private void OnKeyDown(KeyDownEvent _Event)
        {
            if (_Event.keyCode is not (KeyCode.Return or KeyCode.KeypadEnter or KeyCode.Space)) return;

            Click();
            _Event.StopPropagation();
        }
    }
}
