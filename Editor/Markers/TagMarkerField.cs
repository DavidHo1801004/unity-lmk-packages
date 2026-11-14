using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Utilities;
using LMK.Editor.Windows;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// A tag's markers in the Inspector panel, in a box like a tag container field: one chip per marker, with x to
    /// remove it (or "None"), and + at the bottom to add one: a marker the tag doesn't have yet, or a new marker. Works
    /// for tags of any registry, since markers are the user's own.
    /// </summary>
    internal sealed class TagMarkerField : VisualElement
    {
        public const string USS_CLASS = "tag-marker-field";

        public const string USS_CLASS_CHIP = USS_CLASS + "__chip";

        private const string USS_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/GameplayTagManagerWindowUSS.uss";

        private const float POPUP_WIDTH = 240f;


        private readonly RegistryGuid registry;
        private readonly RegistryGuid tag;

        private readonly VisualElement chips;

        internal Button AddButton { get; }



        public TagMarkerField(RegistryGuid _Registry, RegistryGuid _Tag)
        {
            registry = _Registry;
            tag = _Tag;

            name = "marker-field";
            AddToClassList(USS_CLASS);

            chips = new VisualElement { name = "marker-chips" };
            chips.AddToClassList(USS_CLASS + "__chips");
            Add(chips);

            AddButton = new Button(ShowAddMenu) { name = "add-marker-button", text = "+", tooltip = "Add a marker. Only you see markers." };
            AddButton.AddToClassList(USS_CLASS + "__add");
            Add(AddButton);

            // Markers change from other views too, e.g. undo.
            RegisterCallback<AttachToPanelEvent>(_ => TagMarkers.Changed += Refresh);
            RegisterCallback<DetachFromPanelEvent>(_ => TagMarkers.Changed -= Refresh);

            Refresh();
        }



        /// <summary>
        /// Show the tag's current markers.
        /// </summary>
        public void Refresh()
        {
            chips.Clear();

            var markers = TagMarkers.GetMarkers(registry, tag);
            foreach (var marker in markers)
                chips.Add(CreateChip(marker));

            // Like an empty tag container field.
            if (markers.Count == 0)
            {
                var empty = new Label("None") { name = "marker-empty" };
                empty.AddToClassList(USS_CLASS + "__empty");
                chips.Add(empty);
            }
        }

        /// <summary>
        /// A marker drawn like a tag in a tag container field: a remove button joined to the name, which has the
        /// marker's colour.
        /// </summary>
        private VisualElement CreateChip(TagMarkerDefinition _Marker)
        {
            var chip = new VisualElement { tooltip = _Marker.text };
            chip.AddToClassList(USS_CLASS_CHIP);

            var markerId = _Marker.id;
            var remove = new Button(() => TagMarkers.Unassign(registry, tag, markerId)) { name = "marker-remove", text = "x", tooltip = $"Remove \"{_Marker.text}\"." };
            remove.AddToClassList(USS_CLASS_CHIP + "-remove");
            chip.Add(remove);

            var label = new Label(_Marker.text);
            label.AddToClassList(USS_CLASS_CHIP + "-label");
            label.style.backgroundColor = _Marker.color;
            label.style.color = ContrastText(_Marker.color);
            chip.Add(label);

            return chip;
        }

        private void ShowAddMenu()
        {
            var menu = new GenericDropdownMenu();

            bool any = false;
            foreach (var marker in TagMarkers.Definitions)
            {
                if (TagMarkers.HasMarker(registry, tag, marker.id)) continue;

                var markerId = marker.id;
                menu.AddItem(marker.text, false, () => TagMarkers.Assign(registry, tag, markerId));
                any = true;
            }

            if (any)
                menu.AddSeparator(null);

            menu.AddItem("New Marker...", false, ShowNewMarker);
            menu.AddItem("Manage Markers...", false, TagMarkerManagerWindow.Open);

            menu.DropDown(AddButton.worldBound, AddButton, DropdownMenuSizeMode.Auto);
        }

        /// <summary>
        /// A popup to name a new marker and pick its colour from the palette. Enter or Add creates it and gives it to
        /// the tag. Manage Markers sets any other colour: a colour picker would close the popup, since it takes focus.
        /// </summary>
        internal void ShowNewMarker()
        {
            var content = new VisualElement { name = "new-marker-popup" };
            var styleSheet = LMKAssetUtility.Load<StyleSheet>(USS_PATH);
            if (styleSheet != null)
                content.styleSheets.Add(styleSheet);

            var text = new TextField("Name") { name = "new-marker-text", maxLength = TagMarkers.MAX_TEXT_LENGTH };
            content.Add(text);

            var palette = TagMarkerListView.PALETTE;
            var color = palette[TagMarkers.Definitions.Count % palette.Length];
            content.Add(CreateSwatches(palette, color, picked => color = picked));

            var error = new Label { name = "new-marker-error" };
            error.style.display = DisplayStyle.None;
            content.Add(error);

            AnchoredPopup popup = null;

            void Create()
            {
                var result = TagMarkers.AddDefinition(text.value, color);
                if (result.Success)
                    result = TagMarkers.Assign(registry, tag, result.Id);

                if (!result.Success)
                {
                    error.text = result.Error;
                    error.style.display = DisplayStyle.Flex;
                    return;
                }

                popup?.Close();
            }

            var add = new Button(Create) { name = "new-marker-add", text = "Add" };
            content.Add(add);

            text.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is not (KeyCode.Return or KeyCode.KeypadEnter)) return;

                e.StopImmediatePropagation();
                Create();
            }, TrickleDown.TrickleDown);

            text.RegisterValueChangedCallback(_ => error.style.display = DisplayStyle.None);

            popup = AnchoredPopup.Show(AddButton, content, Vector2.zero, POPUP_WIDTH);
        }

        /// <summary>
        /// A row of colour swatches, one picked at a time.
        /// </summary>
        private static VisualElement CreateSwatches(Color[] _Colors, Color _Picked, System.Action<Color> _OnPicked)
        {
            var row = new VisualElement { name = "new-marker-swatches" };
            row.AddToClassList(USS_CLASS + "__swatches");

            var swatches = new List<VisualElement>();
            foreach (var color in _Colors)
            {
                var swatch = new VisualElement { tooltip = "#" + ColorUtility.ToHtmlStringRGB(color) };
                swatch.AddToClassList(USS_CLASS + "__swatch");
                swatch.style.backgroundColor = color;
                swatch.EnableInClassList(USS_CLASS + "__swatch--picked", TagMarkerSet.SameColor(color, _Picked));

                var picked = color;
                swatch.AddManipulator(new Clickable(() =>
                {
                    foreach (var other in swatches)
                        other.EnableInClassList(USS_CLASS + "__swatch--picked", other == swatch);

                    _OnPicked(picked);
                }));

                swatches.Add(swatch);
                row.Add(swatch);
            }

            return row;
        }

        /// <summary>
        /// Dark or white text, whichever contrasts more with <paramref name="_Background"/>. Both contrast equally at a
        /// relative luminance of about 0.179.
        /// </summary>
        internal static Color ContrastText(Color _Background)
        {
            var linear = _Background.linear;
            float luminance = 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
            return luminance > 0.179f ? new Color(0.1f, 0.1f, 0.1f) : Color.white;
        }
    }
}
