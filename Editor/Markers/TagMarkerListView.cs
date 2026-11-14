using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Registry;
using LMK.Editor.UIElements;
using LMK.Editor.Utilities;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Every marker, in the order rows show them: rename, recolour, delete, add, and drag the handle to reorder.
    /// Changes are saved at once and can be undone (<see cref="TagMarkers"/>). Shown by
    /// <see cref="TagMarkerManagerWindow"/>.
    /// </summary>
    internal sealed class TagMarkerListView : VisualElement
    {
        public const string USS_CLASS = "tag-marker-list";
        public const string USS_CLASS_ROW = USS_CLASS + "__row";
        public const string USS_CLASS_HANDLE = USS_CLASS + "__handle";

        private const string USS_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/TagMarkerListUSS.uss";

        /// <summary>
        /// Colours offered for new markers: light and desaturated, distinct from each other.
        /// </summary>
        internal static readonly Color[] PALETTE =
        {
            new(0.55f, 0.69f, 0.87f), // blue
            new(0.87f, 0.80f, 0.55f), // yellow
            new(0.60f, 0.80f, 0.62f), // green
            new(0.88f, 0.62f, 0.60f), // red
            new(0.75f, 0.65f, 0.87f), // purple
            new(0.90f, 0.72f, 0.52f), // orange
            new(0.55f, 0.80f, 0.80f), // teal
            new(0.80f, 0.80f, 0.80f), // grey
        };

        /// <summary>
        /// Asks before deleting a marker that tags use: title, message, whether to go on. A dialog by default.
        /// </summary>
        internal Func<string, string, bool> Confirm = (_Title, _Message) => EditorUtility.DisplayDialog(_Title, _Message, "Delete", "Cancel");

        private sealed class Row
        {
            public RegistryGuid id;
            public VisualElement root;
            public ColorField color;
            public TextField text;
            public Label uses;
        }

        private readonly VisualElement list;
        private readonly Label emptyLabel;
        private readonly Label errorLabel;
        private readonly List<Row> rows = new();

        internal Button AddButton { get; }

        /// <summary>
        /// The marker of each row, in order.
        /// </summary>
        internal IEnumerable<RegistryGuid> RowIds
        {
            get
            {
                foreach (var row in rows)
                    yield return row.id;
            }
        }

        /// <summary>
        /// The last error shown, or an empty string.
        /// </summary>
        internal string Error => errorLabel.text;



        public TagMarkerListView()
        {
            name = "marker-list";
            AddToClassList(USS_CLASS);

            var styleSheet = LMKAssetUtility.Load<StyleSheet>(USS_PATH);
            if (styleSheet != null)
                styleSheets.Add(styleSheet);

            var note = new Label("Your markers, in the order tag rows show them. Only you see them, and every change is saved at once.");
            note.AddToClassList(USS_CLASS + "__note");
            Add(note);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(USS_CLASS + "__scroll");
            Add(scroll);

            list = new VisualElement { name = "marker-rows" };
            list.AddToClassList(USS_CLASS + "__rows");
            scroll.Add(list);

            var reorder = new DragReorderManipulator(FlexDirection.Column) { CanStartDrag = IsOnHandle };
            reorder.Reordered += (from, to) =>
            {
                if (from >= 0 && from < rows.Count)
                    Report(Move(rows[from].id, to));
            };
            list.AddManipulator(reorder);

            emptyLabel = new Label("No markers yet.");
            emptyLabel.AddToClassList(USS_CLASS + "__empty");
            scroll.Add(emptyLabel);

            errorLabel = new Label { name = "marker-list-error" };
            errorLabel.AddToClassList(USS_CLASS + "__error");
            errorLabel.style.display = DisplayStyle.None;
            Add(errorLabel);

            AddButton = new Button(() =>
            {
                var result = Add();
                Report(result);
                if (result.Success)
                    FocusText(result.Id);
            }) { name = "add-marker", text = "Add Marker" };
            AddButton.AddToClassList(USS_CLASS + "__add");
            Add(AddButton);

            // Markers change from other views too, e.g. undo or the Inspector panel.
            RegisterCallback<AttachToPanelEvent>(_ => TagMarkers.Changed += Refresh);
            RegisterCallback<DetachFromPanelEvent>(_ => TagMarkers.Changed -= Refresh);

            Refresh();
        }



        #region Operations
        /// <summary>
        /// Add a marker at the end, named "Marker N", in the next colour of the palette.
        /// </summary>
        internal RegistryResult Add()
        {
            var definitions = TagMarkers.Definitions;

            string text;
            int number = definitions.Count + 1;
            do text = $"Marker {number++}";
            while (TagMarkers.CheckText(text, RegistryGuid.Zero) != null);

            return TagMarkers.AddDefinition(text, PALETTE[definitions.Count % PALETTE.Length]);
        }

        internal RegistryResult Rename(RegistryGuid _Marker, string _Text)
        {
            if (!TagMarkers.TryGetDefinition(_Marker, out var definition)) return RegistryResult.Fail("The marker no longer exists.");
            return TagMarkers.EditDefinition(_Marker, _Text, definition.color);
        }

        internal RegistryResult Recolor(RegistryGuid _Marker, Color _Color)
        {
            if (!TagMarkers.TryGetDefinition(_Marker, out var definition)) return RegistryResult.Fail("The marker no longer exists.");
            return TagMarkers.EditDefinition(_Marker, definition.text, _Color);
        }

        internal RegistryResult Move(RegistryGuid _Marker, int _Index) => TagMarkers.MoveDefinition(_Marker, _Index);

        /// <summary>
        /// Delete a marker. One that tags use is only deleted after <see cref="Confirm"/>.
        /// </summary>
        /// <returns> Whether it was deleted. </returns>
        internal bool Delete(RegistryGuid _Marker)
        {
            if (!TagMarkers.TryGetDefinition(_Marker, out var definition)) return false;

            int uses = TagMarkers.CountUses(_Marker);
            if (uses > 0 && !Confirm("Delete Marker", $"\"{definition.text}\" is on {Tags(uses)}. Delete it and remove it from them?"))
                return false;

            return Report(TagMarkers.DeleteDefinition(_Marker));
        }
        #endregion



        #region Display
        /// <summary>
        /// Show the current markers. Rows are updated in place while the same markers are in the same order, so a
        /// field being edited, or an open colour picker, keeps its row.
        /// </summary>
        public void Refresh()
        {
            var definitions = TagMarkers.Definitions;

            bool same = definitions.Count == rows.Count;
            for (int i = 0; same && i < rows.Count; i++)
                same = rows[i].id.Equals(definitions[i].id);

            if (!same)
            {
                list.Clear();
                rows.Clear();

                foreach (var definition in definitions)
                {
                    var row = CreateRow(definition.id);
                    rows.Add(row);
                    list.Add(row.root);
                }
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var definition = definitions[i];
                rows[i].color.SetValueWithoutNotify(definition.color);
                rows[i].text.SetValueWithoutNotify(definition.text);
                rows[i].uses.text = Tags(TagMarkers.CountUses(definition.id));
            }

            emptyLabel.style.display = rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private Row CreateRow(RegistryGuid _Marker)
        {
            var row = new Row { id = _Marker, root = new VisualElement() };
            row.root.AddToClassList(USS_CLASS_ROW);

            var handle = new Label("≡") { tooltip = "Drag to reorder." };
            handle.AddToClassList(USS_CLASS_HANDLE);
            row.root.Add(handle);

            row.color = new ColorField { showAlpha = false, showEyeDropper = true, name = "marker-color" };
            row.color.AddToClassList(USS_CLASS + "__color");
            row.color.RegisterValueChangedCallback(e => Report(Recolor(_Marker, e.newValue)));
            row.root.Add(row.color);

            // Committed on Enter or when it loses focus.
            row.text = new TextField { isDelayed = true, maxLength = TagMarkers.MAX_TEXT_LENGTH, name = "marker-text" };
            row.text.AddToClassList(USS_CLASS + "__text");
            row.text.RegisterValueChangedCallback(e =>
            {
                if (!Report(Rename(_Marker, e.newValue)))
                    Refresh();
            });
            row.root.Add(row.text);

            row.uses = new Label();
            row.uses.AddToClassList(USS_CLASS + "__uses");
            row.root.Add(row.uses);

            var delete = new Button(() => Delete(_Marker)) { text = "×", tooltip = "Delete the marker, and remove it from every tag.", name = "marker-delete" };
            delete.AddToClassList(USS_CLASS + "__delete");
            row.root.Add(delete);

            return row;
        }

        private void FocusText(RegistryGuid _Marker)
        {
            // Once the rebuilt row has a layout.
            schedule.Execute(() =>
            {
                foreach (var row in rows)
                {
                    if (!row.id.Equals(_Marker)) continue;

                    row.text.Focus();
                    row.text.SelectAll();
                    return;
                }
            });
        }

        /// <returns> Whether the operation succeeded. </returns>
        private bool Report(RegistryResult _Result)
        {
            errorLabel.text = _Result.Success ? string.Empty : _Result.Error;
            errorLabel.style.display = _Result.Success ? DisplayStyle.None : DisplayStyle.Flex;
            return _Result.Success;
        }

        private static bool IsOnHandle(VisualElement _Pressed)
        {
            for (var element = _Pressed; element != null; element = element.hierarchy.parent)
                if (element.ClassListContains(USS_CLASS_HANDLE))
                    return true;

            return false;
        }

        private static string Tags(int _Count) => _Count == 1 ? "1 tag" : $"{_Count} tags";
        #endregion
    }
}
