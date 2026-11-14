using System.Collections.Generic;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// How a tag row shows its markers in the space left after the tag's name.
    /// </summary>
    internal enum TagMarkerLayoutMode
    {
        /// <summary> No markers, or no room even for one strip. </summary>
        Hidden,

        /// <summary> Every marker fits as a chip with its name. </summary>
        Labelled,

        /// <summary> Every marker fits as a thin strip, without its name. </summary>
        Strips,

        /// <summary> Even the strips don't fit: they scroll sideways in the space there is. </summary>
        Scroll,
    }



    /// <summary>
    /// The width fitting of <see cref="TagMarkerStrip"/>, kept free of UI so it can be tested.
    /// </summary>
    internal static class TagMarkerLayout
    {
        /// <summary>
        /// Width of a marker shown as a strip.
        /// </summary>
        public const float STRIP_WIDTH = 6f;

        /// <summary>
        /// Space between two markers.
        /// </summary>
        public const float GAP = 2f;

        /// <summary>
        /// Pick how to show markers.
        /// </summary>
        /// <param name="_ChipWidths"> Width of each marker as a chip with its name. </param>
        /// <param name="_Available"> Room after the tag's name. </param>
        /// <param name="_Width"> Width of the strip: what the markers take, or <paramref name="_Available"/> when they scroll. </param>
        /// <param name="_ContentWidth"> Width of every marker side by side, in the chosen mode. </param>
        public static TagMarkerLayoutMode Fit(IReadOnlyList<float> _ChipWidths, float _Available, out float _Width, out float _ContentWidth)
        {
            _Width = _ContentWidth = 0f;

            int count = _ChipWidths?.Count ?? 0;
            if (count == 0 || float.IsNaN(_Available) || _Available < STRIP_WIDTH) return TagMarkerLayoutMode.Hidden;

            float gaps = GAP * (count - 1);

            float labelled = gaps;
            foreach (float width in _ChipWidths)
                labelled += width;

            if (labelled <= _Available)
            {
                _Width = _ContentWidth = labelled;
                return TagMarkerLayoutMode.Labelled;
            }

            float strips = STRIP_WIDTH * count + gaps;
            _ContentWidth = strips;

            if (strips <= _Available)
            {
                _Width = strips;
                return TagMarkerLayoutMode.Strips;
            }

            _Width = _Available;
            return TagMarkerLayoutMode.Scroll;
        }

        /// <summary>
        /// Where an expanded marker goes: centred on its strip, moved inward to stay within the row.
        /// </summary>
        /// <param name="_Center"> Centre of the strip. </param>
        /// <param name="_Width"> Width of the expanded marker. </param>
        /// <param name="_Min"> Left limit, e.g. the row's left edge. </param>
        /// <param name="_Max"> Right limit, e.g. the row's right edge. </param>
        /// <returns> The left edge of the expanded marker. </returns>
        public static float ExpandedLeft(float _Center, float _Width, float _Min, float _Max)
        {
            float left = _Center - _Width * 0.5f;
            if (left + _Width > _Max) left = _Max - _Width;
            if (left < _Min) left = _Min;
            return left;
        }

        /// <summary>
        /// A scroll offset kept within the content.
        /// </summary>
        public static float ClampOffset(float _Offset, float _ContentWidth, float _ViewWidth)
        {
            float max = _ContentWidth - _ViewWidth;
            if (max <= 0f) return 0f;
            return _Offset < 0f ? 0f : _Offset > max ? max : _Offset;
        }
    }
}
