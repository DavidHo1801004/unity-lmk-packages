using System;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

using Object = UnityEngine.Object;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Read-only display of an object reference, following Unity's object field: thumbnail and name,
    /// <c>None (Type)</c>, or <c>Missing (Type)</c>.
    /// </summary>
    [UxmlElement("ObjectLabel", libraryPath = "LMK/Labels", visibility = LibraryVisibility.Visible)]
    public partial class ObjectLabel : VisualElement
    {
        private const string USS_CLASS = "object-label";

        private const string USS_CLASS_ICON = USS_CLASS + "__icon";

        private const string USS_CLASS_NAME = USS_CLASS + "__name";

        private const string USS_CLASS_NONE = USS_CLASS + "--none";

        private const string USS_CLASS_MISSING = USS_CLASS + "--missing";

        private const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/ObjectLabelUSS.uss";

        private const string NONE_TEXT = "None";

        private const string MISSING_TEXT = "Missing";

        private readonly Image icon;

        private readonly Label nameLabel;



        public ObjectLabel()
        {
            LMKTheme.ApplyIfStandalone(this);
            LMKTheme.AddStyleSheet(this, USS_PATH);

            AddToClassList(USS_CLASS);

            icon = new()
            {
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore,
            };
            icon.AddToClassList(USS_CLASS_ICON);
            Add(icon);

            nameLabel = new();
            nameLabel.AddToClassList(USS_CLASS_NAME);
            Add(nameLabel);

            SetValue((Object)null);
        }



        /// <summary>
        /// Display <paramref name="_Value"/>.
        /// </summary>
        /// <param name="_Value"> The object, or <see langword="null"/> to display as unassigned. </param>
        /// <param name="_ObjectType"> Type named by the unassigned text, e.g. <c>None (Texture2D)</c>. </param>
        public void SetValue(Object _Value, Type _ObjectType = null)
        {
            if (_Value)
                ShowObject(_Value);
            else
                ShowUnassigned(NONE_TEXT, USS_CLASS_NONE, _ObjectType?.Name);
        }

        /// <summary>
        /// Display the value of <paramref name="_Property"/>. <br/>
        /// Unlike <see cref="SetValue(Object, Type)"/>, this can tell a missing reference from an unassigned one.
        /// </summary>
        /// <param name="_Property"> A property of type <see cref="SerializedPropertyType.ObjectReference"/>. </param>
        public void SetValue(SerializedProperty _Property)
        {
            if (_Property == null || _Property.propertyType != SerializedPropertyType.ObjectReference)
            {
                SetValue((Object)null);
                return;
            }

            var value = _Property.objectReferenceValue;
            if (value)
            {
                ShowObject(value);
                return;
            }

            // A destroyed or deleted target keeps its instance ID, an unassigned field has none.
            bool isMissing = _Property.objectReferenceInstanceIDValue != 0;
            ShowUnassigned(isMissing ? MISSING_TEXT : NONE_TEXT,
                isMissing ? USS_CLASS_MISSING : USS_CLASS_NONE,
                GetPropertyTypeName(_Property));
        }



        private void ShowObject(Object _Value)
        {
            var content = EditorGUIUtility.ObjectContent(_Value, _Value.GetType());

            // Sprites get the generic Sprite icon as a thumbnail, so they are drawn directly instead.
            // Cleared first: Image logs a warning when it already holds the other kind of source.
            var sprite = _Value as Sprite;
            icon.image = null;
            icon.sprite = sprite;
            if (!sprite)
                icon.image = content.image;

            icon.style.display = DisplayStyle.Flex;
            nameLabel.text = content.text;

            RemoveFromClassList(USS_CLASS_NONE);
            RemoveFromClassList(USS_CLASS_MISSING);
        }

        private void ShowUnassigned(string _State, string _UssClass, string _TypeName)
        {
            icon.image = null;
            icon.sprite = null;
            icon.style.display = DisplayStyle.None;
            nameLabel.text = string.IsNullOrEmpty(_TypeName) ? _State : $"{_State} ({_TypeName})";

            EnableInClassList(USS_CLASS_NONE, _UssClass == USS_CLASS_NONE);
            EnableInClassList(USS_CLASS_MISSING, _UssClass == USS_CLASS_MISSING);
        }

        // SerializedProperty.type reads "PPtr<$TypeName>" for object references.
        private static string GetPropertyTypeName(SerializedProperty _Property)
        {
            string type = _Property.type;
            int start = type.IndexOf('$') + 1;
            int end = type.LastIndexOf('>');

            return start > 0 && end > start ? type[start..end] : null;
        }
    }
}
