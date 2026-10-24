using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;
using LMK.Editor.Windows;
using LMK.Runtime.Attributes;

namespace LMK.Editor.Attributes
{
    [CustomPropertyDrawer(typeof(InlineInstancedAttribute), true)]
    public class InlineInstancedAttributeDrawer : PropertyDrawer
    {
        private const string UXML_PATH = "Packages/com.lmk.attributes/Editor/Attributes/Assets/InlineInstancedDrawerUXML.uxml";
        private const string INVALID_TYPE_ERROR = "Inline attribute only works on SerializeReference fields.";

        private const string USS_CLASS_INVALID = "invalid";
        private const string USS_CLASS_MISSING = "missing";
        private const string USS_CLASS_EMPTY = "empty";

        /// <summary>
        /// Current state of the managed reference across all selected targets.
        /// </summary>
        private readonly struct ReferenceState : IEquatable<ReferenceState>
        {
            public readonly Type type;
            public readonly string missingTypeName;
            public readonly bool isMixed;

            public bool IsMissing => missingTypeName != null;

            public ReferenceState(Type _Type, string _MissingTypeName, bool _IsMixed)
            {
                type = _Type;
                missingTypeName = _MissingTypeName;
                isMixed = _IsMixed;
            }

            public bool Equals(ReferenceState _Other)
            {
                return type == _Other.type && missingTypeName == _Other.missingTypeName && isMixed == _Other.isMixed;
            }
        }

        /// <summary>
        /// Multi-target state, reused until a target changes.
        /// Reading it needs one <see cref="SerializedObject"/> per target, which is too slow for every GUI event.
        /// </summary>
        private sealed class MultiTargetState
        {
            public UnityEngine.Object[] targets;
            public int[] dirtyCounts;
            public ReferenceState state;

            public bool IsValidFor(UnityEngine.Object[] _Targets)
            {
                if (targets.Length != _Targets.Length) return false;

                for (int i = 0; i < targets.Length; i++)
                {
                    if (targets[i] != _Targets[i]) return false;
                    if (dirtyCounts[i] != EditorUtility.GetDirtyCount(_Targets[i])) return false;
                }
                return true;
            }
        }

        private static readonly Dictionary<(UnityEngine.Object, string), MultiTargetState> multiTargetStateCache = new();

        // Reused by IMGUI for the type select button, so drawing doesn't allocate.
        private static readonly GUIContent buttonContent = new();

        [InitializeOnLoadMethod]
        private static void RegisterCacheCleanup()
        {
            Selection.selectionChanged += multiTargetStateCache.Clear;
        }



        #region UI Toolkit
        public override VisualElement CreatePropertyGUI(SerializedProperty _Property)
        {
            // Handle invalid property type.
            if (_Property.propertyType != SerializedPropertyType.ManagedReference)
            {
                return new Label($"Error: {INVALID_TYPE_ERROR}");
            }

            var uxml = LMKAssetUtility.Load<VisualTreeAsset>(UXML_PATH);
            if (!uxml)
            {
                return new Label($"Error: Missing UXML for {GetType().Name} at \"{UXML_PATH}\".");
            }

            // Draw default inline reference selector.
            return DrawInlineReferenceSelector(_Property, uxml);
        }

        /// <summary>
        /// Main drawer method.
        /// </summary>
        private VisualElement DrawInlineReferenceSelector(SerializedProperty _Property, VisualTreeAsset _Uxml)
        {
            var root = _Uxml.Instantiate();
            LMKTheme.Apply(root);

            // Keep the object and path instead of the property itself:
            // a captured SerializedProperty goes stale when list elements are reordered or removed.
            var serializedObject = _Property.serializedObject;
            string propertyPath = _Property.propertyPath;

            // Get all valid types for this field
            var typeData = InlineInstancedReflectionCache.Get(GetElementType(fieldInfo.FieldType));

            var typeSelectField = root.Q<DropdownField>("type-select");
            var propertyField = root.Q<PropertyField>("property-field");
            var missingTypeBox = root.Q<HelpBox>("missing-type-box");

            ReferenceState? appliedState = null; // last state shown, see Refresh()

            // Property field. It keeps its own label, so prefab overrides and the context menu work even when the value is None.
            propertyField.BindProperty(_Property);

            // Type select dropdown, opened by mouse or keyboard
            void OpenDropdown()
            {
                var dropdownContent = new SearchableDropdown(typeData.menuItems, typeData.menuNamespaces);
                dropdownContent.OnSelectedItemChanged += (index) =>
                {
                    Type selectedType = index == 0 ? null : typeData.exposedTypes[index - 1];
                    AssignType(serializedObject, propertyPath, selectedType);
                    Refresh();
                };

                UnityEditor.PopupWindow.Show(typeSelectField.worldBound, dropdownContent);
            }

            typeSelectField.RegisterCallback<PointerDownEvent>((e) =>
            {
                if (e.button != 0) return;

                e.StopPropagation(); // don't let the DropdownField open its own empty menu
                OpenDropdown();
            }, TrickleDown.TrickleDown);
            typeSelectField.RegisterCallback<NavigationSubmitEvent>((e) =>
            {
                e.StopPropagation();
                OpenDropdown();
            }, TrickleDown.TrickleDown);

            // Refresh on any change, including undo/redo, paste, Reset and changes made from scripts.
            // The binding system updates the serialized object before the callback, so no Update() here.
            void Refresh()
            {
                if (!TryFindProperty(serializedObject, propertyPath, out var property)) return;

                var state = GetReferenceState(property);
                if (appliedState.HasValue && appliedState.Value.Equals(state)) return; // most changes are in child fields
                appliedState = state;

                typeSelectField.showMixedValue = state.isMixed;
                typeSelectField.SetValueWithoutNotify(GetDisplayText(state, typeData, false));
                typeSelectField.EnableInClassList(USS_CLASS_INVALID, !state.isMixed && state.type == null && !state.IsMissing);
                typeSelectField.EnableInClassList(USS_CLASS_MISSING, state.IsMissing);

                propertyField.EnableInClassList(USS_CLASS_EMPTY, state.type == null);

                missingTypeBox.style.display = state.IsMissing ? DisplayStyle.Flex : DisplayStyle.None;
                if (state.IsMissing)
                    missingTypeBox.text = GetMissingTypeMessage(state.missingTypeName);
            }

            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            Refresh();

            return root;
        }
        #endregion



        #region IMGUI
        // Fallback for IMGUI inspectors: custom Editor.OnInspectorGUI, EditorGUILayout.PropertyField, Odin, etc.
        public override float GetPropertyHeight(SerializedProperty _Property, GUIContent _Label)
        {
            float height = EditorGUI.GetPropertyHeight(_Property, _Label, true);

            if (_Property.propertyType == SerializedPropertyType.ManagedReference
                && GetReferenceState(_Property).IsMissing)
            {
                height += EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;
            }
            return height;
        }

        public override void OnGUI(Rect _Position, SerializedProperty _Property, GUIContent _Label)
        {
            if (_Property.propertyType != SerializedPropertyType.ManagedReference)
            {
                EditorGUI.LabelField(_Position, _Label.text, INVALID_TYPE_ERROR);
                return;
            }

            var typeData = InlineInstancedReflectionCache.Get(GetElementType(fieldInfo.FieldType));
            var state = GetReferenceState(_Property);

            // Missing type warning, drawn below the field
            if (state.IsMissing)
            {
                float boxHeight = EditorGUIUtility.singleLineHeight * 2;
                Rect boxRect = new(_Position.x, _Position.yMax - boxHeight, _Position.width, boxHeight);
                EditorGUI.HelpBox(boxRect, GetMissingTypeMessage(state.missingTypeName), MessageType.Warning);
                _Position.height -= boxHeight + EditorGUIUtility.standardVerticalSpacing;
            }

            // Type select button. Drawn before the property field so it receives the click first.
            float labelWidth = EditorGUIUtility.labelWidth + 2f;
            Rect buttonRect = new(
                _Position.x + labelWidth,
                _Position.y,
                Mathf.Max(0f, _Position.width - labelWidth),
                EditorGUIUtility.singleLineHeight);

            buttonContent.text = GetDisplayText(state, typeData, true);
            using (new EditorGUI.MixedValueScope(state.isMixed))
            {
                if (EditorGUI.DropdownButton(buttonRect, buttonContent, FocusType.Keyboard))
                {
                    var serializedObject = _Property.serializedObject;
                    string propertyPath = _Property.propertyPath;

                    int currentIndex = state.isMixed ? -1 : GetMenuIndex(state.type, typeData);
                    var menu = new GenericMenu();
                    for (int i = 0; i < typeData.menuItems.Length; i++)
                    {
                        Type itemType = i == 0 ? null : typeData.exposedTypes[i - 1];
                        GUIContent content = new(typeData.plainMenuTexts[i], typeData.menuItems[i].tooltip);
                        menu.AddItem(content, i == currentIndex, () => AssignType(serializedObject, propertyPath, itemType));
                    }
                    menu.DropDown(buttonRect);
                }
            }

            // Nested call draws the default field for this property, not this drawer again.
            EditorGUI.PropertyField(_Position, _Property, _Label, true);
        }
        #endregion



        #region Drawer Utilities
        /// <summary>
        /// Text displayed in the type select field for the given state.
        /// </summary>
        /// <param name="_Plain"> Whether to use the text without rich text tags, for IMGUI. </param>
        private static string GetDisplayText(ReferenceState _State, InlineInstancedTypeCacheData _TypeData, bool _Plain)
        {
            if (_State.isMixed) return string.Empty;
            if (_State.IsMissing) return $"Missing: {_State.missingTypeName}";

            int index = GetMenuIndex(_State.type, _TypeData);
            if (index >= 0) return _Plain ? _TypeData.plainMenuTexts[index] : _TypeData.menuItems[index].text;

            // Assigned from code with a type that is not exposed in the dropdown.
            return _State.type.Name;
        }

        /// <summary>
        /// Menu index of <paramref name="_Type"/>: 0 for None, or -1 if the type isn't exposed.
        /// </summary>
        private static int GetMenuIndex(Type _Type, InlineInstancedTypeCacheData _TypeData)
        {
            if (_Type == null) return 0;

            int index = _TypeData.IndexOf(_Type);
            return index >= 0 ? index + 1 : -1;
        }

        private static string GetMissingTypeMessage(string _MissingTypeName)
        {
            return $"Type \"{_MissingTypeName}\" can't be found. Its data is kept until another type is selected. "
                + "If the class was renamed or moved, add [MovedFrom] to it.";
        }

        /// <summary>
        /// Read the reference type of <paramref name="_Property"/> on every selected target.
        /// </summary>
        private static ReferenceState GetReferenceState(SerializedProperty _Property)
        {
            var serializedObject = _Property.serializedObject;
            if (!serializedObject.isEditingMultipleObjects)
            {
                return GetReferenceState(_Property, serializedObject.targetObject);
            }

            var targets = serializedObject.targetObjects;
            var key = (targets[0], _Property.propertyPath);
            if (multiTargetStateCache.TryGetValue(key, out var cached) && cached.IsValidFor(targets))
            {
                return cached.state;
            }

            var state = ReadMultiTargetState(targets, key.propertyPath);

            var dirtyCounts = new int[targets.Length];
            for (int i = 0; i < targets.Length; i++)
                dirtyCounts[i] = EditorUtility.GetDirtyCount(targets[i]);

            multiTargetStateCache[key] = new MultiTargetState { targets = targets, dirtyCounts = dirtyCounts, state = state };
            return state;
        }

        private static ReferenceState ReadMultiTargetState(UnityEngine.Object[] _Targets, string _PropertyPath)
        {
            ReferenceState first = default;
            for (int i = 0; i < _Targets.Length; i++)
            {
                using var targetObject = new SerializedObject(_Targets[i]);
                var targetProperty = targetObject.FindProperty(_PropertyPath);
                if (targetProperty == null) return new ReferenceState(null, null, true);

                var state = GetReferenceState(targetProperty, _Targets[i]);
                if (i == 0)
                    first = state;
                else if (state.type != first.type || state.missingTypeName != first.missingTypeName)
                    return new ReferenceState(null, null, true);
            }
            return first;
        }

        private static ReferenceState GetReferenceState(SerializedProperty _Property, UnityEngine.Object _Target)
        {
            object value = _Property.managedReferenceValue;
            if (value != null) return new ReferenceState(value.GetType(), null, false);

            // A null value may still hold data for a type that no longer exists.
            if (_Target != null && SerializationUtility.HasManagedReferencesWithMissingTypes(_Target))
            {
                long referenceId = _Property.managedReferenceId;
                foreach (var missingType in SerializationUtility.GetManagedReferencesWithMissingTypes(_Target))
                {
                    if (missingType.referenceId != referenceId) continue;

                    string typeName = string.IsNullOrEmpty(missingType.namespaceName)
                        ? missingType.className
                        : $"{missingType.namespaceName}.{missingType.className}";
                    return new ReferenceState(null, typeName, false);
                }
            }

            return new ReferenceState(null, null, false);
        }

        /// <summary>
        /// Assign a new instance of <paramref name="_Type"/> (or null) to the property on every selected target.
        /// Each target gets its own instance.
        /// </summary>
        private static void AssignType(SerializedObject _SerializedObject, string _PropertyPath, Type _Type)
        {
            if (!TryFindProperty(_SerializedObject, _PropertyPath, out var property)) return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Set {_PropertyPath} to {(_Type == null ? "None" : _Type.Name)}");

            if (!_SerializedObject.isEditingMultipleObjects)
            {
                // Write through the inspector's own object, so its pending changes are applied, not discarded.
                property.managedReferenceValue = _Type == null ? null : Activator.CreateInstance(_Type, true);
                _SerializedObject.ApplyModifiedProperties();
            }
            else
            {
                // A multi-object property sets the same instance on every target, so write each target separately.
                foreach (var target in _SerializedObject.targetObjects)
                {
                    using var targetObject = new SerializedObject(target);
                    var targetProperty = targetObject.FindProperty(_PropertyPath);
                    if (targetProperty == null) continue;

                    targetProperty.managedReferenceValue = _Type == null ? null : Activator.CreateInstance(_Type, true);
                    targetObject.ApplyModifiedProperties();
                }
                _SerializedObject.Update();
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        /// <summary>
        /// Find the property again from its object, failing safely if the object was disposed or the path no longer exists.
        /// </summary>
        private static bool TryFindProperty(SerializedObject _SerializedObject, string _PropertyPath, out SerializedProperty _Property)
        {
            _Property = null;
            try
            {
                if (_SerializedObject == null || _SerializedObject.targetObject == null) return false;

                _Property = _SerializedObject.FindProperty(_PropertyPath);
                return _Property != null && _Property.propertyType == SerializedPropertyType.ManagedReference;
            }
            catch (Exception) // disposed SerializedObject
            {
                return false;
            }
        }

        /// <summary>
        /// Handle collections to retrieve element type, or field type for single field.
        /// </summary>
        /// <param name="_FieldType"> Type of the given field. </param>
        private static Type GetElementType(Type _FieldType)
        {
            // Handle native arrays
            if (_FieldType.IsArray)
                return _FieldType.GetElementType();

            // Handle generic collections
            if (_FieldType.IsGenericType)
            {
                Type genericDef = _FieldType.GetGenericTypeDefinition();
                if (genericDef == typeof(List<>))
                    return _FieldType.GetGenericArguments()[0];
            }

            return _FieldType;
        }
        #endregion
    }
}
