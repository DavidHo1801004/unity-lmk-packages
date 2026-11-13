using System;
using System.Collections.Generic;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// How a column after <c>guid</c> is padded when a registry file is written.
    /// </summary>
    public enum RegistryColumnWidth
    {
        /// <summary>
        /// Padded to <see cref="RegistryColumn.FixedWidth"/> characters.
        /// </summary>
        Fixed,

        /// <summary>
        /// Padded to the longest cell of the file, or the column name.
        /// </summary>
        FitContent,

        /// <summary>
        /// The last column: not padded, and it may contain the column separator.
        /// </summary>
        Last,
    }



    /// <summary>
    /// One column of a kind's rows, after the <c>guid</c> column the core owns.
    /// </summary>
    public readonly struct RegistryColumn
    {
        public readonly string Name;
        public readonly RegistryColumnWidth Width;
        public readonly int FixedWidth;

        private RegistryColumn(string _Name, RegistryColumnWidth _Width, int _FixedWidth)
        {
            if (string.IsNullOrEmpty(_Name)) throw new ArgumentException("A column needs a name.", nameof(_Name));

            Name = _Name;
            Width = _Width;
            FixedWidth = _FixedWidth;
        }

        public static RegistryColumn Fixed(string _Name, int _Width) => new(_Name, RegistryColumnWidth.Fixed, Math.Max(_Width, _Name.Length));

        public static RegistryColumn FitContent(string _Name) => new(_Name, RegistryColumnWidth.FitContent, 0);

        public static RegistryColumn Last(string _Name) => new(_Name, RegistryColumnWidth.Last, 0);
    }



    /// <summary>
    /// What a registry kind supplies to read and write its registry files. The core owns the header,
    /// the <c>guid</c> column, row order, the checksum and the integrity of IDs.
    /// </summary>
    /// <typeparam name="TRow"> The kind's row data: every column after <c>guid</c>. </typeparam>
    public interface IRegistryFormat<TRow> where TRow : struct
    {
        /// <summary>
        /// The comment line at the top of every file, naming the format and its version,
        /// e.g. <c># gameplaytag-registry v3 — written by the Gameplay Tag Manager. Do not edit by hand.</c>
        /// </summary>
        string FormatLine { get; }

        /// <summary>
        /// What one row is called in messages, in lower case, e.g. "tag".
        /// </summary>
        string RowNoun { get; }

        /// <summary>
        /// The tool that writes the files, for messages, e.g. "Gameplay Tag Manager".
        /// </summary>
        string EditorName { get; }

        /// <summary>
        /// The columns after <c>guid</c>, in file order. The last one must be <see cref="RegistryColumnWidth.Last"/>;
        /// it is optional in a row and may contain the column separator.
        /// </summary>
        IReadOnlyList<RegistryColumn> Columns { get; }

        /// <summary>
        /// Read one row's cells, already trimmed, one per column of <see cref="Columns"/>.
        /// </summary>
        /// <param name="_Error"> Why the row is invalid, e.g. <c>invalid parent ID "x"</c>. Set when returning false. </param>
        bool TryParseRow(string[] _Cells, out TRow _Row, out string _Error);

        /// <summary>
        /// Write one row's cells, one per column of <see cref="Columns"/>. Cells other than the last must not
        /// contain the column separator or line breaks.
        /// </summary>
        string[] FormatRow(in TRow _Row);

        /// <summary>
        /// Add the kind's integrity errors, e.g. missing parents or clamp cycles. The core has already checked
        /// that every row ID is non-zero and unique.
        /// </summary>
        void Validate(RegistryData<TRow> _Data, List<string> _Errors);
    }
}
