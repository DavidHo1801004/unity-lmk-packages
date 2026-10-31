using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Serialization;
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
        private const string SHARED_INSTANCE_WARNING = "This element shares its instance with an earlier element, so editing one edits both.";

        private const string USS_CLASS_EMPTY = "empty";

        private const string MISSING_BANNER_NAME = "lmk-missing-references-banner";

        private const string ARRAY_DATA_MARKER = ".Array.data[";

        private const int MISSING_BOX_LINES = 4;

        // Script writes without SetDirty don't change the dirty count, so IMGUI re-reads after this long.
        private const double MULTI_TARGET_STATE_MAX_AGE = 0.25;

        /// <summary>
        /// Current state of the managed reference across all selected targets.
        /// </summary>
        internal readonly struct ReferenceState : IEquatable<ReferenceState>
        {
            public readonly Type type;
            public readonly bool isMixed;
            public readonly bool isShared;

            public ReferenceState(Type _Type, bool _IsMixed, bool _IsShared)
            {
                type = _Type;
                isMixed = _IsMixed;
                isShared = _IsShared;
            }

            public bool Equals(ReferenceState _Other)
            {
                return type == _Other.type && isMixed == _Other.isMixed && isShared == _Other.isShared;
            }
        }

        /// <summary>
        /// Multi-target state, reused until a target changes.
        /// Reading it needs one <see cref="SerializedObject"/> per target, which is too slow for every GUI event.
        /// </summary>
        private sealed class MultiTargetState
        {
            public readonly ReferenceState state;

            private readonly UnityEngine.Object[] targets;
            private readonly int[] dirtyCounts;
            private readonly double readTime;

            public MultiTargetState(UnityEngine.Object[] _Targets, ReferenceState _State)
            {
                state = _State;
                targets = _Targets;
                readTime = EditorApplication.timeSinceStartup;

                dirtyCounts = new int[_Targets.Length];
                for (int i = 0; i < _Targets.Length; i++)
                {
                    if (_Targets[i] != null)
                        dirtyCounts[i] = EditorUtility.GetDirtyCount(_Targets[i]);
                }
            }

            public bool IsValid()
            {
                if (EditorApplication.timeSinceStartup - readTime > MULTI_TARGET_STATE_MAX_AGE) return false;

                for (int i = 0; i < targets.Length; i++)
                {
                    if (targets[i] == null || dirtyCounts[i] != EditorUtility.GetDirtyCount(targets[i])) return false;
                }
                return true;
            }
        }

        // Entries by property path. Weak keys, so a state lives only as long as its SerializedObject.
        private static readonly ConditionalWeakTable<SerializedObject, Dictionary<string, MultiTargetState>> multiTargetStateCache = new();

        // Reference id -> index of its first element, by array. All rows refresh together after a change,
        // so each array is read once instead of once per row. Cleared on the next editor tick.
        private static readonly Dictionary<(SerializedObject, string), Dictionary<long, int>> firstElementIndices = new();

        // Reused by IMGUI for the type select button, so drawing doesn't allocate.
        private static readonly GUIContent buttonContent = new();



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
            LMKTheme.ApplyIfStandalone(root);

            // Keep the object and path instead of the property itself:
            // a captured SerializedProperty goes stale when list elements are reordered or removed.
            var serializedObject = _Property.serializedObject;
            string propertyPath = _Property.propertyPath;

            // Get all valid types for this field
            Type elementType = GetElementType(fieldInfo.FieldType);
            var typeData = InlineInstancedReflectionCache.Get(elementType);

            var typeSelectField = root.Q<DropdownField>("type-select");
            var propertyField = root.Q<PropertyField>("property-field");
            var warningBox = root.Q<HelpBox>("warning-box");
            var makeUniqueButton = root.Q<Button>("make-unique-button");

            ReferenceState? appliedState = null; // last state shown, see Refresh()
            bool showedMissingMessage = false;

            // Property field. It keeps its own label, so prefab overrides and the context menu work even when the value is None.
            // Only the path is set here: the bind of the parent field covers it, and binding now would build its content twice.
            propertyField.bindingPath = propertyPath;

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

            makeUniqueButton.clicked += () =>
            {
                MakeUnique(serializedObject, propertyPath);
                Refresh();
            };

            // Refresh on any change, including undo/redo, paste, Reset and changes made from scripts.
            // The binding system updates the serialized object before the callback, so no Update() here.
            void Refresh()
            {
                if (!TryFindProperty(serializedObject, propertyPath, out var property)) return;

                var state = GetReferenceState(property, true);

                // Depends on other fields of the object too, so it runs before the early return below.
                string missingMessage = GetMissingReferencesMessage(serializedObject);
                bool hasBanner = (missingMessage != null || showedMissingMessage) && UpdateMissingBanner(root, missingMessage);
                showedMissingMessage = missingMessage != null;
                string warning = GetRowWarning(state, hasBanner ? null : missingMessage, typeData, elementType);

                warningBox.style.display = warning != null ? DisplayStyle.Flex : DisplayStyle.None;
                warningBox.text = warning ?? string.Empty;

                if (appliedState.HasValue && appliedState.Value.Equals(state)) return; // most changes are in child fields
                appliedState = state;

                typeSelectField.showMixedValue = state.isMixed;
                typeSelectField.SetValueWithoutNotify(GetDisplayText(state, typeData, false));

                propertyField.EnableInClassList(USS_CLASS_EMPTY, state.type == null);

                makeUniqueButton.style.display = state.isShared ? DisplayStyle.Flex : DisplayStyle.None;
            }

            root.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            root.RegisterCallback<AttachToPanelEvent>(_ => Refresh()); // the banner needs the inspector this row ends up in
            Refresh();

            return root;
        }

        /// <summary>
        /// Warning displayed in the row of this field, or <see langword="null"/> for none.
        /// </summary>
        /// <param name="_MissingMessage"> Missing references warning to display on empty rows, if no banner displays it. </param>
        private static string GetRowWarning(ReferenceState _State, string _MissingMessage,
            InlineInstancedTypeCacheData _TypeData, Type _ElementType)
        {
            if (_State.isShared) return SHARED_INSTANCE_WARNING;
            if (_State.isMixed || _State.type != null) return null;
            if (_MissingMessage != null) return _MissingMessage;

            return _TypeData.isValid ? null : GetNoExposedTypesMessage(_ElementType);
        }

        /// <summary>
        /// Display <paramref name="_Message"/> once per inspector, at its top, instead of once per field:
        /// a missing reference can't be traced back to the field which holds it.
        /// </summary>
        /// <param name="_Message"> Banner text, or <see langword="null"/> to remove the banner. </param>
        /// <returns> False if <paramref name="_Root"/> isn't inside an inspector, so no banner can be used. </returns>
        private static bool UpdateMissingBanner(VisualElement _Root, string _Message)
        {
            var inspector = _Root.GetFirstAncestorOfType<InspectorElement>();
            if (inspector == null) return false;

            // Only the first child is checked: every row of the inspector runs this on every change.
            var banner = inspector.childCount > 0 && inspector[0].name == MISSING_BANNER_NAME
                ? (HelpBox)inspector[0]
                : null;

            if (_Message == null)
            {
                banner?.RemoveFromHierarchy();
                return true;
            }

            if (banner == null)
            {
                banner = new HelpBox(_Message, HelpBoxMessageType.Warning) { name = MISSING_BANNER_NAME };
                inspector.Insert(0, banner);
            }
            else if (banner.text != _Message)
            {
                banner.text = _Message;
            }
            return true;
        }
        #endregion



        #region IMGUI
        // Fallback for IMGUI inspectors: custom Editor.OnInspectorGUI, EditorGUILayout.PropertyField, Odin, etc.
        // A shared parent to hold one warning doesn't exist here, so each empty row displays it.
        // Shared instances aren't detected: comparing elements on every GUI event is too slow.
        public override float GetPropertyHeight(SerializedProperty _Property, GUIContent _Label)
        {
            float height = EditorGUI.GetPropertyHeight(_Property, _Label, true);

            if (_Property.propertyType == SerializedPropertyType.ManagedReference
                && ShowsMissingWarning(GetReferenceState(_Property, false), _Property.serializedObject))
            {
                height += EditorGUIUtility.singleLineHeight * MISSING_BOX_LINES + EditorGUIUtility.standardVerticalSpacing;
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
            var state = GetReferenceState(_Property, false);

            // Missing references warning, drawn below the field
            if (ShowsMissingWarning(state, _Property.serializedObject))
            {
                float boxHeight = EditorGUIUtility.singleLineHeight * MISSING_BOX_LINES;
                Rect boxRect = new(_Position.x, _Position.yMax - boxHeight, _Position.width, boxHeight);
                EditorGUI.HelpBox(boxRect, GetMissingReferencesMessage(_Property.serializedObject), MessageType.Warning);
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

        private static bool ShowsMissingWarning(ReferenceState _State, SerializedObject _SerializedObject)
        {
            return !_State.isMixed && _State.type == null && HasMissingReferences(_SerializedObject);
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

        private static string GetNoExposedTypesMessage(Type _ElementType)
        {
            return $"No class can be assigned to {_ElementType.Name}. "
                + "Classes must be [Serializable] and exposed with [CanReferenceInline].";
        }

        private static bool HasMissingReferences(SerializedObject _SerializedObject)
        {
            if (!_SerializedObject.isEditingMultipleObjects)
            {
                var target = _SerializedObject.targetObject;
                return target != null && SerializationUtility.HasManagedReferencesWithMissingTypes(target);
            }

            foreach (var target in _SerializedObject.targetObjects)
            {
                if (target != null && SerializationUtility.HasManagedReferencesWithMissingTypes(target)) return true;
            }
            return false;
        }

        /// <summary>
        /// Warning listing the missing classes referenced by the selected targets.
        /// </summary>
        /// <returns> The warning, or <see langword="null"/> if no reference is missing. </returns>
        internal static string GetMissingReferencesMessage(SerializedObject _SerializedObject)
        {
            if (!HasMissingReferences(_SerializedObject)) return null;

            int referenceCount = 0;
            var typeNames = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var target in _SerializedObject.targetObjects)
            {
                if (target == null) continue;

                foreach (var missingType in SerializationUtility.GetManagedReferencesWithMissingTypes(target))
                {
                    referenceCount++;
                    typeNames.Add(string.IsNullOrEmpty(missingType.namespaceName)
                        ? missingType.className
                        : $"{missingType.namespaceName}.{missingType.className}");
                }
            }

            return $"{referenceCount} reference{(referenceCount == 1 ? "" : "s")} to missing "
                + $"type{(typeNames.Count == 1 ? "" : "s")}: {string.Join(", ", typeNames)}. Fields using them display None, "
                + "and saving can permanently remove their data. Restore the class, or add [MovedFrom] to it if it was renamed or moved.";
        }

        /// <summary>
        /// Read the reference type of <paramref name="_Property"/> on every selected target.
        /// </summary>
        /// <param name="_Fresh">
        /// Whether to read the current state, and check for a shared instance.
        /// Otherwise a multi-target state may be reused, which suits callers that run on every GUI event.
        /// </param>
        internal static ReferenceState GetReferenceState(SerializedProperty _Property, bool _Fresh)
        {
            var serializedObject = _Property.serializedObject;
            if (!serializedObject.isEditingMultipleObjects)
            {
                Type type = _Property.managedReferenceValue?.GetType();
                return new ReferenceState(type, false, _Fresh && IsSharedWithEarlierElement(_Property));
            }

            string propertyPath = _Property.propertyPath;
            if (_Fresh) return ReadMultiTargetState(serializedObject.targetObjects, propertyPath);

            var states = multiTargetStateCache.GetOrCreateValue(serializedObject);
            if (states.TryGetValue(propertyPath, out var cached) && cached.IsValid())
            {
                return cached.state;
            }

            var targets = serializedObject.targetObjects;
            var state = ReadMultiTargetState(targets, propertyPath);
            states[propertyPath] = new MultiTargetState(targets, state);
            return state;
        }

        private static ReferenceState ReadMultiTargetState(UnityEngine.Object[] _Targets, string _PropertyPath)
        {
            ReferenceState first = default;
            for (int i = 0; i < _Targets.Length; i++)
            {
                if (_Targets[i] == null) return new ReferenceState(null, true, false); // destroyed while selected

                using var targetObject = new SerializedObject(_Targets[i]);
                var targetProperty = targetObject.FindProperty(_PropertyPath);
                if (targetProperty == null) return new ReferenceState(null, true, false);

                Type type = targetProperty.managedReferenceValue?.GetType();
                if (i == 0)
                    first = new ReferenceState(type, false, false);
                else if (type != first.type)
                    return new ReferenceState(null, true, false);
            }
            return first;
        }

        /// <summary>
        /// Whether <paramref name="_Property"/> is a list element holding the same instance as an earlier element,
        /// which is what Unity's Duplicate Array Element command leaves behind.
        /// </summary>
        private static bool IsSharedWithEarlierElement(SerializedProperty _Property)
        {
            long referenceId = _Property.managedReferenceId;
            if (referenceId == ManagedReferenceUtility.RefIdNull || referenceId == ManagedReferenceUtility.RefIdUnknown) return false;

            string path = _Property.propertyPath;
            int markerIndex = path.LastIndexOf(ARRAY_DATA_MARKER, StringComparison.Ordinal);
            if (markerIndex < 0 || path[^1] != ']') return false; // not a list element

            string indexText = path[(markerIndex + ARRAY_DATA_MARKER.Length)..^1];
            if (!int.TryParse(indexText, out int elementIndex) || elementIndex == 0) return false;

            var firstIndices = GetFirstElementIndices(_Property.serializedObject, path[..markerIndex]);
            return firstIndices.TryGetValue(referenceId, out int firstIndex) && firstIndex < elementIndex;
        }

        private static Dictionary<long, int> GetFirstElementIndices(SerializedObject _SerializedObject, string _ArrayPath)
        {
            var key = (_SerializedObject, _ArrayPath);
            if (firstElementIndices.TryGetValue(key, out var indices)) return indices;

            if (firstElementIndices.Count == 0)
                EditorApplication.delayCall += firstElementIndices.Clear;

            indices = new Dictionary<long, int>();
            var array = _SerializedObject.FindProperty(_ArrayPath);
            if (array != null && array.arraySize > 0)
            {
                var element = array.GetArrayElementAtIndex(0);
                int arraySize = array.arraySize;
                for (int i = 0; i < arraySize; i++)
                {
                    indices.TryAdd(element.managedReferenceId, i);
                    if (!element.Next(false)) break;
                }
            }

            firstElementIndices[key] = indices;
            return indices;
        }

        /// <summary>
        /// Create an instance of <paramref name="_Type"/>, reporting an exception thrown by its constructor.
        /// </summary>
        /// <param name="_Instance"> The new instance, or <see langword="null"/> if <paramref name="_Type"/> is. </param>
        /// <returns> False if the constructor failed. </returns>
        private static bool TryCreateInstance(Type _Type, out object _Instance)
        {
            _Instance = null;
            if (_Type == null) return true;

            try
            {
                _Instance = Activator.CreateInstance(_Type, true);
                return true;
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
                return false;
            }
        }

        private static int BeginUndoGroup(string _Name)
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(_Name);
            return undoGroup;
        }

        /// <summary>
        /// Assign a new instance of <paramref name="_Type"/> (or null) to the property on every selected target.
        /// Each target gets its own instance. Targets already of that type keep theirs, along with its values.
        /// Nothing is written if a constructor fails.
        /// </summary>
        internal static void AssignType(SerializedObject _SerializedObject, string _PropertyPath, Type _Type)
        {
            if (!TryFindProperty(_SerializedObject, _PropertyPath, out var property)) return;

            firstElementIndices.Clear(); // the caller refreshes right after this, in the same tick
            string undoName = $"Set {_PropertyPath} to {(_Type == null ? "None" : _Type.Name)}";

            if (!_SerializedObject.isEditingMultipleObjects)
            {
                if (property.managedReferenceValue?.GetType() == _Type) return;
                if (!TryCreateInstance(_Type, out object instance)) return;

                // Write through the inspector's own object, so its pending changes are applied, not discarded.
                int undoGroup = BeginUndoGroup(undoName);
                property.managedReferenceValue = instance;
                _SerializedObject.ApplyModifiedProperties();
                Undo.CollapseUndoOperations(undoGroup);
                return;
            }

            // Every constructor runs before the first write, so a failure can't leave only some targets changed.
            var pendingWrites = new List<(UnityEngine.Object target, object instance)>();
            foreach (var target in _SerializedObject.targetObjects)
            {
                if (target == null) continue;

                using var targetObject = new SerializedObject(target);
                var targetProperty = targetObject.FindProperty(_PropertyPath);
                if (targetProperty == null || targetProperty.managedReferenceValue?.GetType() == _Type) continue;

                if (!TryCreateInstance(_Type, out object instance)) return;
                pendingWrites.Add((target, instance));
            }
            if (pendingWrites.Count == 0) return;

            int multiUndoGroup = BeginUndoGroup(undoName);

            // A multi-object property sets the same instance on every target, so write each target separately.
            foreach (var (target, instance) in pendingWrites)
            {
                using var targetObject = new SerializedObject(target);
                targetObject.FindProperty(_PropertyPath).managedReferenceValue = instance;
                targetObject.ApplyModifiedProperties();
            }
            _SerializedObject.Update();

            Undo.CollapseUndoOperations(multiUndoGroup);
        }

        /// <summary>
        /// Replace the instance of <paramref name="_PropertyPath"/> with a copy of its own, for an element
        /// which shares it with another one.
        /// </summary>
        internal static void MakeUnique(SerializedObject _SerializedObject, string _PropertyPath)
        {
            if (!TryFindProperty(_SerializedObject, _PropertyPath, out var property)) return;

            object source = property.managedReferenceValue;
            if (source == null || !TryCreateInstance(source.GetType(), out object copy)) return;

            EditorUtility.CopySerializedManagedFieldsOnly(source, copy);

            firstElementIndices.Clear(); // the caller refreshes right after this, in the same tick
            int undoGroup = BeginUndoGroup($"Make {_PropertyPath} unique");
            property.managedReferenceValue = copy;
            _SerializedObject.ApplyModifiedProperties();
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
