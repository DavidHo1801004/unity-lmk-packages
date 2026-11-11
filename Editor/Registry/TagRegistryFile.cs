using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Result of parsing a registry file.
    /// </summary>
    internal sealed class TagRegistryParseResult
    {
        /// <summary>
        /// Parsed content, or <see langword="null"/> if the file could not be read at all.
        /// </summary>
        public TagRegistryData Data;

        /// <summary>
        /// Problems that make the file unusable. The registry is rejected if there is any.
        /// </summary>
        public readonly List<string> Errors = new();

        /// <summary>
        /// Problems that don't stop the registry from loading, e.g. a checksum mismatch.
        /// </summary>
        public readonly List<string> Warnings = new();

        /// <summary>
        /// Whether the declared checksum differs from the rows, i.e. the file was edited outside the manager.
        /// </summary>
        public bool ChecksumMismatch;

        public bool IsValid => Data != null && Errors.Count == 0;
    }



    /// <summary>
    /// Reads and writes registry files (<c>*.tagregistry</c>, format v3).
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
    /// # guid                             | parent                           | segment | flags    | description
    /// 0a91c3e27f4b4d0c9e1f55b2c8d7a610 | -                                | Ability | implicit |
    /// </code>
    /// Rows are sorted by GUID. The checksum is the SHA-256 of the row lines, each with trailing whitespace
    /// trimmed, joined with LF and ending with LF. CR characters are ignored, so a CRLF working copy still matches.
    /// </remarks>
    internal static class TagRegistryFile
    {
        public const string EXTENSION = "tagregistry";

        public const string FORMAT_LINE = "# gameplaytag-registry v3 — written by the Gameplay Tag Manager. Do not edit by hand.";

        private const string CHECKSUM_PREFIX = "sha256:";
        private const string FLAG_IMPLICIT = "implicit";
        private const string NONE = "-";
        private const char COLUMN = '|';
        private const int KEY_WIDTH = 10;
        private const int GUID_WIDTH = TagGuid.TEXT_LENGTH;
        private const int FLAGS_WIDTH = 8;



        #region Kind
        public static string KindToText(TagRegistryKind _Kind) => _Kind switch
        {
            TagRegistryKind.Main => "main",
            TagRegistryKind.Package => "package",
            TagRegistryKind.Generated => "generated",
            _ => "main",
        };

        public static bool TryParseKind(string _Text, out TagRegistryKind _Kind)
        {
            switch (_Text)
            {
                case "main": _Kind = TagRegistryKind.Main; return true;
                case "package": _Kind = TagRegistryKind.Package; return true;
                case "generated": _Kind = TagRegistryKind.Generated; return true;
                default: _Kind = TagRegistryKind.Main; return false;
            }
        }
        #endregion



        #region Parse
        /// <summary>
        /// Parse and validate the text of a registry file.
        /// </summary>
        public static TagRegistryParseResult Parse(string _Text)
        {
            var result = new TagRegistryParseResult();
            var data = new TagRegistryData();
            var rowLines = new List<string>();

            string declaredChecksum = null;
            bool hasRegistry = false, hasKind = false;

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
                    if (TryParseRow(line, lineNumber, result.Errors, out var row))
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
                        if (TagGuid.TryParse(value, out data.registryId) && !data.registryId.IsZero) hasRegistry = true;
                        else result.Errors.Add($"Line {lineNumber}: invalid registry ID \"{value}\".");
                        break;

                    case "name":
                        data.name = value;
                        break;

                    case "kind":
                        if (TryParseKind(value, out data.kind)) hasKind = true;
                        else result.Errors.Add($"Line {lineNumber}: unknown registry kind \"{value}\".");
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

            if (!hasRegistry && !result.Errors.Exists(e => e.Contains("registry ID")))
                result.Errors.Add("Missing \"registry\" header.");
            if (!hasKind && !result.Errors.Exists(e => e.Contains("registry kind")))
                result.Errors.Add("Missing \"kind\" header.");

            // Checksum: only a warning, the file still loads.
            string actualChecksum = CHECKSUM_PREFIX + ComputeChecksum(rowLines);
            if (string.IsNullOrEmpty(declaredChecksum))
            {
                result.ChecksumMismatch = true;
                result.Warnings.Add("Missing checksum: the file was not written by the Gameplay Tag Manager.");
            }
            else if (!string.Equals(declaredChecksum, actualChecksum, StringComparison.Ordinal))
            {
                result.ChecksumMismatch = true;
                result.Warnings.Add("Checksum mismatch: the file was edited outside the Gameplay Tag Manager.");
            }

            Validate(data, result.Errors);

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

        private static bool TryParseRow(string _Line, int _LineNumber, List<string> _Errors, out TagRegistryRow _Row)
        {
            _Row = default;

            // The description is the last column and may itself contain '|'.
            string[] parts = _Line.Split(COLUMN, 5);
            if (parts.Length < 4)
            {
                _Errors.Add($"Line {_LineNumber}: a row needs the columns guid | parent | segment | flags | description.");
                return false;
            }

            string guidText = parts[0].Trim();
            string parentText = parts[1].Trim();
            string segment = parts[2].Trim();
            string flags = parts[3].Trim();
            string description = parts.Length > 4 ? parts[4].Trim() : string.Empty;

            if (!TagGuid.TryParse(guidText, out var id) || id.IsZero)
            {
                _Errors.Add($"Line {_LineNumber}: invalid tag ID \"{guidText}\".");
                return false;
            }

            var parent = TagGuid.Zero;
            if (parentText != NONE && (!TagGuid.TryParse(parentText, out parent) || parent.IsZero))
            {
                _Errors.Add($"Line {_LineNumber}: invalid parent ID \"{parentText}\".");
                return false;
            }

            bool isImplicit;
            if (flags == FLAG_IMPLICIT) isImplicit = true;
            else if (flags == NONE || flags.Length == 0) isImplicit = false;
            else
            {
                _Errors.Add($"Line {_LineNumber}: unknown flags \"{flags}\".");
                return false;
            }

            _Row = new TagRegistryRow(id, parent, segment, isImplicit, description);
            return true;
        }
        #endregion



        #region Validation
        /// <summary>
        /// Check the integrity of registry content: unique IDs, valid segments, existing parents, no cycles,
        /// unique segments among siblings.
        /// </summary>
        public static void Validate(TagRegistryData _Data, List<string> _Errors)
        {
            var byId = new Dictionary<TagGuid, TagRegistryRow>(_Data.rows.Count);
            foreach (var row in _Data.rows)
            {
                if (row.id.IsZero)
                    _Errors.Add("A row has a zero ID.");
                else if (!byId.TryAdd(row.id, row))
                    _Errors.Add($"Duplicate tag ID {row.id}.");

                if (!TagPath.IsValidSegment(row.segment, out string error))
                    _Errors.Add($"Row {row.id}: {error}");
            }

            var siblings = new HashSet<(TagGuid, string)>();
            foreach (var row in byId.Values)
            {
                if (!row.parent.IsZero && !byId.ContainsKey(row.parent))
                    _Errors.Add($"Row {row.id} (\"{row.segment}\"): parent {row.parent} does not exist.");

                if (!siblings.Add((row.parent, row.segment)))
                    _Errors.Add($"Two tags named \"{row.segment}\" share the parent {(row.parent.IsZero ? "(root)" : row.parent.ToString())}.");
            }

            // Cycles: walking up from any row must reach a root within row count steps.
            foreach (var row in byId.Values)
            {
                var current = row;
                for (int steps = 0; !current.parent.IsZero; steps++)
                {
                    if (steps > byId.Count || !byId.TryGetValue(current.parent, out current))
                    {
                        if (steps > byId.Count)
                            _Errors.Add($"Row {row.id} (\"{row.segment}\") is part of a parent cycle.");
                        break;
                    }
                }
            }
        }
        #endregion



        #region Write
        /// <summary>
        /// Write registry content in the file format: rows sorted by GUID, aligned columns, LF line endings,
        /// and a fresh checksum.
        /// </summary>
        public static string Write(TagRegistryData _Data)
        {
            var rows = new List<TagRegistryRow>(_Data.rows);
            rows.Sort((a, b) => a.id.CompareTo(b.id));

            int segmentWidth = "segment".Length;
            foreach (var row in rows)
                segmentWidth = Math.Max(segmentWidth, row.segment?.Length ?? 0);

            var rowLines = new List<string>(rows.Count);
            foreach (var row in rows)
            {
                string line =
                    $"{row.id} {COLUMN} " +
                    $"{(row.parent.IsZero ? NONE : row.parent.ToString()).PadRight(GUID_WIDTH)} {COLUMN} " +
                    $"{(row.segment ?? string.Empty).PadRight(segmentWidth)} {COLUMN} " +
                    $"{(row.isImplicit ? FLAG_IMPLICIT : NONE).PadRight(FLAGS_WIDTH)} {COLUMN} " +
                    SingleLine(row.description);

                rowLines.Add(line.TrimEnd());
            }

            var builder = new StringBuilder();
            builder.Append(FORMAT_LINE).Append('\n');
            AppendHeader(builder, "registry", _Data.registryId.ToString());
            AppendHeader(builder, "name", SingleLine(_Data.name));
            AppendHeader(builder, "kind", KindToText(_Data.kind));
            if (!string.IsNullOrWhiteSpace(_Data.package))
                AppendHeader(builder, "package", SingleLine(_Data.package));
            if (!string.IsNullOrWhiteSpace(_Data.version))
                AppendHeader(builder, "version", SingleLine(_Data.version));
            AppendHeader(builder, "checksum", CHECKSUM_PREFIX + ComputeChecksum(rowLines));

            builder.Append(
                $"# {"guid".PadRight(GUID_WIDTH - 2)} {COLUMN} " +
                $"{"parent".PadRight(GUID_WIDTH)} {COLUMN} " +
                $"{"segment".PadRight(segmentWidth)} {COLUMN} " +
                $"{"flags".PadRight(FLAGS_WIDTH)} {COLUMN} description").Append('\n');

            foreach (string line in rowLines)
                builder.Append(line).Append('\n');

            return builder.ToString();
        }

        private static void AppendHeader(StringBuilder _Builder, string _Key, string _Value)
            => _Builder.Append(_Key.PadRight(KEY_WIDTH)).Append(_Value).Append('\n');

        private static string SingleLine(string _Text)
            => string.IsNullOrEmpty(_Text) ? string.Empty : _Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        #endregion



        #region Hashing
        /// <summary>
        /// SHA-256 hex of the row lines: each trimmed at the end, joined with LF, with a final LF.
        /// </summary>
        public static string ComputeChecksum(IEnumerable<string> _RowLines)
        {
            var builder = new StringBuilder();
            foreach (string line in _RowLines)
                builder.Append(line.Replace("\r", string.Empty).TrimEnd()).Append('\n');

            return Sha256Hex(builder.ToString());
        }

        /// <summary>
        /// Hash of a whole file, ignoring CR characters. Used to detect changes made outside the manager.
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
