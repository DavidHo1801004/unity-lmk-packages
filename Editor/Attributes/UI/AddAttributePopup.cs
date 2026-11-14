using System;

using UnityEditor;
using UnityEngine;

using LMK.Editor.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// A small popup asking for a new attribute's name, e.g. "Vitals.Shield". Stays open with the error if the
    /// name is refused.
    /// </summary>
    internal sealed class AddAttributePopup : PopupWindowContent
    {
        private const string CONTROL = "AddAttributeName";

        private readonly Func<string, RegistryResult> add;
        private string name;
        private string error;
        private bool focused;

        private AddAttributePopup(string _Name, Func<string, RegistryResult> _Add)
        {
            name = _Name;
            add = _Add;
        }

        /// <param name="_Add"> Adds the attribute. A failed result keeps the popup open. </param>
        public static void Show(Rect _Anchor, string _InitialName, Func<string, RegistryResult> _Add)
            => PopupWindow.Show(_Anchor, new AddAttributePopup(_InitialName, _Add));

        public override Vector2 GetWindowSize() => new(320f, string.IsNullOrEmpty(error) ? 70f : 110f);

        public override void OnGUI(Rect _Rect)
        {
            GUILayout.Label("New Gameplay Attribute", EditorStyles.boldLabel);

            GUI.SetNextControlName(CONTROL);
            name = EditorGUILayout.TextField("Name", name);
            if (!focused)
            {
                EditorGUI.FocusTextInControl(CONTROL);
                focused = true;
            }

            bool submit = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                submit |= GUILayout.Button("Add", GUILayout.Width(80f));
            }

            if (!string.IsNullOrEmpty(error))
                EditorGUILayout.HelpBox(error, MessageType.Error);

            if (!submit) return;

            var result = add(name?.Trim());
            if (result.Success)
            {
                editorWindow.Close();
                return;
            }

            error = result.Error;
            editorWindow.Repaint();
        }
    }
}
