using System;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.UIElements;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Building blocks of the ability system's asset inspectors: an LMK-themed root, sections that fold like foldouts,
    /// and lists. Sections and lists remember whether they are collapsed, per user and project.
    /// </summary>
    internal static class InspectorSection
    {
        public const string USS_PATH = "Packages/com.lmk.abilitysystem/Editor/Assets/InspectorsUSS.uss";

        public const string ROOT_USS_CLASS = "lmk-asset-inspector";
        public const string SECTION_USS_CLASS = ROOT_USS_CLASS + "__section";
        public const string NOTE_USS_CLASS = ROOT_USS_CLASS + "__note";
        public const string LIST_USS_CLASS = ROOT_USS_CLASS + "__list";

        private const string PERSISTENCE_PREFIX = "LMK.AbilitySystem.Inspector.";

        /// <summary>
        /// The root of an inspector: LMK theme and the inspectors' style sheet.
        /// </summary>
        public static VisualElement CreateRoot()
        {
            var root = new VisualElement();
            root.AddToClassList(ROOT_USS_CLASS);
            LMKTheme.Apply(root);
            LMKTheme.AddStyleSheet(root, USS_PATH);
            return root;
        }

        /// <summary>
        /// A titled section. Its collapsed state is kept by <paramref name="_Owner"/> type and title.
        /// </summary>
        public static CollapsiblePanel Create(VisualElement _Parent, Type _Owner, string _Title)
        {
            var section = new CollapsiblePanel(_Title)
            {
                CollapseAxis = TwoPaneSplitViewOrientation.Vertical,
                PersistenceKey = PersistenceKey(_Owner, _Title),
            };
            section.AddToClassList(SECTION_USS_CLASS);

            _Parent.Add(section);
            return section;
        }

        /// <summary>
        /// A <see cref="PropertyList"/> of the array or list <paramref name="_Path"/>, added to <paramref name="_Parent"/>.
        /// Its collapsed state is kept by <paramref name="_Owner"/> type and title. Added to an inspector's root, it is a
        /// section of its own; <see cref="Content"/> adds more below its rows.
        /// </summary>
        public static PropertyList List(VisualElement _Parent, SerializedObject _SerializedObject, string _Path, Type _Owner,
                                        string _Title, string _EmptyText = null)
        {
            var list = new PropertyList(_SerializedObject.FindProperty(_Path), _Title)
            {
                PersistenceKey = PersistenceKey(_Owner, _Title),
            };
            if (_EmptyText != null)
                list.EmptyText = _EmptyText;

            list.AddToClassList(LIST_USS_CLASS);
            list.EnableInClassList(SECTION_USS_CLASS, _Parent.ClassListContains(ROOT_USS_CLASS));

            _Parent.Add(list);
            return list;
        }

        /// <summary>
        /// The panel of <paramref name="_List"/>: what is added to it shows below the rows, and collapses with them.
        /// </summary>
        public static VisualElement Content(PropertyList _List) => _List.Q<CollapsiblePanel>();

        /// <summary>
        /// A <see cref="PropertyField"/> for the serialized field <paramref name="_Path"/>, added to <paramref name="_Parent"/>.
        /// The inspector binds it with the rest of the root.
        /// </summary>
        public static PropertyField Field(VisualElement _Parent, SerializedObject _SerializedObject, string _Path, string _Label = null)
        {
            var property = _SerializedObject.FindProperty(_Path);
            var field = _Label != null ? new PropertyField(property, _Label) : new PropertyField(property);
            _Parent.Add(field);
            return field;
        }

        /// <summary>
        /// Whether <paramref name="_Property"/> is drawn as a <see cref="List"/>: an array or list, but not a string.
        /// </summary>
        public static bool IsList(SerializedProperty _Property)
            => _Property is { isArray: true } && _Property.propertyType != SerializedPropertyType.String;

        /// <summary>
        /// A muted line of text, e.g. what a list is used for.
        /// </summary>
        public static Label Note(VisualElement _Parent, string _Text)
        {
            var note = new Label(_Text);
            note.AddToClassList(NOTE_USS_CLASS);
            _Parent.Add(note);
            return note;
        }

        /// <summary>
        /// Show or hide an element without removing it.
        /// </summary>
        public static void SetShown(VisualElement _Element, bool _Shown)
            => _Element.style.display = _Shown ? DisplayStyle.Flex : DisplayStyle.None;

        private static string PersistenceKey(Type _Owner, string _Title) => $"{PERSISTENCE_PREFIX}{_Owner.Name}.{_Title}";
    }
}
