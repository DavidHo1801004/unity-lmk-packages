using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Registry;
using LMK.Editor.UIElements;
using LMK.Runtime.AbilitySystem;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// A <see cref="PropertyList"/> of attribute entries (an array of structs whose <c>attribute</c> field is a
    /// <see cref="GameplayAttribute"/>), with each attribute at most once. Add opens <see cref="AttributeDropdown"/> with
    /// only the attributes not listed yet, so the list never holds an empty or duplicate attribute, and is disabled once
    /// every attribute is listed. Entries can be reordered and removed, with Undo.
    /// </summary>
    internal static class AttributeEntryList
    {
        private const string ATTRIBUTE_FIELD = "attribute";

        private const string ROW_USS_CLASS = "lmk-asset-inspector__attribute-row";
        private const string NAME_USS_CLASS = "lmk-asset-inspector__attribute-name";
        private const string VALUE_USS_CLASS = "lmk-asset-inspector__attribute-value";

        private const string ALL_LISTED = "Every attribute is already listed";

        /// <param name="_ArrayPath"> Path of the entry array, e.g. <c>entries</c>. </param>
        /// <param name="_ValueField"> Field of an entry drawn next to its attribute, or <see langword="null"/> for none. </param>
        /// <param name="_EmptyText"> Shown while the list is empty. </param>
        public static PropertyList Create(VisualElement _Parent, SerializedObject _SerializedObject, string _ArrayPath, string _ValueField,
                                          Type _Owner, string _Title, string _EmptyText)
        {
            var list = InspectorSection.List(_Parent, _SerializedObject, _ArrayPath, _Owner, _Title, _EmptyText);
            string undoName = $"Add Attribute ({_Title})"; // as PropertyList's "Add Element (<title>)"

            list.makeItem = () =>
            {
                var row = new VisualElement();
                row.AddToClassList(ROW_USS_CLASS);
                row.EnableInClassList(ROW_USS_CLASS + "--with-value", _ValueField != null);
                return row;
            };
            list.bindItem = (_Row, _Entry) => BindRow(_Row, _Entry, _ValueField);

            list.AddRequested = _Anchor =>
            {
                var filter = AttributeDropdown.Excluding(Listed(_SerializedObject.targetObjects, _ArrayPath));
                AttributeDropdown.Show(_Anchor, GameplayAttribute.None, false, _Picked =>
                {
                    Add(_SerializedObject, _ArrayPath, _ValueField, _Picked, undoName);
                    list.Refresh();
                }, filter);
            };

            void UpdateAddButton()
            {
                if (!IsAlive(_SerializedObject)) return;

                var filter = AttributeDropdown.Excluding(Listed(_SerializedObject.targetObjects, _ArrayPath));
                list.SetAddEnabled(AttributeDropdown.Any(filter), ALL_LISTED);
            }

            // Registry edits change the names and the attributes left to add. Value edits don't rebuild the rows:
            // their fields are bound and update themselves. An attribute changed in place (another inspector, a
            // script) isn't a structural change for the list, so it is caught here.
            string shownSignature = null;
            list.ItemsChanged += () =>
            {
                shownSignature = IsAlive(_SerializedObject) ? Signature(_SerializedObject.FindProperty(_ArrayPath)) : null;
                UpdateAddButton();
            };

            var tracker = new VisualElement { name = "attribute-entry-tracker" };
            tracker.style.display = DisplayStyle.None;
            list.Add(tracker);
            tracker.TrackPropertyValue(_SerializedObject.FindProperty(_ArrayPath), _ =>
            {
                if (IsAlive(_SerializedObject) && Signature(_SerializedObject.FindProperty(_ArrayPath)) != shownSignature)
                    list.Refresh();
            });

            void OnRegistriesChanged()
            {
                if (IsAlive(_SerializedObject)) list.Refresh();
            }
            list.RegisterCallback<AttachToPanelEvent>(_ => AttributeKind.Instance.SnapshotChanged += OnRegistriesChanged);
            list.RegisterCallback<DetachFromPanelEvent>(_ => AttributeKind.Instance.SnapshotChanged -= OnRegistriesChanged);

            list.Refresh(); // bindItem is set after the constructor bound the list
            return list;
        }



        #region Rows
        private static void BindRow(VisualElement _Row, SerializedProperty _Entry, string _ValueField)
        {
            var attribute = _Entry.FindPropertyRelative(ATTRIBUTE_FIELD);
            var (text, tooltip, missing) = GameplayAttributeDrawer.Describe(attribute);
            bool mixed = attribute.hasMultipleDifferentValues;

            var name = new Label(mixed ? "—" : text) { tooltip = mixed ? "The selected objects list different attributes here." : tooltip };
            name.AddToClassList(NAME_USS_CLASS);
            name.EnableInClassList(NAME_USS_CLASS + "--missing", missing && !mixed);
            _Row.Add(name);

            if (_ValueField != null)
                _Row.Add(ScalableFloatInput(_Entry.FindPropertyRelative(_ValueField)));
        }

        // A ScalableFloat on one line: the value, and the level curve that scales it.
        private static VisualElement ScalableFloatInput(SerializedProperty _ScalableFloat)
        {
            var input = new VisualElement();
            input.AddToClassList(VALUE_USS_CLASS);

            var number = new FloatField { tooltip = "The starting value." };
            number.AddToClassList(VALUE_USS_CLASS + "-number");
            number.BindProperty(_ScalableFloat.FindPropertyRelative("value"));
            input.Add(number);

            var curve = new CurveField { tooltip = "Multiplies the value by the curve at the unit's level. Leave it empty for a constant value." };
            curve.AddToClassList(VALUE_USS_CLASS + "-curve");
            curve.BindProperty(_ScalableFloat.FindPropertyRelative("curve"));
            input.Add(curve);

            return input;
        }

        // The listed attributes, in order.
        private static string Signature(SerializedProperty _Array)
        {
            if (_Array == null) return null;

            var parts = new string[_Array.arraySize];
            for (int i = 0; i < parts.Length; i++)
                parts[i] = RegistryProperties.Read(_Array.GetArrayElementAtIndex(i).FindPropertyRelative(ATTRIBUTE_FIELD), AttributeKind.Instance).ToString();

            return string.Join("|", parts);
        }
        #endregion



        #region Editing
        // The attributes listed by any of the objects, so an added attribute is new to each of them.
        private static HashSet<GameplayAttribute> Listed(UnityEngine.Object[] _Targets, string _ArrayPath)
        {
            var listed = new HashSet<GameplayAttribute>();
            foreach (var target in _Targets)
            {
                if (target == null) continue;

                using var single = new SerializedObject(target);
                var array = single.FindProperty(_ArrayPath);
                for (int i = 0; i < array.arraySize; i++)
                {
                    var reference = RegistryProperties.Read(array.GetArrayElementAtIndex(i).FindPropertyRelative(ATTRIBUTE_FIELD), AttributeKind.Instance);
                    listed.Add(GameplayAttribute.FromId(reference.Registry, reference.Id, reference.Name));
                }
            }

            return listed;
        }

        // Appends the attribute to each selected object that doesn't list it yet, one object at a time: a structural
        // change applied through a multi-object SerializedObject writes the first object's whole array to the others.
        private static void Add(SerializedObject _SerializedObject, string _ArrayPath, string _ValueField, GameplayAttribute _Attribute, string _UndoName)
        {
            if (_Attribute.IsNone || !IsAlive(_SerializedObject)) return;

            bool changed = false;
            foreach (var target in _SerializedObject.targetObjects)
            {
                if (target == null || Listed(new[] { target }, _ArrayPath).Contains(_Attribute)) continue;

                using var single = new SerializedObject(target);
                var array = single.FindProperty(_ArrayPath);
                int index = array.arraySize;
                array.InsertArrayElementAtIndex(index);

                var entry = array.GetArrayElementAtIndex(index);
                RegistryProperties.Write(entry.FindPropertyRelative(ATTRIBUTE_FIELD), _Attribute.RegistryId, _Attribute.Id, _Attribute.Name);
                if (_ValueField != null)
                    ResetValue(entry.FindPropertyRelative(_ValueField), _Attribute);

                changed |= single.ApplyModifiedProperties();
            }

            _SerializedObject.Update();
            if (changed)
                Undo.SetCurrentGroupName(_UndoName);
        }

        // A new value starts at the attribute's registry default, without the copied entry's curve.
        private static void ResetValue(SerializedProperty _Value, GameplayAttribute _Attribute)
        {
            float defaultValue = 0f;
            var registry = AttributeKind.Instance.Snapshot.Find(_Attribute.RegistryId);
            if (registry != null && registry.TryGetRow(_Attribute.Id, out var row))
                defaultValue = row.defaultValue;

            var number = _Value.FindPropertyRelative("value");
            if (number != null) number.floatValue = defaultValue;

            var curve = _Value.FindPropertyRelative("curve");
            if (curve != null) curve.animationCurveValue = new AnimationCurve();
        }

        private static bool IsAlive(SerializedObject _SerializedObject)
        {
            try
            {
                return _SerializedObject.targetObject != null;
            }
            catch (Exception _Exception) when (_Exception is ArgumentNullException or NullReferenceException)
            {
                return false; // disposed with its inspector
            }
        }
        #endregion
    }
}
