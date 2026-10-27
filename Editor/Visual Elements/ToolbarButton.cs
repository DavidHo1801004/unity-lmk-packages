using System;

using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Flat toolbar <see cref="Button"/> which works as a plain button or a toggle. <br/>
    /// Display states, from lowest to highest highlight: normal, active (pressed, or on in toggle mode), hovered.
    /// </summary>
    /// <remarks>
    /// The toggle state is updated by a <see cref="Button.clicked"/> handler,
    /// so replacing <see cref="Button.clickable"/> also removes the toggle behaviour.
    /// </remarks>
    [UxmlElement("ToolbarButton", libraryPath = "LMK/Buttons", visibility = LibraryVisibility.Visible)]
    public partial class ToolbarButton : Button, INotifyValueChanged<bool>
    {
        private bool isToggle;
        /// <summary>
        /// If true, each click flips <see cref="value"/>. <br/>
        /// Switching to button mode resets <see cref="value"/> to false.
        /// </summary>
        [UxmlAttribute(name = "is-toggle")]
        public bool IsToggle
        {
            get => isToggle;
            set
            {
                isToggle = value;
                EnableInClassList(USS_CLASS_TOGGLE, isToggle);

                if (!isToggle)
                    this.value = false;
            }
        }


        private bool toggleValue;
        /// <summary>
        /// The toggle state. Sends a <see cref="ChangeEvent{T}"/> when changed. <br/>
        /// Always false in button mode; setting it to true is ignored.
        /// </summary>
        [UxmlAttribute(name = "value")]
        public bool value
        {
            get => toggleValue;
            set
            {
                if (value && !isToggle) return;
                if (toggleValue == value) return;

                using var changeEvent = ChangeEvent<bool>.GetPooled(toggleValue, value);
                changeEvent.target = this;
                SetValueWithoutNotify(value);
                SendEvent(changeEvent);
            }
        }



        private const string USS_CLASS = "toolbar-button";

        private const string USS_CLASS_TOGGLE = USS_CLASS + "--toggle";

        private const string USS_CLASS_ACTIVE = USS_CLASS + "--active";

        private const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/ToolbarButtonUSS.uss";



        public ToolbarButton()
            : this(false, null) { }

        /// <param name="_IsToggle"> If true, each click flips <see cref="value"/>. </param>
        /// <param name="_ClickEvent"> Optional <see cref="Button.clicked"/> handler. Called after the toggle state is updated. </param>
        public ToolbarButton(bool _IsToggle, Action _ClickEvent = null) : base()
        {
            LMKTheme.ApplyIfStandalone(this);
            LMKTheme.AddStyleSheet(this, USS_PATH);

            // Replace the default button look (border, background, hover colours).
            RemoveFromClassList(ussClassName);
            AddToClassList(USS_CLASS);

            // Subscribed first, so other clicked handlers see the updated value.
            clicked += OnClicked;
            if (_ClickEvent != null)
                clicked += _ClickEvent;

            IsToggle = _IsToggle;
        }



        /// <summary>
        /// Set <see cref="value"/> without sending a <see cref="ChangeEvent{T}"/>. <br/>
        /// Ignored when setting true in button mode.
        /// </summary>
        public void SetValueWithoutNotify(bool _NewValue)
        {
            if (_NewValue && !isToggle) return;

            toggleValue = _NewValue;
            EnableInClassList(USS_CLASS_ACTIVE, toggleValue);
        }

        private void OnClicked()
        {
            if (isToggle)
                value = !value;
        }
    }
}
