using System;
using System.Linq;

using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Registry;
using LMK.Editor.UIElements;
using LMK.Editor.Utilities;
using LMK.Editor.Windows;
using LMK.Runtime.AbilitySystem;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// A small popup asking for a new attribute's name, "Group.Name" or "Name", styled like the Add Tag popup.
    /// Enter adds the attribute; Esc or clicking elsewhere closes it. A refused name keeps it open with the error.
    /// </summary>
    /// <remarks>
    /// The group is letters and digits only, as in the inspector's Group field; the name after it is letters,
    /// digits and '_'. Keys that would break that are blocked, pasted text keeps only the characters of a name,
    /// and what is left is checked as it changes.
    /// </remarks>
    internal static class AddAttributePopup
    {
        // The Add Tag popup's rules, shared through these classes.
        private const string USS_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/GameplayTagManagerWindowUSS.uss";
        private const string USS_CLASS = "add-popup";
        private const string USS_WARNING = "warning";

        private const string EXAMPLE = "Enter a name, e.g. Vitals.Shield.";



        /// <summary>
        /// Open the popup under <paramref name="_Anchor"/>, offset by <paramref name="_Offset"/>.
        /// </summary>
        /// <param name="_Prefix"> Text to start with, the caret after it, e.g. "Vitals." for an attribute of that group. </param>
        /// <param name="_Add"> Adds the attribute. A failed result keeps the popup open. </param>
        public static AnchoredPopup Show(VisualElement _Anchor, Vector2 _Offset, float _Width, string _Prefix, Func<string, RegistryResult> _Add)
        {
            AnchoredPopup popup = null;
            var content = CreateContent(_Prefix, _Add, () => popup?.Close());

            popup = AnchoredPopup.Show(_Anchor, content, _Offset, _Width);
            if (popup == null) return null;

            // Keep a prefix, with the caret after it, when the popup focuses the field.
            var field = content.Q<PlaceholderTextField>();
            if (!string.IsNullOrEmpty(_Prefix))
            {
                field.textSelection.selectAllOnFocus = false;
                field.textSelection.selectAllOnMouseUp = false;
                field.schedule.Execute(() => field.SelectRange(field.value.Length, field.value.Length)).ExecuteLater(50);
            }

            return popup;
        }

        internal static VisualElement CreateContent(string _Prefix, Func<string, RegistryResult> _Add, Action _Close)
        {
            var content = new VisualElement { name = "add-attribute-popup" };
            content.AddToClassList(USS_CLASS);
            var styleSheet = LMKAssetUtility.Load<StyleSheet>(USS_PATH);
            if (styleSheet != null)
                content.styleSheets.Add(styleSheet);

            var field = new PlaceholderTextField { name = "add-attribute-field", label = "Name", PlaceholderText = "Group.Name" };
            field.AddToClassList(USS_CLASS + "__field");
            field.SetValueWithoutNotify(_Prefix ?? string.Empty);
            content.Add(field);

            var error = new Label { name = "add-attribute-error" };
            error.AddToClassList(USS_CLASS + "__error");
            content.Add(error);

            void ShowError(string _Error)
            {
                bool show = !string.IsNullOrEmpty(_Error);
                error.text = _Error ?? string.Empty;
                error.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                field.EnableInClassList(USS_WARNING, show);
            }

            ShowError(Check(field.value, out _));

            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    e.StopImmediatePropagation();

                    string name = field.value.Trim();
                    string problem = Check(name, out bool complete);
                    if (!complete)
                    {
                        ShowError(problem ?? EXAMPLE);
                        return;
                    }

                    var result = _Add(name);
                    if (result.Success)
                        _Close();
                    else
                        ShowError(result.Error);
                    return;
                }

                if (e.character == '\0' || char.IsControl(e.character)) return;

                int start = Math.Min(field.cursorIndex, field.selectIndex);
                int end = Math.Max(field.cursorIndex, field.selectIndex);
                if (!CanType(field.value, start, end, e.character))
                    e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);

            field.RegisterValueChangedCallback(e =>
            {
                // Pasted text keeps only the characters of a name; Check reports what is still wrong.
                string text = e.newValue;
                if (!IsPartialName(text))
                {
                    text = AttributeInspectorPanel.FilterInserted(e.previousValue, text, IsNameChar);
                    field.SetValueWithoutNotify(text);
                }

                ShowError(Check(text, out _));
            });

            return content;
        }



        #region Rules
        /// <summary>
        /// A character of a name: a letter, a digit, '_' or '.'.
        /// </summary>
        internal static bool IsNameChar(char _Char) => AttributeInspectorPanel.IsSegmentChar(_Char) || _Char == GameplayAttribute.SEPARATOR;

        /// <summary>
        /// A name, complete or still being typed: "Name", "Group." or "Group.Name", where the group is letters and
        /// digits and the name letters, digits and '_'.
        /// </summary>
        internal static bool IsPartialName(string _Text)
        {
            if (string.IsNullOrEmpty(_Text)) return true;
            if (!_Text.All(IsNameChar)) return false;

            int dot = _Text.IndexOf(GameplayAttribute.SEPARATOR);
            if (dot < 0) return true;

            return dot > 0
                && _Text.IndexOf(GameplayAttribute.SEPARATOR, dot + 1) < 0
                && AttributeInspectorPanel.IsValidGroup(_Text[..dot]);
        }

        /// <summary>
        /// Whether typing <paramref name="_Char"/> over the selection [<paramref name="_Start"/>, <paramref name="_End"/>)
        /// still gives a name.
        /// </summary>
        internal static bool CanType(string _Text, int _Start, int _End, char _Char)
        {
            _Text ??= string.Empty;
            _Start = Mathf.Clamp(_Start, 0, _Text.Length);
            _End = Mathf.Clamp(_End, _Start, _Text.Length);

            return IsPartialName(_Text[.._Start] + _Char + _Text[_End..]);
        }

        /// <summary>
        /// The error to show for <paramref name="_Name"/>, or <see langword="null"/>. A name that is empty or ends
        /// with '.' is incomplete, not wrong: no error, but it can't be added yet.
        /// </summary>
        internal static string Check(string _Name, out bool _Complete)
        {
            _Complete = false;
            _Name = _Name?.Trim() ?? string.Empty;

            if (_Name.Length == 0 || _Name[^1] == GameplayAttribute.SEPARATOR) return null;

            if (!IsPartialName(_Name))
                return "Use Group.Name or Name: the group can only contain letters and digits, the name letters, digits and '_'.";

            if (!AttributeName.IsValid(_Name, out string error)) return error;

            var main = AttributeKind.Instance.Snapshot.Main;
            if (main != null && main.TryFind(_Name, out _))
                return $"An attribute named \"{_Name}\" already exists.";

            _Complete = true;
            return null;
        }
        #endregion
    }
}
