using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Reorderable list bound to an array or list <see cref="SerializedProperty"/>, in a collapsible panel whose header
    /// shows the title, the element count and an add button. <br/>
    /// Rows have a drag handle, the element and a remove button.
    /// </summary>
    /// <remarks>
    /// Bind it in code, with the constructor or <see cref="BindProperty"/>: Unity's binding API gives custom elements no
    /// way to receive a property from <c>Bind()</c>, so it has no binding path. A <c>Bind()</c> on an ancestor is fine. <br/>
    /// Every change goes through the <see cref="SerializedObject"/>, so Undo, multi-object editing and prefab overrides
    /// work. With several objects selected whose lists have different sizes, the list is read-only, as in Unity. <br/>
    /// Rows are rebuilt when elements are added, removed or moved, by this list, Undo or scripts. Value edits only
    /// update the bound fields. Rows aren't virtualized: meant for inspector-sized lists.
    /// </remarks>
    [UxmlElement("PropertyList", libraryPath = "LMK/Fields", visibility = LibraryVisibility.Visible)]
    public partial class PropertyList : VisualElement
    {
        private bool hasCustomTitle;
        /// <summary>
        /// Text in the header. Defaults to the property's display name.
        /// </summary>
        [UxmlAttribute(name = "title")]
        public string Title
        {
            get => panel.Title;
            set
            {
                hasCustomTitle = !string.IsNullOrEmpty(value);
                panel.Title = hasCustomTitle ? value : GetArray()?.displayName ?? string.Empty;
            }
        }

        /// <summary>
        /// Text shown while the list is empty, e.g. <c>"No attributes"</c>.
        /// </summary>
        [UxmlAttribute(name = "empty-text")]
        public string EmptyText
        {
            get => emptyLabel.text;
            set => emptyLabel.text = string.IsNullOrEmpty(value) ? DEFAULT_EMPTY_TEXT : value;
        }

        /// <summary>
        /// Key of the saved expanded state, see <see cref="CollapsiblePanel.PersistenceKey"/>. Empty means it isn't saved.
        /// </summary>
        [UxmlAttribute(name = "persistence-key")]
        public string PersistenceKey
        {
            get => panel.PersistenceKey;
            set => panel.PersistenceKey = value;
        }

        /// <summary>
        /// Whether only the header is shown.
        /// </summary>
        public bool Collapsed
        {
            get => panel.Collapsed;
            set => panel.Collapsed = value;
        }

        /// <summary>
        /// Called by the add button instead of adding an element, with the button's world bounds, e.g. to open a picker
        /// such as <see cref="SearchableDropdown"/> under it. The owner then adds the element itself. <br/>
        /// Null adds an element the way Unity does.
        /// </summary>
        public Action<Rect> AddRequested { get; set; }

        /// <summary>
        /// Creates the content of a row. Null uses a <see cref="PropertyField"/>.
        /// </summary>
        public Func<VisualElement> makeItem { get; set; }

        /// <summary>
        /// Binds the content of a row to its element. Null binds content that is <see cref="IBindable"/>,
        /// such as the default <see cref="PropertyField"/>. <br/>
        /// Set it before binding the list, or call <see cref="Refresh"/>.
        /// </summary>
        public Action<VisualElement, SerializedProperty> bindItem { get; set; }

        /// <summary>
        /// Called after the rows are rebuilt, e.g. to update <see cref="SetAddEnabled"/> when elements are added or removed.
        /// </summary>
        public event Action ItemsChanged;

        /// <summary>
        /// The bound array, or null if unbound or its object is gone. A fresh property on each call.
        /// </summary>
        public SerializedProperty Property => GetArray();

        /// <summary>
        /// The bound serialized object, or null.
        /// </summary>
        public SerializedObject SerializedObject => serializedObject;

        /// <summary>
        /// Whether the add button is enabled by <see cref="SetAddEnabled"/>. It is also disabled while the list is read-only.
        /// </summary>
        public bool AddEnabled => addEnabled;

        /// <summary>
        /// The add button, e.g. to anchor a picker.
        /// </summary>
        public VisualElement AddButton => addButton;

        /// <summary>
        /// The panel holding the header and rows.
        /// </summary>
        internal CollapsiblePanel Panel => panel;

        internal VisualElement Rows => rows;

        internal Label CountLabel => countLabel;

        internal Label EmptyLabel => emptyLabel;

        internal Label MessageLabel => messageLabel;

        internal VisualElement AddWrapper => addWrapper;

        internal const string USS_CLASS = "lmk-property-list";

        internal const string USS_CLASS_COUNT = USS_CLASS + "__count";

        internal const string USS_CLASS_ADD_WRAPPER = USS_CLASS + "__add-wrapper";

        internal const string USS_CLASS_ADD_BUTTON = USS_CLASS + "__add-button";

        internal const string USS_CLASS_ROWS = USS_CLASS + "__rows";

        internal const string USS_CLASS_ROW = USS_CLASS + "__row";

        internal const string USS_CLASS_ROW_ALTERNATE = USS_CLASS_ROW + "--alternate";

        internal const string USS_CLASS_HANDLE = USS_CLASS + "__handle";

        internal const string USS_CLASS_HANDLE_BAR = USS_CLASS + "__handle-bar";

        internal const string USS_CLASS_ROW_CONTENT = USS_CLASS + "__row-content";

        internal const string USS_CLASS_ROW_CONTENT_DEFAULT = USS_CLASS_ROW_CONTENT + "--property-field";

        internal const string USS_CLASS_REMOVE_BUTTON = USS_CLASS + "__remove-button";

        internal const string USS_CLASS_EMPTY = USS_CLASS + "__empty";

        internal const string USS_CLASS_MESSAGE = USS_CLASS + "__message";

        internal const string MIXED_SIZES_MESSAGE = "Lists with different sizes can't be edited with several objects selected.";

        internal const string MIXED_COUNT = "—";

        private const string DEFAULT_EMPTY_TEXT = "List is empty";

        private const string ADD_TOOLTIP = "Add an element";

        private const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/PropertyListUSS.uss";

        private readonly CollapsiblePanel panel;

        private readonly Label countLabel;

        private readonly VisualElement addWrapper;

        private readonly ToolbarButton addButton;

        private readonly Label messageLabel;

        private readonly Label emptyLabel;

        private readonly VisualElement rows;

        private SerializedObject serializedObject;

        private string arrayPath;

        // Tracks the bound array. Replaced on each bind, so trackers of earlier properties don't linger.
        private VisualElement tracker;

        private bool addEnabled = true;

        private string addDisabledTooltip;

        private bool editable;

        // Size and managed references of the rows shown. A change rebuilds the rows.
        private string shownStructure;

        // Content hash of each element shown. The same hashes in another order is a move, which rebuilds the rows;
        // other changes are value edits, which bound fields show themselves.
        private uint[] shownHashes = Array.Empty<uint>();



        public PropertyList()
            : this(null) { }

        /// <param name="_Property"> Array or list to show, or null to bind later. </param>
        /// <param name="_Title"> Text in the header, or null for the property's display name. </param>
        public PropertyList(SerializedProperty _Property, string _Title = null)
        {
            LMKTheme.ApplyIfStandalone(this);
            LMKTheme.AddStyleSheet(this, USS_PATH);

            AddToClassList(USS_CLASS);

            panel = new CollapsiblePanel(string.Empty);
            hierarchy.Add(panel);

            countLabel = new Label { pickingMode = PickingMode.Ignore };
            countLabel.AddToClassList(USS_CLASS_COUNT);
            panel.HeaderActions.Add(countLabel);

            // A disabled button gets no tooltip, so the wrapper holds it while the button ignores the pointer.
            addWrapper = new VisualElement();
            addWrapper.AddToClassList(USS_CLASS_ADD_WRAPPER);
            panel.HeaderActions.Add(addWrapper);

            addButton = new ToolbarButton(false, OnAddClicked) { text = "+", tooltip = ADD_TOOLTIP };
            addButton.AddToClassList(USS_CLASS_ADD_BUTTON);
            addWrapper.Add(addButton);

            messageLabel = new Label(MIXED_SIZES_MESSAGE);
            messageLabel.AddToClassList(USS_CLASS_MESSAGE);
            panel.Add(messageLabel);

            emptyLabel = new Label(DEFAULT_EMPTY_TEXT);
            emptyLabel.AddToClassList(USS_CLASS_EMPTY);
            panel.Add(emptyLabel);

            rows = new VisualElement();
            rows.AddToClassList(USS_CLASS_ROWS);
            panel.Add(rows);

            var reorder = new DragReorderManipulator(FlexDirection.Column)
            {
                CanStartDrag = _Pressed => editable && enabledInHierarchy && IsInHandle(_Pressed),
            };
            reorder.Reordered += Move;
            rows.AddManipulator(reorder);

            // Undo doesn't always reach property trackers, e.g. on objects that aren't assets.
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed += RefreshIfChanged;
                RefreshIfChanged();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= RefreshIfChanged);

            if (_Property != null)
                BindProperty(_Property);
            else
                Rebuild();

            if (_Title != null)
                Title = _Title;
        }



        #region Binding
        /// <summary>
        /// Show and edit <paramref name="_Property"/>, an array or list.
        /// </summary>
        public void BindProperty(SerializedProperty _Property)
        {
            serializedObject = _Property?.serializedObject;
            arrayPath = _Property?.propertyPath;

            tracker?.RemoveFromHierarchy();
            tracker = null;

            if (_Property != null)
            {
                tracker = new VisualElement { name = "lmk-property-list-tracker" };
                tracker.style.display = DisplayStyle.None;
                hierarchy.Add(tracker);
                tracker.TrackPropertyValue(_Property, _ => RefreshIfChanged());
            }

            if (!hasCustomTitle)
                panel.Title = _Property?.displayName ?? string.Empty;

            Rebuild();
        }

        private SerializedProperty GetArray()
        {
            if (serializedObject == null || string.IsNullOrEmpty(arrayPath)) return null;

            try
            {
                if (serializedObject.targetObject == null) return null;
            }
            catch (Exception _Exception) when (_Exception is ArgumentNullException or NullReferenceException)
            {
                return null; // the serialized object was disposed, e.g. by its inspector closing
            }

            var array = serializedObject.FindProperty(arrayPath);
            return array is { isArray: true } && array.propertyType != SerializedPropertyType.String ? array : null;
        }
        #endregion



        #region Rows
        /// <summary>
        /// Rebuild the rows, e.g. after <see cref="makeItem"/> or <see cref="bindItem"/> change, or when row content
        /// depends on something else that changed.
        /// </summary>
        public void Refresh()
        {
            Rebuild();
        }

        // Rebuild for a structural change: elements added, removed or moved. Value edits keep the rows.
        private void RefreshIfChanged()
        {
            if (GetArray() == null)
            {
                if (shownStructure != null) Rebuild();
                return;
            }

            serializedObject.UpdateIfRequiredOrScript();
            var array = GetArray();

            if (GetStructure(array) != shownStructure)
            {
                Rebuild();
                return;
            }

            var hashes = GetHashes(array);
            if (hashes.SequenceEqual(shownHashes)) return;

            if (IsReordering(shownHashes, hashes))
                Rebuild();
            else
                shownHashes = hashes;
        }

        private void Rebuild()
        {
            rows.Clear();

            serializedObject?.UpdateIfRequiredOrScript();
            var array = GetArray();

            bool mixedSizes = array != null && serializedObject.isEditingMultipleObjects
                && array.FindPropertyRelative("Array.size").hasMultipleDifferentValues;

            editable = array != null && array.editable && !mixedSizes;
            shownStructure = array != null ? GetStructure(array) : null;
            shownHashes = array != null && !mixedSizes ? GetHashes(array) : Array.Empty<uint>();

            countLabel.text = array == null ? string.Empty : mixedSizes ? MIXED_COUNT : array.arraySize.ToString();
            SetShown(messageLabel, mixedSizes);
            SetShown(emptyLabel, array != null && !mixedSizes && array.arraySize == 0);
            UpdateAddButton();

            if (array != null && !mixedSizes)
            {
                for (int i = 0; i < array.arraySize; i++)
                    rows.Add(MakeRow(i, array.GetArrayElementAtIndex(i)));
            }

            ItemsChanged?.Invoke();
        }

        private VisualElement MakeRow(int _Index, SerializedProperty _Element)
        {
            var row = new VisualElement();
            row.AddToClassList(USS_CLASS_ROW);
            row.EnableInClassList(USS_CLASS_ROW_ALTERNATE, _Index % 2 == 1);

            var handle = new VisualElement { tooltip = "Drag to reorder" };
            handle.AddToClassList(USS_CLASS_HANDLE);
            for (int i = 0; i < 3; i++)
            {
                var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                bar.AddToClassList(USS_CLASS_HANDLE_BAR);
                handle.Add(bar);
            }
            SetShown(handle, editable);
            row.Add(handle);

            var content = makeItem != null ? makeItem() : new PropertyField();
            content.AddToClassList(USS_CLASS_ROW_CONTENT);
            content.EnableInClassList(USS_CLASS_ROW_CONTENT_DEFAULT, makeItem == null);
            row.Add(content);

            if (bindItem != null)
                bindItem(content, _Element);
            else if (content is IBindable bindable)
                bindable.BindProperty(_Element);

            var removeButton = new ToolbarButton(false, () => RemoveAt(_Index)) { text = "-", tooltip = "Remove this element" };
            removeButton.AddToClassList(USS_CLASS_REMOVE_BUTTON);
            SetShown(removeButton, editable);
            row.Add(removeButton);

            return row;
        }

        private static uint[] GetHashes(SerializedProperty _Array)
        {
            var hashes = new uint[_Array.arraySize];
            for (int i = 0; i < hashes.Length; i++)
                hashes[i] = _Array.GetArrayElementAtIndex(i).contentHash;
            return hashes;
        }

        // The same elements in another order. Equal elements swapped are indistinguishable, and need no rebuild.
        internal static bool IsReordering(IReadOnlyList<uint> _Before, IReadOnlyList<uint> _After)
        {
            if (_Before.Count != _After.Count || _Before.SequenceEqual(_After)) return false;

            return _Before.OrderBy(_Hash => _Hash).SequenceEqual(_After.OrderBy(_Hash => _Hash));
        }

        private static string GetStructure(SerializedProperty _Array)
        {
            var signature = new StringBuilder();
            signature.Append(_Array.FindPropertyRelative("Array.size").hasMultipleDifferentValues ? "*" : string.Empty);
            signature.Append(_Array.arraySize);

            // A managed reference moved, replaced or retyped changes its row's drawer, which isn't rebound by path.
            if (_Array.arrayElementType.StartsWith("managedReference", StringComparison.Ordinal))
            {
                for (int i = 0; i < _Array.arraySize; i++)
                {
                    var element = _Array.GetArrayElementAtIndex(i);
                    signature.Append('|').Append(element.managedReferenceId).Append(':').Append(element.managedReferenceFullTypename);
                }
            }

            return signature.ToString();
        }

        private static bool IsInHandle(VisualElement _Element)
        {
            for (var element = _Element; element != null; element = element.hierarchy.parent)
            {
                if (element.ClassListContains(USS_CLASS_HANDLE)) return true;
                if (element.ClassListContains(USS_CLASS_ROW)) return false;
            }
            return false;
        }

        private static void SetShown(VisualElement _Element, bool _Shown)
            => _Element.style.display = _Shown ? DisplayStyle.Flex : DisplayStyle.None;
        #endregion



        #region Editing
        /// <summary>
        /// Enable or disable the add button. While disabled, <paramref name="_DisabledTooltip"/> says why,
        /// e.g. <c>"Every attribute is already added"</c>.
        /// </summary>
        public void SetAddEnabled(bool _Enabled, string _DisabledTooltip = null)
        {
            addEnabled = _Enabled;
            addDisabledTooltip = _DisabledTooltip;
            UpdateAddButton();
        }

        private void UpdateAddButton()
        {
            bool enabled = addEnabled && editable;
            addButton.SetEnabled(enabled);
            addButton.pickingMode = enabled ? PickingMode.Position : PickingMode.Ignore;
            addWrapper.tooltip = enabled ? null : !addEnabled ? addDisabledTooltip : null;
        }

        private void OnAddClicked()
        {
            if (!editable || !addEnabled) return;

            if (AddRequested != null)
            {
                AddRequested(addButton.worldBound);
                return;
            }

            Edit("Add Element", _Array => _Array.arraySize++); // as Unity's lists: no shared managed reference
        }

        private void RemoveAt(int _Index)
        {
            Edit("Remove Element", _Array =>
            {
                if (_Index < _Array.arraySize)
                    _Array.DeleteArrayElementAtIndex(_Index);
            });
        }

        private void Move(int _From, int _To)
        {
            Edit("Reorder Elements", _Array =>
            {
                if (_From < _Array.arraySize && _To < _Array.arraySize)
                    _Array.MoveArrayElement(_From, _To);
            });
        }

        private void Edit(string _UndoName, Action<SerializedProperty> _Change)
        {
            if (!editable || GetArray() == null) return;

            bool changed = false;
            if (serializedObject.isEditingMultipleObjects)
            {
                // One object at a time: a structural change applied through a multi-object SerializedObject writes the
                // first object's whole array to the others.
                foreach (var target in serializedObject.targetObjects)
                {
                    if (target == null) continue;

                    using var single = new SerializedObject(target);
                    var array = single.FindProperty(arrayPath);
                    if (array == null) continue;

                    _Change(array);
                    changed |= single.ApplyModifiedProperties();
                }

                serializedObject.Update();
            }
            else
            {
                serializedObject.Update();
                _Change(GetArray());
                changed = serializedObject.ApplyModifiedProperties();
            }

            if (changed)
                Undo.SetCurrentGroupName($"{_UndoName} ({panel.Title})");

            Rebuild();
        }
        #endregion
    }
}
