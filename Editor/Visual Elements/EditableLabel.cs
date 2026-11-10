using System;
using System.Text;

using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Label whose text can be edited in place: a double-click, a click on the pencil shown on hover,
    /// or <see cref="BeginEdit"/> turn it into a text field. Enter commits, Esc cancels. <br/>
    /// Sends a <see cref="ChangeEvent{T}"/> when a commit changes <see cref="value"/>.
    /// </summary>
    /// <remarks>
    /// Add <see cref="titleVariantUssClassName"/> for a larger, bold title.
    /// </remarks>
    [UxmlElement("EditableLabel", libraryPath = "LMK/Text Fields", visibility = LibraryVisibility.Visible)]
    public partial class EditableLabel : VisualElement, INotifyValueChanged<string>
    {
        /// <summary>
        /// USS class for the title variant: larger and bold.
        /// </summary>
        public static readonly string titleVariantUssClassName = USS_CLASS + "--title";


        private string text = string.Empty;
        /// <summary>
        /// The text. Sends a <see cref="ChangeEvent{T}"/> when changed.
        /// </summary>
        [UxmlAttribute(name = "value")]
        public string value
        {
            get => text;
            set
            {
                value ??= string.Empty;
                if (text == value) return;

                using var changeEvent = ChangeEvent<string>.GetPooled(text, value);
                changeEvent.target = this;
                SetValueWithoutNotify(value);
                SendEvent(changeEvent);
            }
        }

        /// <summary>
        /// If true, losing focus commits the edit; if false, it cancels it.
        /// </summary>
        [UxmlAttribute(name = "commit-on-blur")]
        public bool CommitOnBlur { get; set; }


        private bool editable = true;
        /// <summary>
        /// Whether the text can be edited. Turning it off cancels an edit in progress.
        /// </summary>
        [UxmlAttribute(name = "editable")]
        public bool Editable
        {
            get => editable;
            set
            {
                editable = value;
                UpdatePencilDisplay();

                if (!editable)
                    EndEdit(false);
            }
        }

        /// <summary>
        /// Characters that can be typed or pasted. Null accepts every character.
        /// </summary>
        public Func<char, bool> IsValidChar { get; set; }

        /// <summary>
        /// Checks the text on commit: returns an error message, or null when the text is valid.
        /// An error keeps the field open and shows the message as its tooltip.
        /// </summary>
        public Func<string, string> Validate { get; set; }

        /// <summary>
        /// Whether the text field is shown.
        /// </summary>
        public bool IsEditing { get; private set; }

        internal TextField Field => field;

        internal Label LabelElement => label;

        internal Image PencilIcon => pencilIcon;

        internal const string USS_CLASS = "lmk-editable-label";

        internal const string USS_CLASS_LABEL = USS_CLASS + "__label";

        internal const string USS_CLASS_ICON = USS_CLASS + "__icon";

        internal const string USS_CLASS_FIELD = USS_CLASS + "__field";

        internal const string USS_CLASS_EDITING = USS_CLASS + "--editing";

        internal const string USS_CLASS_INVALID = USS_CLASS + "--invalid";

        private const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/EditableLabelUSS.uss";

        // White, tinted with --lmk-color-icon so it suits both skins.
        private const string PENCIL_ICON_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/Sprites/pencil-symbol-white.png";

        // How long BeginEdit keeps trying to focus the field.
        private const long FOCUS_TIMEOUT_MS = 1000;

        private readonly Label label;

        private readonly Image pencilIcon;

        private readonly TextField field;

        // Focus-out only ends an edit once the field really had focus: a focus change that settles
        // after the edit started (e.g. from the click or menu that started it) must not end it.
        private bool fieldHadFocus;

        private bool isFiltering;



        public EditableLabel()
        {
            LMKTheme.ApplyIfStandalone(this);
            LMKTheme.AddStyleSheet(this, USS_PATH);

            AddToClassList(USS_CLASS);

            label = new() { pickingMode = PickingMode.Ignore };
            label.AddToClassList(USS_CLASS_LABEL);
            Add(label);

            // A button of its own: highlighted on hover, and a click starts editing.
            pencilIcon = new()
            {
                image = LMKAssetUtility.Load<Texture2D>(PENCIL_ICON_PATH),
                scaleMode = ScaleMode.ScaleToFit,
                tooltip = "Edit",
            };
            pencilIcon.AddToClassList(USS_CLASS_ICON);
            pencilIcon.AddManipulator(new Clickable(BeginEdit));
            Add(pencilIcon);

            field = new() { isDelayed = false };
            field.AddToClassList(USS_CLASS_FIELD);
            field.style.display = DisplayStyle.None;
            Add(field);

            // Registered first, so the field's own change events never reach callbacks on this element.
            RegisterCallback<ChangeEvent<string>>(OnChildChangeEvent);

            RegisterCallback<PointerDownEvent>(OnPointerDown);

            // Trickle down: handled before the text field reacts, see OnFieldKeyDown.
            RegisterCallback<KeyDownEvent>(OnFieldKeyDown, TrickleDown.TrickleDown);

            field.RegisterCallback<ChangeEvent<string>>(OnFieldChanged);
            field.RegisterCallback<FocusInEvent>(_ => fieldHadFocus = true);
            field.RegisterCallback<FocusOutEvent>(OnFieldFocusOut);

            Editable = true;
            SetValueWithoutNotify(string.Empty);
        }



        /// <summary>
        /// Set <see cref="value"/> without sending a <see cref="ChangeEvent{T}"/>.
        /// </summary>
        public void SetValueWithoutNotify(string _NewValue)
        {
            text = _NewValue ?? string.Empty;
            ((INotifyValueChanged<string>)label).SetValueWithoutNotify(text);
        }

        /// <summary>
        /// Show the text field with the current text selected. Does nothing if not <see cref="Editable"/>.
        /// </summary>
        public void BeginEdit()
        {
            if (IsEditing || !editable) return;

            IsEditing = true;
            fieldHadFocus = false;

            field.SetValueWithoutNotify(text);
            SetInvalid(null);

            label.style.display = DisplayStyle.None;
            UpdatePencilDisplay();
            field.style.display = DisplayStyle.Flex;
            AddToClassList(USS_CLASS_EDITING);

            // A frame later: focusing straight away can be undone by the click or menu that started the edit.
            // Retried until the style update has shown the field, since a hidden field can't take focus.
            schedule.Execute(TryFocusField)
                .Every(0)
                .Until(() => !IsEditing || IsFocusInField())
                .ForDuration(FOCUS_TIMEOUT_MS);
        }

        /// <summary>
        /// Hide the text field. <paramref name="_Commit"/> applies its text, unless <see cref="Validate"/>
        /// returns an error: then the field stays open with the error as its tooltip.
        /// </summary>
        public void EndEdit(bool _Commit)
        {
            FinishEdit(_Commit);
        }

        // False if a Validate error kept the edit open.
        private bool FinishEdit(bool _Commit)
        {
            if (!IsEditing) return true;

            string newText = field.value ?? string.Empty;
            if (_Commit && Validate != null)
            {
                string error = Validate(newText);
                if (error != null)
                {
                    SetInvalid(error);
                    return false;
                }
            }

            IsEditing = false;
            fieldHadFocus = false;
            SetInvalid(null);

            field.style.display = DisplayStyle.None;
            label.style.display = DisplayStyle.Flex;
            UpdatePencilDisplay();
            RemoveFromClassList(USS_CLASS_EDITING);

            if (_Commit)
                value = newText;

            return true;
        }

        // Removed from the layout while editing, so the field starts where the label text was.
        private void UpdatePencilDisplay()
        {
            bool isShown = editable && !IsEditing && pencilIcon.image;
            pencilIcon.style.display = isShown ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void TryFocusField()
        {
            if (!IsEditing || IsFocusInField() || !field.canGrabFocus) return;

            field.Focus();
            field.SelectAll();
        }

        // The field delegates focus to its text input, so the focused element is usually a descendant.
        private bool IsFocusInField()
        {
            for (var element = focusController?.focusedElement as VisualElement; element != null; element = element.hierarchy.parent)
            {
                if (element == field)
                    return true;
            }
            return false;
        }



        #region Events
        private void OnChildChangeEvent(ChangeEvent<string> _Event)
        {
            if (_Event.target != this)
                _Event.StopImmediatePropagation();
        }

        private void OnPointerDown(PointerDownEvent _Event)
        {
            if (_Event.button != 0 || _Event.clickCount != 2 || IsEditing || !editable) return;

            BeginEdit();
            _Event.StopPropagation();
        }

        // A single-line field can lose focus on Enter, and that focus-out would end the edit
        // before a key up arrives. Handling the key down while it trickles down avoids that.
        private void OnFieldKeyDown(KeyDownEvent _Event)
        {
            if (!IsEditing) return;

            switch (_Event.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    EndEdit(true);
                    _Event.StopImmediatePropagation();
                    return;

                case KeyCode.Escape:
                    EndEdit(false);
                    _Event.StopImmediatePropagation();
                    return;
            }

            if (IsValidChar != null && _Event.character != '\0' && !char.IsControl(_Event.character) && !IsValidChar(_Event.character))
                _Event.StopImmediatePropagation();
        }

        // Typed characters are filtered on key down; this catches pasted text.
        private void OnFieldChanged(ChangeEvent<string> _Event)
        {
            if (isFiltering) return;

            SetInvalid(null);

            if (IsValidChar == null) return;

            string filtered = Filter(_Event.newValue, IsValidChar);
            if (filtered == _Event.newValue) return;

            isFiltering = true;
            int cursor = Mathf.Min(field.cursorIndex, filtered.Length);
            field.value = filtered;
            field.SelectRange(cursor, cursor);
            isFiltering = false;
        }

        private void OnFieldFocusOut(FocusOutEvent _Event)
        {
            if (!IsEditing || !fieldHadFocus) return;

            // Focus moving inside the field, e.g. to its text input, isn't a blur.
            for (var element = _Event.relatedTarget as VisualElement; element != null; element = element.hierarchy.parent)
            {
                if (element == field) return;
            }

            // A blur can't keep an invalid edit open, since nothing would have focus to fix it.
            if (!CommitOnBlur || !FinishEdit(true))
                FinishEdit(false);
        }
        #endregion



        private void SetInvalid(string _Error)
        {
            EnableInClassList(USS_CLASS_INVALID, _Error != null);
            field.tooltip = _Error ?? string.Empty;
        }

        /// <summary>
        /// <paramref name="_Text"/> without the characters <paramref name="_IsValidChar"/> rejects.
        /// </summary>
        internal static string Filter(string _Text, Func<char, bool> _IsValidChar)
        {
            if (string.IsNullOrEmpty(_Text) || _IsValidChar == null) return _Text ?? string.Empty;

            StringBuilder builder = null;
            for (int i = 0; i < _Text.Length; i++)
            {
                if (_IsValidChar(_Text[i]))
                {
                    builder?.Append(_Text[i]);
                }
                else if (builder == null)
                {
                    builder = new StringBuilder(_Text.Length);
                    builder.Append(_Text, 0, i);
                }
            }
            return builder?.ToString() ?? _Text;
        }
    }
}
