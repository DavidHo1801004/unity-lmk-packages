using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Registry;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Inspector field of a <see cref="GameplayAttribute"/>: its current name, "&lt;name&gt; (deleted)" when it doesn't
    /// resolve, or "None". A click opens the attribute picker. While any selected object holds a missing attribute, a
    /// click offers only to clear it.
    /// </summary>
    [CustomPropertyDrawer(typeof(GameplayAttribute))]
    internal sealed class GameplayAttributeDrawer : PropertyDrawer
    {
        public const string USS_CLASS = "gameplay-attribute-field";
        private const string NONE = "None";



        #region UI Toolkit
        public override VisualElement CreatePropertyGUI(SerializedProperty _Property)
        {
            // Drawers are shared between properties, so all state lives in this method.
            var field = new AttributeFieldElement(_Property.displayName);
            field.AddToClassList(USS_CLASS);
            string propertyPath = _Property.propertyPath;

            void Refresh()
            {
                if (!RegistryDrawerHelper.IsAlive(_Property)) return;

                _Property.serializedObject.UpdateIfRequiredOrScript();
                var (text, tooltip, missing) = Describe(_Property.serializedObject.FindProperty(propertyPath));
                field.SetDisplay(_Property.hasMultipleDifferentValues ? "—" : text, tooltip, missing);
            }

            field.Clicked += () =>
            {
                if (!RegistryDrawerHelper.IsAlive(_Property)) return;
                OnClick(_Property.serializedObject, propertyPath, field.Button.worldBound, Refresh);
            };

            field.TrackPropertyValue(_Property, _ => Refresh());
            field.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                AttributeKind.Instance.SnapshotChanged += Refresh;
                Refresh();
            });
            field.RegisterCallback<DetachFromPanelEvent>(_ => AttributeKind.Instance.SnapshotChanged -= Refresh);

            Refresh();
            return field;
        }

        /// <summary>
        /// A label and a dropdown-styled button, aligned like the inspector's other fields.
        /// </summary>
        private sealed class AttributeFieldElement : BaseField<string>
        {
            public readonly Button Button;
            public event System.Action Clicked;

            public AttributeFieldElement(string _Label) : base(_Label, new VisualElement())
            {
                AddToClassList(alignedFieldUssClassName);

                var input = this.Q(className: inputUssClassName);
                input.style.flexDirection = FlexDirection.Row;

                Button = new Button(() => Clicked?.Invoke()) { name = "attribute-button" };
                Button.AddToClassList(USS_CLASS + "__button");
                Button.style.flexGrow = 1;
                Button.style.marginLeft = 0;
                Button.style.marginRight = 0;
                Button.style.unityTextAlign = TextAnchor.MiddleLeft;
                input.Add(Button);
            }

            public void SetDisplay(string _Text, string _Tooltip, bool _Missing)
            {
                Button.text = _Text;
                Button.tooltip = _Tooltip;
                Button.EnableInClassList(USS_CLASS + "--missing", _Missing);
                Button.style.color = _Missing ? new StyleColor(new Color(0.95f, 0.45f, 0.4f)) : StyleKeyword.Null;
            }
        }
        #endregion



        #region IMGUI
        public override void OnGUI(Rect _Position, SerializedProperty _Property, GUIContent _Label)
        {
            EditorGUI.BeginProperty(_Position, _Label, _Property);
            var buttonRect = EditorGUI.PrefixLabel(_Position, _Label);

            var (text, tooltip, _) = Describe(_Property);
            EditorGUI.showMixedValue = _Property.hasMultipleDifferentValues;
            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(text, tooltip), FocusType.Keyboard))
                OnClick(_Property.serializedObject, _Property.propertyPath, buttonRect, null);
            EditorGUI.showMixedValue = false;

            EditorGUI.EndProperty();
        }
        #endregion



        #region Shared
        internal static (string Text, string Tooltip, bool Missing) Describe(SerializedProperty _Property)
        {
            var reference = RegistryProperties.Read(_Property, AttributeKind.Instance);
            if (reference.Id.IsZero) return (NONE, "No attribute.", false);

            var status = AttributeKind.Instance.GetStatus(reference.Registry, reference.Id);
            if (status == RegistryStatus.Live)
            {
                string name = AttributeKind.Instance.GetRowName(reference.Registry, reference.Id) ?? reference.Name;
                return (name, $"{name}\nRegistry: {AttributeKind.Instance.Snapshot.Find(reference.Registry)?.Name}", false);
            }

            return (RegistryDrawerHelper.GetMissingLabel(reference), RegistryDrawerHelper.GetMissingTooltip(reference, status), true);
        }

        private static void OnClick(SerializedObject _SerializedObject, string _PropertyPath, Rect _Anchor, System.Action _Refresh)
        {
            var targets = _SerializedObject.targetObjects;

            int missing = RegistryDrawerHelper.CountMissing(targets, _PropertyPath, false);
            if (missing > 0)
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent(missing == 1 ? "Clear missing attribute" : $"Clear {missing} missing attributes"), false, () =>
                {
                    RegistryDrawerHelper.RemoveMissing(targets, _PropertyPath, false, "Clear Missing Gameplay Attribute");
                    _SerializedObject.Update();
                    _Refresh?.Invoke();
                });
                menu.ShowAsContext();
                return;
            }

            var property = _SerializedObject.FindProperty(_PropertyPath);
            var current = RegistryProperties.Read(property, AttributeKind.Instance);

            AttributePicker.Show(_Anchor, GameplayAttribute.FromId(current.Registry, current.Id, current.Name), true, picked =>
            {
                if (_SerializedObject.targetObject == null) return;

                _SerializedObject.Update();
                var target = _SerializedObject.FindProperty(_PropertyPath);
                RegistryProperties.Write(target, picked.RegistryId, picked.Id, picked.Name);
                _SerializedObject.ApplyModifiedProperties();
                _Refresh?.Invoke();
            });
        }
        #endregion
    }
}
