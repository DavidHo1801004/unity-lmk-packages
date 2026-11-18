using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Field with a label and any content as its input, whose label lines up with the inspector's built-in fields. <br/>
    /// For custom property drawers whose value isn't a single built-in field, e.g. a button that opens a picker.
    /// </summary>
    /// <remarks>
    /// <see cref="VisualElement.Add"/> and UXML children go into the input, which lays them out in a row. <br/>
    /// The field has no value of its own: bind or update the content instead. <br/>
    /// Labels line up inside an <c>InspectorElement</c>, like the fields a <c>PropertyField</c> creates.
    /// </remarks>
    [UxmlElement("AlignedField", libraryPath = "LMK/Fields", visibility = LibraryVisibility.Visible)]
    public partial class AlignedField : BaseField<object>
    {
        private bool alignLabelToTop;
        /// <summary>
        /// Whether the label sits at the top of the field instead of the middle, for content taller than one line,
        /// e.g. a list of tags that wraps.
        /// </summary>
        [UxmlAttribute(name = "align-label-to-top")]
        public bool AlignLabelToTop
        {
            get => alignLabelToTop;
            set
            {
                alignLabelToTop = value;
                EnableInClassList(USS_CLASS_LABEL_TOP, alignLabelToTop);
            }
        }

        /// <summary>
        /// The input area, right of the label. Same as <see cref="contentContainer"/>.
        /// </summary>
        public VisualElement Input { get; }

        /// <summary>
        /// The input. <see cref="VisualElement.Add"/> and UXML children go here.
        /// </summary>
        public override VisualElement contentContainer => Input ?? this; // BaseField's constructor adds the label and input with Add()

        /// <summary>
        /// USS class of the field.
        /// </summary>
        public new const string ussClassName = "lmk-aligned-field";

        internal const string USS_CLASS_LABEL_TOP = ussClassName + "--label-top";

        private const string USS_PATH = LMKAssetUtility.EDITOR_PACKAGE_ROOT + "/Editor/Visual Elements/Assets/AlignedFieldUSS.uss";



        public AlignedField()
            : this(null) { }

        /// <param name="_Label"> Text of the label, or <see langword="null"/> for no label. </param>
        public AlignedField(string _Label)
            : this(_Label, new VisualElement()) { }

        private AlignedField(string _Label, VisualElement _Input)
            : base(_Label, _Input)
        {
            Input = _Input;

            LMKTheme.ApplyIfStandalone(this);
            LMKTheme.AddStyleSheet(this, USS_PATH);

            AddToClassList(ussClassName);
            AddToClassList(alignedFieldUssClassName);
        }
    }
}
