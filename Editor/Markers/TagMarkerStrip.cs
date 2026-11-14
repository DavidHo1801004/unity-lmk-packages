using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Utilities;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// A tag's markers at the end of its row, in the room left after the tag's name, which they never cover:
    /// <list type="bullet">
    /// <item> as chips with their names, when they all fit; </item>
    /// <item> otherwise as thin strips. Hovering a strip expands it from its centre to show its name, over its
    /// neighbours, which stay where they are. A ring in the row's colour keeps it apart from them; </item>
    /// <item> when even the strips don't fit, they scroll sideways with Shift + wheel or a horizontal swipe. A
    /// fade marks each side with more markers. A plain wheel still scrolls the tree. </item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Laid out over the row (absolutely positioned), so changing mode never changes the row's own layout. The
    /// room is measured again whenever the row's geometry changes.
    /// </remarks>
    internal sealed class TagMarkerStrip : VisualElement
    {
        public const string USS_CLASS = "tag-marker-strip";
        public const string USS_CLASS_ITEM = USS_CLASS + "__item";
        public const string USS_CLASS_ITEM_STRIP = USS_CLASS_ITEM + "--strip";
        public const string USS_CLASS_RING = USS_CLASS + "__ring";

        private const string USS_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/TagMarkerStripUSS.uss";

        // Padding inside a chip, on each side of its name.
        private const float CHIP_PADDING = 6f;

        // Room kept between the tag's name and the markers, and between the markers and the row's end. The name
        // turns bold on hover, so the gap is generous.
        private const float NAME_GAP = 12f;
        private const float END_INSET = 4f;

        // The ring around an expanded marker, and its border inside the ring.
        private const float RING = 2f;
        private const float BORDER = 1f;

        // Pixels scrolled per wheel unit.
        private const float SCROLL_SPEED = 8f;

        // Fallback width per character, while text can't be measured (no panel yet).
        private const float CHAR_WIDTH = 6f;

        private static StyleSheet styleSheet;
        private static Texture2D fadeLeftTexture;
        private static Texture2D fadeRightTexture;

        private readonly VisualElement row;
        private readonly Label nameLabel;

        private readonly VisualElement viewport;
        private readonly VisualElement content;
        private readonly VisualElement fadeLeft;
        private readonly VisualElement fadeRight;

        private readonly VisualElement ring;
        private readonly VisualElement expanded;
        private readonly Label expandedLabel;

        private readonly List<TagMarkerDefinition> markers = new();
        private readonly List<VisualElement> items = new();
        private readonly List<float> chipWidths = new();

        private float contentWidth;
        private float offset;
        private int expandedIndex = -1;

        /// <summary>
        /// How the markers are shown, as of the last layout.
        /// </summary>
        internal TagMarkerLayoutMode Mode { get; private set; } = TagMarkerLayoutMode.Hidden;

        /// <summary>
        /// The marker elements, in definition order.
        /// </summary>
        internal IReadOnlyList<VisualElement> Items => items;

        /// <summary>
        /// The expanded marker, or -1.
        /// </summary>
        internal int ExpandedIndex => expandedIndex;



        /// <param name="_Row"> The row the strip sits in, at its end. </param>
        /// <param name="_NameLabel"> The label with the tag's name, which the strip never covers. </param>
        public TagMarkerStrip(VisualElement _Row, Label _NameLabel)
        {
            row = _Row;
            nameLabel = _NameLabel;

            name = "marker-strip";
            AddToClassList(USS_CLASS);
            pickingMode = PickingMode.Ignore;

            styleSheet ??= LMKAssetUtility.Load<StyleSheet>(USS_PATH);
            if (styleSheet != null)
                styleSheets.Add(styleSheet);

            viewport = new VisualElement { name = "viewport", pickingMode = PickingMode.Ignore };
            viewport.AddToClassList(USS_CLASS + "__viewport");
            Add(viewport);

            content = new VisualElement { name = "content", pickingMode = PickingMode.Ignore };
            content.AddToClassList(USS_CLASS + "__content");
            viewport.Add(content);

            fadeLeft = CreateFade("fade-left", FadeTexture(true));
            fadeLeft.style.left = 0f;
            fadeRight = CreateFade("fade-right", FadeTexture(false));
            fadeRight.style.right = 0f;

            ring = new VisualElement { name = "expanded-ring", pickingMode = PickingMode.Ignore };
            ring.AddToClassList(USS_CLASS_RING);
            ring.style.display = DisplayStyle.None;
            Add(ring);

            expanded = new VisualElement { name = "expanded", pickingMode = PickingMode.Ignore };
            expanded.AddToClassList(USS_CLASS + "__expanded");
            ring.Add(expanded);

            expandedLabel = new Label { pickingMode = PickingMode.Ignore };
            expandedLabel.AddToClassList(USS_CLASS + "__label");
            expanded.Add(expandedLabel);

            RegisterCallback<WheelEvent>(OnWheel);
            row.RegisterCallback<GeometryChangedEvent>(OnRowGeometryChanged);
        }

        /// <summary>
        /// Stop following the row, before the strip is removed from it.
        /// </summary>
        public void Release()
        {
            row.UnregisterCallback<GeometryChangedEvent>(OnRowGeometryChanged);
            RemoveFromHierarchy();
        }



        #region Markers
        /// <summary>
        /// Show these markers, in this order.
        /// </summary>
        public void SetMarkers(IReadOnlyList<TagMarkerDefinition> _Markers)
        {
            Collapse();

            markers.Clear();
            content.Clear();
            items.Clear();

            if (_Markers != null)
                markers.AddRange(_Markers);

            for (int i = 0; i < markers.Count; i++)
                items.Add(CreateItem(markers[i], i));

            Relayout();
        }

        private VisualElement CreateItem(TagMarkerDefinition _Marker, int _Index)
        {
            var item = new VisualElement { tooltip = _Marker.text };
            item.AddToClassList(USS_CLASS_ITEM);
            item.style.backgroundColor = _Marker.color;
            item.style.marginLeft = _Index == 0 ? 0f : TagMarkerLayout.GAP;

            var label = new Label(_Marker.text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(USS_CLASS + "__label");
            label.style.color = TagMarkerField.ContrastText(_Marker.color);
            item.Add(label);

            item.RegisterCallback<PointerEnterEvent>(_ => Expand(_Index));
            item.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (expandedIndex == _Index)
                    Collapse();
            });

            content.Add(item);
            return item;
        }
        #endregion



        #region Layout
        private void OnRowGeometryChanged(GeometryChangedEvent _Event) => Relayout();

        /// <summary>
        /// Measure the room after the tag's name and lay the markers out in it.
        /// </summary>
        private void Relayout()
        {
            if (markers.Count == 0)
            {
                ApplyLayout(0f);
                return;
            }

            var rowBound = row.worldBound;
            var nameBound = nameLabel?.worldBound ?? Rect.zero;
            if (panel == null || float.IsNaN(rowBound.width) || rowBound.width <= 0f) return;

            float nameEnd = nameLabel == null
                ? rowBound.xMin
                : nameBound.xMin + nameLabel.resolvedStyle.paddingLeft + MeasureText(nameLabel, nameLabel.text);

            ApplyLayout(rowBound.xMax - END_INSET - nameEnd - NAME_GAP);
        }

        /// <summary>
        /// Lay the markers out in <paramref name="_Available"/> pixels.
        /// </summary>
        internal void ApplyLayout(float _Available)
        {
            chipWidths.Clear();
            for (int i = 0; i < markers.Count; i++)
                chipWidths.Add(MeasureText(items[i].Q<Label>(), markers[i].text) + 2f * CHIP_PADDING);

            Mode = TagMarkerLayout.Fit(chipWidths, _Available, out float width, out contentWidth);

            style.display = Mode == TagMarkerLayoutMode.Hidden ? DisplayStyle.None : DisplayStyle.Flex;
            style.width = width;
            content.style.width = contentWidth;

            bool labelled = Mode == TagMarkerLayoutMode.Labelled;
            for (int i = 0; i < items.Count; i++)
            {
                items[i].EnableInClassList(USS_CLASS_ITEM_STRIP, !labelled);
                items[i].style.width = labelled ? chipWidths[i] : TagMarkerLayout.STRIP_WIDTH;
            }

            if (labelled) Collapse();
            SetOffset(Mode == TagMarkerLayoutMode.Scroll ? offset : 0f, width);
        }

        private void SetOffset(float _Offset, float _ViewWidth)
        {
            offset = TagMarkerLayout.ClampOffset(_Offset, contentWidth, _ViewWidth);
            content.style.translate = new Translate(-offset, 0f);

            bool scrolls = Mode == TagMarkerLayoutMode.Scroll;
            var tint = RowBackground();
            fadeLeft.style.display = scrolls && offset > 0.5f ? DisplayStyle.Flex : DisplayStyle.None;
            fadeRight.style.display = scrolls && offset < contentWidth - _ViewWidth - 0.5f ? DisplayStyle.Flex : DisplayStyle.None;
            fadeLeft.style.unityBackgroundImageTintColor = tint;
            fadeRight.style.unityBackgroundImageTintColor = tint;
        }

        private void OnWheel(WheelEvent _Event)
        {
            if (Mode != TagMarkerLayoutMode.Scroll) return;

            // A plain wheel scrolls the tree. Shift + wheel, or a horizontal swipe, scrolls the markers.
            float delta = _Event.delta.x != 0f ? _Event.delta.x : _Event.shiftKey ? _Event.delta.y : 0f;
            if (delta == 0f) return;

            Collapse();
            SetOffset(offset + delta * SCROLL_SPEED, resolvedStyle.width);
            _Event.StopPropagation();
        }
        #endregion



        #region Expanded Marker
        /// <summary>
        /// Show a strip's marker with its name, centred on the strip, over its neighbours and kept within the row.
        /// It grows from the strip's width to its own.
        /// </summary>
        internal void Expand(int _Index)
        {
            if (Mode is TagMarkerLayoutMode.Labelled or TagMarkerLayoutMode.Hidden || _Index < 0 || _Index >= items.Count) return;

            expandedIndex = _Index;
            var marker = markers[_Index];
            var item = items[_Index];

            var background = RowBackground();
            ring.style.borderLeftColor = ring.style.borderRightColor = ring.style.borderTopColor = ring.style.borderBottomColor = background;
            ring.style.backgroundColor = background;

            var border = Color.Lerp(marker.color, Color.black, 0.35f);
            expanded.style.backgroundColor = marker.color;
            expanded.style.borderLeftColor = expanded.style.borderRightColor = expanded.style.borderTopColor = expanded.style.borderBottomColor = border;

            expandedLabel.text = marker.text;
            expandedLabel.style.color = TagMarkerField.ContrastText(marker.color);

            ring.style.display = DisplayStyle.Flex;

            // In the strip's coordinates. Without a layout yet, it is placed once there is one.
            var bound = worldBound;
            var itemBound = item.worldBound;
            var rowBound = row.worldBound;
            if (float.IsNaN(bound.width) || float.IsNaN(itemBound.width) || float.IsNaN(rowBound.width)) return;

            float extra = RING + BORDER;

            float center = itemBound.center.x - bound.xMin;
            float startWidth = itemBound.width + 2f * extra;
            float targetWidth = chipWidths[_Index] + 2f * extra;
            float targetLeft = TagMarkerLayout.ExpandedLeft(center, targetWidth, rowBound.xMin - bound.xMin, rowBound.xMax - bound.xMin);

            // From the strip, then to the full chip on the next frame, so the transition runs.
            ring.style.left = center - startWidth * 0.5f;
            ring.style.width = startWidth;

            ring.schedule.Execute(() =>
            {
                if (expandedIndex != _Index) return;

                ring.style.left = targetLeft;
                ring.style.width = targetWidth;
            });
        }

        internal void Collapse()
        {
            expandedIndex = -1;
            ring.style.display = DisplayStyle.None;
        }
        #endregion



        #region Helpers
        /// <summary>
        /// The colour behind the row: its own, or the first ancestor's that isn't transparent.
        /// </summary>
        private Color RowBackground()
        {
            for (var element = row; element != null; element = element.hierarchy.parent)
            {
                var color = element.resolvedStyle.backgroundColor;
                if (color.a > 0.01f) return new Color(color.r, color.g, color.b, 1f);
            }

            return new Color(0.22f, 0.22f, 0.22f, 1f);
        }

        private static float MeasureText(Label _Label, string _Text)
        {
            if (string.IsNullOrEmpty(_Text)) return 0f;

            float width = _Label != null && _Label.panel != null
                ? _Label.MeasureTextSize(_Text, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined).x
                : float.NaN;

            return float.IsNaN(width) || width <= 0f ? _Text.Length * CHAR_WIDTH : width;
        }

        private VisualElement CreateFade(string _Name, Texture2D _Texture)
        {
            var fade = new VisualElement { name = _Name, pickingMode = PickingMode.Ignore };
            fade.AddToClassList(USS_CLASS + "__fade");
            fade.style.backgroundImage = _Texture;
            fade.style.display = DisplayStyle.None;
            viewport.Add(fade);
            return fade;
        }

        /// <summary>
        /// White, opaque at the edge it sits on and transparent towards the middle. Tinted with the row's colour.
        /// </summary>
        private static Texture2D FadeTexture(bool _Left)
        {
            ref var texture = ref _Left ? ref fadeLeftTexture : ref fadeRightTexture;
            if (texture != null) return texture;

            const int WIDTH = 16;
            texture = new Texture2D(WIDTH, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            for (int x = 0; x < WIDTH; x++)
            {
                float t = x / (WIDTH - 1f);
                texture.SetPixel(x, 0, new Color(1f, 1f, 1f, _Left ? 1f - t : t));
            }

            texture.Apply();
            return texture;
        }
        #endregion
    }
}
