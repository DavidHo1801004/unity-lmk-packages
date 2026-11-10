using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Container of <see cref="FieldRow"/>s whose labels all get the same width, so their values line up. <br/>
    /// With <see cref="LabelWidth"/> 0 the width is the widest label's, measured again whenever a label changes
    /// or a row is added.
    /// </summary>
    /// <remarks>
    /// Rows can be nested in other elements inside the group, but rows in a nested group belong to that group.
    /// </remarks>
    [UxmlElement("FieldRowGroup", libraryPath = "LMK/Layout", visibility = LibraryVisibility.Visible)]
    public partial class FieldRowGroup : VisualElement
    {
        private float labelWidth;
        /// <summary>
        /// Label width of every row, or 0 to use the widest label.
        /// </summary>
        [UxmlAttribute(name = "label-width")]
        public float LabelWidth
        {
            get => labelWidth;
            set
            {
                labelWidth = Mathf.Max(value, 0f);
                Align();
            }
        }

        private float rowSpacing = 2f;
        /// <summary>
        /// Vertical space between rows, in pixels.
        /// </summary>
        [UxmlAttribute(name = "row-spacing")]
        public float RowSpacing
        {
            get => rowSpacing;
            set
            {
                rowSpacing = value;
                Align();
            }
        }

        internal const string USS_CLASS = "lmk-field-row-group";

        private readonly List<FieldRow> rows = new();



        public FieldRowGroup()
        {
            LMKTheme.ApplyIfStandalone(this);
            LMKTheme.AddStyleSheet(this, FieldRow.USS_PATH);

            AddToClassList(USS_CLASS);

            RegisterCallback<GeometryChangedEvent>(_ => Align());
        }



        /// <summary>
        /// Give every row of this group the same label width and the row spacing.
        /// Needs resolved styles, so it does nothing until the group is in a panel.
        /// </summary>
        internal void Align()
        {
            if (panel == null) return;

            rows.Clear();
            CollectRows(this, rows);

            bool isFixed = labelWidth > 0f;
            float width = labelWidth;
            if (!isFixed)
            {
                foreach (var row in rows)
                    width = Mathf.Max(width, row.MeasureLabelWidth());
            }

            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].ApplyLabelWidth(this, width, isFixed);
                rows[i].SetSpacing(i == 0 ? 0f : rowSpacing);
            }
        }

        // Rows of this group in hierarchy order, without descending into nested groups.
        private static void CollectRows(VisualElement _Element, List<FieldRow> _Rows)
        {
            foreach (var child in _Element.hierarchy.Children())
            {
                if (child is FieldRow row)
                    _Rows.Add(row);
                else if (child is not FieldRowGroup)
                    CollectRows(child, _Rows);
            }
        }
    }
}
