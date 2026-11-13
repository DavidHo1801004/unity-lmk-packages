using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Reads and writes registry files of every kind.
    /// </summary>
    /// <remarks>
    /// <code>
    /// # gameplaytag-registry v3 — written by the Gameplay Tag Manager. Do not edit by hand.
    /// registry  6b1f0c2a9d7e4b3f8a5c1d0e2f3a4b5c
    /// name      Main
    /// kind      main
    /// package   com.vendor.combat   (generated registries only)
    /// version   1.2.0               (generated registries only)
    /// checksum  sha256:…
    /// # guid                           | parent                           | segment | flags    | description
    /// 0a91c3e27f4b4d0c9e1f55b2c8d7a610 | -                                | Ability | implicit |
    /// </code>
    /// The header is the same for every kind; the columns after <c>guid</c> are the kind's
    /// (<see cref="IRegistryFormat{TRow}"/>). Rows are sorted by GUID, with padded columns and LF line endings.
    /// The checksum is the SHA-256 of the row lines, each with trailing whitespace trimmed, joined with LF and
    /// ending with LF. CR characters are ignored, so a CRLF working copy still matches.
    /// </remarks>
    public static class RegistryFile
    {
        public const char COLUMN = '|';

        /// <summary>
        /// Written for a missing reference to another row, or an empty flags cell.
        /// </summary>
        public const string NONE = "-";

        private const string CHECKSUM_PREFIX = "sha256:";
        private const int KEY_WIDTH = 10;
        private const int GUID_WIDTH = RegistryGuid.TEXT_LENGTH;



        #region Origin
        /// <summary>
        /// The header's <c>kind</c> value of an origin.
        /// </summary>
        public static string OriginToText(RegistryOrigin _Origin) => _Origin switch
        {
            RegistryOrigin.Main => "main",
            RegistryOrigin.Package => "package",
            RegistryOrigin.Generated => "generated",
            _ => "main",
        };

        public static bool TryParseOrigin(string _Text, out RegistryOrigin _Origin)
        {
            switch (_Text)
            {
                case "main": _Origin = RegistryOrigin.Main; return true;
                case "package": _Origin = RegistryOrigin.Package; return true;
                case "generated": _Origin = RegistryOrigin.Generated; return true;
                default: _Origin = RegistryOrigin.Main; return false;
            }
        }
        #endregion



        #region Parse
        /// <summary>
        /// Parse and validate the text of a registry file.
        /// </summary>
        public static RegistryParseResult<TRow> Parse<TRow>(string _Text, IRegistryFormat<TRow> _Format) where TRow : struct
        {
            if (_Format == null) throw new ArgumentNullException(nameof(_Format));
            CheckColumns(_Format);

            var result = new RegistryParseResult<TRow>();
            var data = new RegistryData<TRow>();
            var rowLines = new List<string>();

            string declaredChecksum = null;
            bool hasRegistry = false, hasOrigin = false;
            bool registryError = false, originError = false;

            string[] lines = (_Text ?? string.Empty).Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNumber = i + 1;
                string line = lines[i].TrimEnd();
                string trimmed = line.TrimStart();

                if (trimmed.StartsWith("<<<<<<<") || trimmed.StartsWith("=======") || trimmed.StartsWith(">>>>>>>"))
                {
                    result.Errors.Add($"Line {lineNumber}: version control conflict marker. Resolve the conflict first.");
                    continue;
                }

                if (trimmed.Length == 0 || trimmed[0] == '#') continue;

                // Row
                if (line.IndexOf(COLUMN) >= 0)
                {
                    rowLines.Add(line);
                    if (TryParseRow(line, lineNumber, _Format, result.Errors, out var row))
                        data.rows.Add(row);

                    continue;
                }

                // Header
                if (rowLines.Count > 0)
                {
                    result.Errors.Add($"Line {lineNumber}: header line after the first row.");
                    continue;
                }

                SplitHeader(trimmed, out string key, out string value);
                switch (key)
                {
                    case "registry":
                        if (RegistryGuid.TryParse(value, out data.registryId) && !data.registryId.IsZero) hasRegistry = true;
                        else
                        {
                            registryError = true;
                            result.Errors.Add($"Line {lineNumber}: invalid registry ID \"{value}\".");
                        }
                        break;

                    case "name":
                        data.name = value;
                        break;

                    case "kind":
                        if (TryParseOrigin(value, out data.origin)) hasOrigin = true;
                        else
                        {
                            originError = true;
                            result.Errors.Add($"Line {lineNumber}: unknown registry kind \"{value}\".");
                        }
                        break;

                    case "package":
                        data.package = value;
                        break;

                    case "version":
                        data.version = value;
                        break;

                    case "checksum":
                        declaredChecksum = value;
                        break;

                    default:
                        result.Warnings.Add($"Line {lineNumber}: unknown header \"{key}\" is ignored.");
                        break;
                }
            }

            if (!hasRegistry && !registryError)
                result.Errors.Add("Missing \"registry\" header.");
            if (!hasOrigin && !originError)
                result.Errors.Add("Missing \"kind\" header.");

            // Checksum: only a warning, the file still loads.
            string actualChecksum = CHECKSUM_PREFIX + ComputeChecksum(rowLines);
            if (string.IsNullOrEmpty(declaredChecksum))
            {
                result.ChecksumMismatch = true;
                result.Warnings.Add($"Missing checksum: the file was not written by the {_Format.EditorName}.");
            }
            else if (!string.Equals(declaredChecksum, actualChecksum, StringComparison.Ordinal))
            {
                result.ChecksumMismatch = true;
                result.Warnings.Add($"Checksum mismatch: the file was edited outside the {_Format.EditorName}.");
            }

            Validate(data, _Format, result.Errors);

            result.Data = data;
            return result;
        }

        private static void SplitHeader(string _Line, out string _Key, out string _Value)
        {
            int index = 0;
            while (index < _Line.Length && !char.IsWhiteSpace(_Line[index]))
                index++;

            _Key = _Line[..index];
            _Value = _Line[index..].Trim();
        }

        private static bool TryParseRow<TRow>(string _Line, int _LineNumber, IRegistryFormat<TRow> _Format, List<string> _Errors,
                                              out RegistryRow<TRow> _Row) where TRow : struct
        {
            _Row = default;
            var columns = _Format.Columns;

            // The last column may itself contain the separator, and may be missing altogether.
            string[] parts = _Line.Split(COLUMN, columns.Count + 1);
            if (parts.Length < columns.Count)
            {
                var names = new StringBuilder("guid");
                foreach (var column in columns)
                    names.Append(' ').Append(COLUMN).Append(' ').Append(column.Name);

                _Errors.Add($"Line {_LineNumber}: a row needs the columns {names}.");
                return false;
            }

            string guidText = parts[0].Trim();
            if (!RegistryGuid.TryParse(guidText, out var id) || id.IsZero)
            {
                _Errors.Add($"Line {_LineNumber}: invalid {_Format.RowNoun} ID \"{guidText}\".");
                return false;
            }

            var cells = new string[columns.Count];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = i + 1 < parts.Length ? parts[i + 1].Trim() : string.Empty;

            if (!_Format.TryParseRow(cells, out var data, out string error))
            {
                _Errors.Add($"Line {_LineNumber}: {error}");
                return false;
            }

            _Row = new RegistryRow<TRow>(id, data);
            return true;
        }
        #endregion



        #region Validation
        /// <summary>
        /// Check the integrity of registry content: non-zero, unique row IDs, then the kind's own rules.
        /// </summary>
        public static void Validate<TRow>(RegistryData<TRow> _Data, IRegistryFormat<TRow> _Format, List<string> _Errors) where TRow : struct
        {
            var ids = new HashSet<RegistryGuid>();
            foreach (var row in _Data.rows)
            {
                if (row.id.IsZero)
                    _Errors.Add("A row has a zero ID.");
                else if (!ids.Add(row.id))
                    _Errors.Add($"Duplicate {_Format.RowNoun} ID {row.id}.");
            }

            _Format.Validate(_Data, _Errors);
        }
        #endregion



        #region Write
        /// <summary>
        /// Write registry content in the file format: rows sorted by GUID, padded columns, LF line endings,
        /// and a fresh checksum. The same content always gives the same text.
        /// </summary>
        public static string Write<TRow>(RegistryData<TRow> _Data, IRegistryFormat<TRow> _Format) where TRow : struct
        {
            if (_Format == null) throw new ArgumentNullException(nameof(_Format));
            CheckColumns(_Format);

            var columns = _Format.Columns;
            var rows = new List<RegistryRow<TRow>>(_Data.rows);
            rows.Sort((a, b) => a.id.CompareTo(b.id));

            var cells = new List<string[]>(rows.Count);
            foreach (var row in rows)
            {
                string[] rowCells = _Format.FormatRow(row.data) ?? Array.Empty<string>();
                var normalized = new string[columns.Count];
                for (int i = 0; i < normalized.Length; i++)
                    normalized[i] = SingleLine(i < rowCells.Length ? rowCells[i] : null);

                cells.Add(normalized);
            }

            var widths = new int[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                widths[i] = columns[i].Width switch
                {
                    RegistryColumnWidth.Fixed => columns[i].FixedWidth,
                    RegistryColumnWidth.FitContent => columns[i].Name.Length,
                    _ => 0,
                };

                if (columns[i].Width == RegistryColumnWidth.FitContent)
                    foreach (var rowCells in cells)
                        widths[i] = Math.Max(widths[i], rowCells[i].Length);
            }

            var rowLines = new List<string>(rows.Count);
            for (int r = 0; r < rows.Count; r++)
            {
                var line = new StringBuilder().Append(rows[r].id).Append(' ').Append(COLUMN).Append(' ');
                AppendCells(line, cells[r], widths);
                rowLines.Add(line.ToString().TrimEnd());
            }

            var builder = new StringBuilder();
            builder.Append(_Format.FormatLine).Append('\n');
            AppendHeader(builder, "registry", _Data.registryId.ToString());
            AppendHeader(builder, "name", SingleLine(_Data.name));
            AppendHeader(builder, "kind", OriginToText(_Data.origin));
            if (!string.IsNullOrWhiteSpace(_Data.package))
                AppendHeader(builder, "package", SingleLine(_Data.package));
            if (!string.IsNullOrWhiteSpace(_Data.version))
                AppendHeader(builder, "version", SingleLine(_Data.version));
            AppendHeader(builder, "checksum", CHECKSUM_PREFIX + ComputeChecksum(rowLines));

            var names = new string[columns.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = columns[i].Name;

            var columnLine = new StringBuilder("# ").Append("guid".PadRight(GUID_WIDTH - 2)).Append(' ').Append(COLUMN).Append(' ');
            AppendCells(columnLine, names, widths);
            builder.Append(columnLine).Append('\n');

            foreach (string line in rowLines)
                builder.Append(line).Append('\n');

            return builder.ToString();
        }

        private static void AppendCells(StringBuilder _Builder, string[] _Cells, int[] _Widths)
        {
            for (int i = 0; i < _Cells.Length; i++)
            {
                bool last = i == _Cells.Length - 1;
                _Builder.Append(last ? _Cells[i] : _Cells[i].PadRight(_Widths[i]));
                if (!last) _Builder.Append(' ').Append(COLUMN).Append(' ');
            }
        }

        private static void AppendHeader(StringBuilder _Builder, string _Key, string _Value)
            => _Builder.Append(_Key.PadRight(KEY_WIDTH)).Append(_Value).Append('\n');

        private static string SingleLine(string _Text)
            => string.IsNullOrEmpty(_Text) ? string.Empty : _Text.Replace('\r', ' ').Replace('\n', ' ').Trim();

        private static void CheckColumns<TRow>(IRegistryFormat<TRow> _Format) where TRow : struct
        {
            var columns = _Format.Columns;
            if (columns == null || columns.Count == 0 || columns[^1].Width != RegistryColumnWidth.Last)
                throw new ArgumentException("A registry format needs at least one column, and its last column must be Last.", nameof(_Format));

            for (int i = 0; i < columns.Count - 1; i++)
                if (columns[i].Width == RegistryColumnWidth.Last)
                    throw new ArgumentException($"Only the last column can be Last, not \"{columns[i].Name}\".", nameof(_Format));
        }
        #endregion



        #region Disk
        /// <summary>
        /// Write <paramref name="_Text"/> to a temporary file, then swap it in, so a crash never leaves a
        /// half-written file. UTF-8 without BOM.
        /// </summary>
        public static void WriteAtomic(string _Path, string _Text)
        {
            string target = Path.GetFullPath(_Path);
            string tempFolder = Path.GetFullPath(Path.Combine("Temp", "LMKRegistry"));
            string temp = Path.Combine(tempFolder, Path.GetFileName(_Path) + ".tmp");

            Directory.CreateDirectory(tempFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllText(temp, _Text ?? string.Empty, new UTF8Encoding(false));

            if (!File.Exists(target))
            {
                File.Move(temp, target);
                return;
            }

            // Right after an import, the file can still be open for a moment (importer, indexer, antivirus),
            // which makes the swap fail on Windows. Retry briefly, then fall back to copying over it.
            const int ATTEMPTS = 5;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Replace(temp, target, null);
                    return;
                }
                catch (IOException) when (attempt < ATTEMPTS)
                {
                    System.Threading.Thread.Sleep(20 * attempt);
                }
                catch (IOException exception)
                {
                    Debug.LogWarning($"Could not swap \"{_Path}\" in atomically ({exception.Message}). It was overwritten in place.");
                    File.Copy(temp, target, true);
                    File.Delete(temp);
                    return;
                }
            }
        }
        #endregion



        #region Hashing
        /// <summary>
        /// SHA-256 hex of row lines: each trimmed at the end, joined with LF, with a final LF.
        /// </summary>
        public static string ComputeChecksum(IEnumerable<string> _RowLines)
        {
            var builder = new StringBuilder();
            foreach (string line in _RowLines)
                builder.Append(line.Replace("\r", string.Empty).TrimEnd()).Append('\n');

            return Sha256Hex(builder.ToString());
        }

        /// <summary>
        /// Hash of a whole file, ignoring CR characters. Used to detect changes made outside the editor.
        /// </summary>
        public static string HashText(string _Text)
            => Sha256Hex((_Text ?? string.Empty).Replace("\r", string.Empty));

        private static string Sha256Hex(string _Text)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(_Text));

            var hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                hex.Append(b.ToString("x2"));

            return hex.ToString();
        }
        #endregion
    }
}
