using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

using TextLabel = UnityEngine.UIElements.Label;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// A label and a value area in one row. <br/>
    /// Inside a <see cref="FieldRowGroup"/> every row gets the same label width, so the values line up.
    /// </summary>
    /// <remarks>
    /// The value area is a row, so a value and a button sit on one line.
    /// Fields in it (<c>unity-base-field</c>) stretch to fill it.
    /// </remarks>
    [UxmlElement("FieldRow", libraryPath = "LMK/Layout", visibility = LibraryVisibility.Visible)]
    public partial class FieldRow : VisualElement
    {
        /// <summary>
        /// Text of the row's label.
        /// </summary>
        [UxmlAttribute(name = "label")]
        public string Label
        {
            get => labelElement.text;
            set
            {
                ((INotifyValueChanged<string>)labelElement).SetValueWithoutNotify(value);
                GetGroup()?.Align();
            }
        }

        private bool alignTop;
        /// <summary>
        /// If true, the label sits at the top of the row instead of the middle, e.g. next to a multi-line text field.
        /// </summary>
        [UxmlAttribute(name = "align-top")]
        public bool AlignTop
        {
            get => alignTop;
            set
            {
                alignTop = value;
                EnableInClassList(USS_CLASS_ALIGN_TOP, alignTop);
            }
        }

        /// <summary>
        /// The value area. <see cref="VisualElement.Add"/> and UXML children go here.
        /// </summary>
        public override VisualElement contentContainer => valueContainer;

        internal TextLabel LabelElement => labelElement;

        internal const string USS_CLASS = "lmk-field-row";

        internal const string USS_CLASS_LABEL = USS_CLASS + "__label";

        internal const string USS_CLASS_VALUE = USS_CLASS + "__value";

        internal const string USS_CLASS_ALIGN_TOP = USS_CLASS + "--align-top";

        internal const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/FieldRowUSS.uss";

        private readonly TextLabel labelElement;

        private readonly VisualElement valueContainer;

        // The group that last set the label width, so leaving it gives the row its own width back.
        private FieldRowGroup alignedBy;



        public FieldRow()
            : this(string.Empty) { }

        /// <param name="_Label"> Text of the row's label. </param>
        /// <param name="_Value"> Optional element to add to the value area. </param>
        public FieldRow(string _Label, VisualElement _Value = null)
        {
            LMKTheme.ApplyIfStandalone(this);
            LMKTheme.AddStyleSheet(this, USS_PATH);

            AddToClassList(USS_CLASS);

            labelElement = new();
            labelElement.AddToClassList(USS_CLASS_LABEL);
            hierarchy.Add(labelElement);

            valueContainer = new();
            valueContainer.AddToClassList(USS_CLASS_VALUE);
            hierarchy.Add(valueContainer);

            Label = _Label;

            if (_Value != null)
                Add(_Value);

            // Runs on the first layout too, so rows added later realign their group.
            labelElement.RegisterCallback<GeometryChangedEvent>(_ => GetGroup()?.Align());
            RegisterCallback<AttachToPanelEvent>(_ => OnAttachToPanel());
        }



        /// <summary>
        /// Width of the label text with its padding and border, without any width set by a group.
        /// </summary>
        internal float MeasureLabelWidth()
        {
            if (string.IsNullOrEmpty(labelElement.text)) return 0f;

            var style = labelElement.resolvedStyle;
            float textWidth = labelElement.MeasureTextSize(labelElement.text, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined).x;
            float width = textWidth + style.paddingLeft + style.paddingRight + style.borderLeftWidth + style.borderRightWidth;

            return float.IsNaN(width) ? 0f : (float)System.Math.Ceiling(width);
        }

        /// <summary>
        /// Set by <paramref name="_Group"/>: the label is at least <paramref name="_Width"/> wide,
        /// or exactly that wide when <paramref name="_IsFixed"/>.
        /// </summary>
        internal void ApplyLabelWidth(FieldRowGroup _Group, float _Width, bool _IsFixed)
        {
            alignedBy = _Group;

            StyleLength minWidth = _Width;
            StyleLength width = _IsFixed ? _Width : StyleKeyword.Null;

            if (labelElement.style.minWidth != minWidth)
                labelElement.style.minWidth = minWidth;
            if (labelElement.style.width != width)
                labelElement.style.width = width;
        }

        internal void SetSpacing(StyleLength _MarginTop)
        {
            if (style.marginTop != _MarginTop)
                style.marginTop = _MarginTop;
        }

        private void OnAttachToPanel()
        {
            var group = GetGroup();
            if (group != null || alignedBy == null) return;

            // Moved out of its group: back to its own label width.
            alignedBy = null;
            labelElement.style.minWidth = StyleKeyword.Null;
            labelElement.style.width = StyleKeyword.Null;
            style.marginTop = StyleKeyword.Null;
        }

        private FieldRowGroup GetGroup()
        {
            return GetFirstAncestorOfType<FieldRowGroup>();
        }
    }
}
