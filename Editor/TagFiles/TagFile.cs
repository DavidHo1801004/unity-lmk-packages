using System;
using System.Collections.Generic;
using System.Text;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// One tag of a tag file.
    /// </summary>
    internal readonly struct TagFileEntry
    {
        public readonly string Path;

        /// <summary>
        /// The text after <c>;</c>, trimmed. <see langword="null"/> if the line has no <c>;</c>, so importing
        /// it never changes a description.
        /// </summary>
        public readonly string Description;

        /// <summary>
        /// Where the entry came from, e.g. <c>Combat.tags:12</c>. Diagnostics only.
        /// </summary>
        public readonly string Location;

        public bool HasDescription => Description != null;

        public TagFileEntry(string _Path, string _Description = null, string _Location = null)
        {
            Path = _Path;
            Description = _Description;
            Location = _Location ?? string.Empty;
        }
    }



    /// <summary>
    /// Result of parsing one or more tag files.
    /// </summary>
    internal sealed class TagFileParseResult
    {
        /// <summary>
        /// One entry per path, in order of first appearance. The same path in several lines or files is one entry.
        /// </summary>
        public readonly List<TagFileEntry> Entries = new();

        /// <summary>
        /// Problems that stop an import, e.g. an invalid path.
        /// </summary>
        public readonly List<string> Errors = new();

        /// <summary>
        /// Problems that don't, e.g. a path listed twice with different descriptions.
        /// </summary>
        public readonly List<string> Warnings = new();

        public bool IsValid => Errors.Count == 0;
    }



    /// <summary>
    /// Reads and writes tag files (<c>*.tags</c>): import data only, with no IDs.
    /// </summary>
    /// <remarks>
    /// <code>
    /// # Combat.tags
    /// Ability.Fire.Projectile ; Fired projectile abilities
    /// Status.Burning          ; Damage over time from fire
    /// </code>
    /// One tag per line: <c>&lt;path&gt; [; &lt;description&gt;]</c>. Lines starting with <c>#</c> and blank
    /// lines are ignored. The description is everything after the first <c>;</c>, so it may contain <c>;</c> itself.
    /// </remarks>
    internal static class TagFile
    {
        public const string EXTENSION = "tags";

        private const char DESCRIPTION_SEPARATOR = ';';

        // Paths longer than this aren't padded, so one long path doesn't push every description far right.
        private const int MAX_ALIGN_WIDTH = 48;



        #region Parse
        /// <summary>
        /// Parse one tag file.
        /// </summary>
        /// <param name="_SourceName"> File name used in messages. </param>
        public static TagFileParseResult Parse(string _Text, string _SourceName = null)
        {
            var result = new TagFileParseResult();
            ParseInto(result, new Dictionary<string, int>(StringComparer.Ordinal), _Text, _SourceName);
            return result;
        }

        /// <summary>
        /// Parse several tag files into one set of entries: the same path in several tag files becomes one tag.
        /// </summary>
        public static TagFileParseResult Parse(IEnumerable<(string Name, string Text)> _Files)
        {
            var result = new TagFileParseResult();
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var (name, text) in _Files)
                ParseInto(result, indices, text, name);

            return result;
        }

        private static void ParseInto(TagFileParseResult _Result, Dictionary<string, int> _Indices, string _Text, string _SourceName)
        {
            string source = string.IsNullOrEmpty(_SourceName) ? "Line" : _SourceName;
            string[] lines = (_Text ?? string.Empty).Replace("\r", string.Empty).Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string location = $"{source}:{i + 1}";
                string line = lines[i].Trim();

                if (line.Length == 0 || line[0] == '#') continue;

                if (line.StartsWith("<<<<<<<") || line.StartsWith("=======") || line.StartsWith(">>>>>>>"))
                {
                    _Result.Errors.Add($"{location}: version control conflict marker. Resolve the conflict first.");
                    continue;
                }

                int separator = line.IndexOf(DESCRIPTION_SEPARATOR);
                string path = (separator < 0 ? line : line[..separator]).Trim();
                string description = separator < 0 ? null : line[(separator + 1)..].Trim();

                if (!TagPath.IsValidPath(path, out string error))
                {
                    _Result.Errors.Add($"{location}: {error}");
                    continue;
                }

                if (!_Indices.TryGetValue(path, out int index))
                {
                    _Indices.Add(path, _Result.Entries.Count);
                    _Result.Entries.Add(new TagFileEntry(path, description, location));
                    continue;
                }

                // Same path again: keep the first description, and fill it in if the first line had none.
                var first = _Result.Entries[index];
                if (description == null || description == first.Description) continue;

                if (!first.HasDescription || first.Description.Length == 0)
                    _Result.Entries[index] = new TagFileEntry(path, description, first.Location);
                else if (description.Length > 0)
                    _Result.Warnings.Add($"{location}: \"{path}\" is also listed at {first.Location} with another description. The first one is used.");
            }
        }
        #endregion



        #region Write
        /// <summary>
        /// Write entries as a tag file: sorted by path, descriptions aligned, LF line endings.
        /// </summary>
        /// <param name="_Header"> Comment lines put at the top, without the leading <c>#</c>. </param>
        public static string Write(IEnumerable<TagFileEntry> _Entries, IEnumerable<string> _Header = null)
        {
            var entries = new List<TagFileEntry>(_Entries);
            entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

            int width = 0;
            foreach (var entry in entries)
                if (!string.IsNullOrEmpty(entry.Description))
                    width = Math.Max(width, Math.Min(entry.Path.Length, MAX_ALIGN_WIDTH));

            var builder = new StringBuilder();
            if (_Header != null)
                foreach (string line in _Header)
                    builder.Append("# ").Append(SingleLine(line)).Append('\n');

            foreach (var entry in entries)
            {
                string description = SingleLine(entry.Description);
                if (description.Length == 0)
                {
                    builder.Append(entry.Path).Append('\n');
                    continue;
                }

                builder.Append(entry.Path.PadRight(width)).Append(' ').Append(DESCRIPTION_SEPARATOR).Append(' ')
                       .Append(description).Append('\n');
            }

            return builder.ToString();
        }

        private static string SingleLine(string _Text)
            => string.IsNullOrEmpty(_Text) ? string.Empty : _Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        #endregion
    }
}
