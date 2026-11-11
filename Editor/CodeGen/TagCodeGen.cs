using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using UnityEditor;
using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Generates <c>GameplayTags.g.cs</c>: one <see cref="GameplayTag"/> constant per tag, in one nested class per
    /// registry. A constant holds the registry ID, the tag ID and the path, so it survives renames.
    /// </summary>
    /// <remarks>
    /// <b>Renames (policy C):</b> when a tag's constant name changes, the old name stays as an <c>[Obsolete]</c>
    /// alias of the new one, until <see cref="PruneAliases"/> finds no script using it. The previous generated file
    /// is the only record of old names, so it must be committed. <br/>
    /// <b>Deletes:</b> a deleted tag's constant and aliases disappear, so any use is a compile error. <br/>
    /// <b>Which:</b> every registry with code generation on (its Inspector toggle), as saved: staged tag edits are not
    /// included. <br/>
    /// <b>When:</b> saving tag edits writes it. Other changes only mark it as pending, and it's written when the
    /// Gameplay Tag Manager loses focus, the editor loses focus, before Play Mode, before a build and on quit. It's
    /// only written if its content changed.
    /// </remarks>
    [InitializeOnLoad]
    internal static class TagCodeGen
    {
        public const string DEFAULT_OUTPUT_PATH = TagRegistryService.ROOT_FOLDER + "/Generated/GameplayTags.g.cs";

        public const string ROOT_CLASS = "Tags";
        public const string MAIN_CLASS = "Main";
        public const string REGISTRY_FIELD = "RegistryId";

        private const string SESSION_PENDING = "LMK.GameplayTags.CodeGenPending";
        private const string ALIAS_MARKER = "alias of";
        private const int SHORT_ID_LENGTH = 8;

        private const string GAMEPLAY_TAG = "global::LMK.Runtime.GameplayTags.GameplayTag";
        private const string TAG_GUID = "global::LMK.Runtime.GameplayTags.TagGuid";

        /// <summary>
        /// Where the code is written. Only tests change it.
        /// </summary>
        internal static string OutputPath { get; set; } = DEFAULT_OUTPUT_PATH;

        private static readonly HashSet<string> KEYWORDS = new(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
            "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
            "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
            "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
            "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
            "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        };

        private const string HEX16 = "[0-9a-fA-F]{16}";
        // An optional namespace qualifier, e.g. "global::LMK.Runtime.GameplayTags.".
        private const string QUALIFIER = @"(?:[\w:.]+\.)?";

        private static readonly Regex REGISTRY_LINE = new(
            $@"\b{REGISTRY_FIELD}\s*=\s*new\s+{QUALIFIER}TagGuid\(\s*0x(?<hi>{HEX16})UL\s*,\s*0x(?<lo>{HEX16})UL\s*\)",
            RegexOptions.Compiled);

        private static readonly Regex CONSTANT_LINE = new(
            $@"\bGameplayTag\s+@?(?<name>\w+)\s*=\s*{QUALIFIER}GameplayTag\.FromId\(\s*{REGISTRY_FIELD}\s*,\s*new\s+{QUALIFIER}TagGuid\(\s*0x(?<hi>{HEX16})UL\s*,\s*0x(?<lo>{HEX16})UL\s*\)",
            RegexOptions.Compiled);

        private static readonly Regex ALIAS_LINE = new(
            $@"\bGameplayTag\s+@?(?<name>\w+)\s*=\s*@?\w+\s*;\s*//\s*{ALIAS_MARKER}\s+(?<id>[0-9a-fA-F]{{32}})",
            RegexOptions.Compiled);



        static TagCodeGen()
        {
            EditorApplication.playModeStateChanged += _State =>
            {
                if (_State == PlayModeStateChange.ExitingEditMode)
                    Flush();
            };
            EditorApplication.focusChanged += _Focused =>
            {
                if (!_Focused)
                    Flush();
            };
            EditorApplication.quitting += Flush;
        }



        #region Pending
        /// <summary>
        /// Whether a tag operation changed the registries since the code was last written.
        /// </summary>
        public static bool IsPending => SessionState.GetBool(SESSION_PENDING, false);

        /// <summary>
        /// Called after every registry write. The code is written by the next <see cref="Flush"/>.
        /// </summary>
        public static void MarkPending() => SessionState.SetBool(SESSION_PENDING, true);

        internal static void CancelPending() => SessionState.EraseBool(SESSION_PENDING);

        /// <summary>
        /// Write the code if tag operations changed the registries since it was last written.
        /// </summary>
        public static void Flush()
        {
            if (!IsPending) return;

            var result = Generate();
            if (!result.Success)
                Debug.LogWarning($"Gameplay tag code was not generated: {result.Error}");
        }
        #endregion



        #region Commands
        /// <summary>
        /// Generate the code from the loaded registries now. The file is only written if its content changed.
        /// </summary>
        public static TagOperationResult Generate()
        {
            var warnings = new List<string>();
            string source = GenerateSource(CollectRegistries(TagRegistryService.SavedSnapshot), ReadOutput(), CodeNamespace, warnings);

            var result = WriteIfChanged(source, out bool written);
            if (!result.Success) return result;

            CancelPending();
            foreach (string warning in warnings)
                Debug.LogWarning($"Gameplay tag code: {warning}");

            if (written)
                Debug.Log($"Gameplay tag code written to \"{OutputPath}\".");

            return result;
        }

        /// <summary>
        /// Remove the <c>[Obsolete]</c> aliases that no script in the project uses (policy C).
        /// </summary>
        /// <param name="_Pruned"> The removed aliases, as <c>Class.Name</c>. </param>
        public static TagOperationResult PruneAliases(out IReadOnlyList<string> _Pruned)
        {
            _Pruned = Array.Empty<string>();

            string previous = ReadOutput();
            if (previous == null) return TagOperationResult.Ok();

            var aliases = ParseGenerated(previous).Where(e => e.IsAlias).ToList();
            if (aliases.Count == 0) return TagOperationResult.Ok();

            var used = FindUsedNames(aliases.Select(e => e.Name), ProjectScripts());
            var drop = new HashSet<(TagGuid, string)>(aliases.Where(e => !used.Contains(e.Name)).Select(e => (e.RegistryId, e.Name)));
            if (drop.Count == 0) return TagOperationResult.Ok();

            var warnings = new List<string>();
            string source = GenerateSource(CollectRegistries(TagRegistryService.SavedSnapshot), previous, CodeNamespace, warnings, drop);

            var result = WriteIfChanged(source, out _);
            if (!result.Success) return result;

            CancelPending();

            // Report only what really went: an alias of a tag deleted since the last write was dropped anyway.
            var remaining = new HashSet<(TagGuid, string)>(ParseGenerated(source).Select(e => (e.RegistryId, e.Name)));
            _Pruned = aliases.Where(e => drop.Contains((e.RegistryId, e.Name)) && !remaining.Contains((e.RegistryId, e.Name)))
                             .Select(e => e.Name)
                             .ToList();

            return result;
        }

        private static string CodeNamespace => GameplayTagSettings.instance.CodeNamespace;

        /// <summary>
        /// The class a registry gets in the generated code, e.g. <c>Tags.Main</c>, or <see langword="null"/> if it
        /// isn't loaded. Computed as if every registry had code generation on.
        /// </summary>
        public static string ClassNameOf(TagGuid _RegistryId)
        {
            var registries = CollectRegistries(TagRegistryService.SavedSnapshot, true);
            var names = AssignClassNames(registries);

            for (int i = 0; i < registries.Count; i++)
                if (registries[i].Id.Equals(_RegistryId))
                    return $"{ROOT_CLASS}.{names[i]}";

            return null;
        }

        private static string ReadOutput()
            => File.Exists(OutputPath) ? File.ReadAllText(OutputPath) : null;

        private static TagOperationResult WriteIfChanged(string _Source, out bool _Written)
        {
            _Written = false;

            string existing = ReadOutput();
            if (existing != null && existing.Replace("\r", string.Empty) == _Source)
                return TagOperationResult.Ok();

            // Nothing to generate, and nothing generated before: don't create a file.
            if (existing == null && _Source == null)
                return TagOperationResult.Ok();

            if (!TagLockProvider.Current.TryAcquire(new[] { OutputPath }, out string lockError))
                return TagOperationResult.Fail($"The gameplay tag lock ({TagLockProvider.Current.Name}) is not available. {lockError}".TrimEnd());

            bool isAsset = OutputPath.StartsWith("Assets/", StringComparison.Ordinal);
            string folder = Path.GetDirectoryName(OutputPath)?.Replace('\\', '/');
            if (isAsset) TagOperations.EnsureFolder(folder);
            else if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            File.WriteAllText(OutputPath, _Source ?? string.Empty, new UTF8Encoding(false));
            _Written = true;

            if (isAsset)
                AssetDatabase.ImportAsset(OutputPath);

            return TagOperationResult.Ok();
        }

        /// <summary>
        /// Scripts that can use the generated code: everything under <c>Assets/</c> and in embedded packages.
        /// </summary>
        private static IEnumerable<string> ProjectScripts()
        {
            string output = Path.GetFullPath(OutputPath);

            foreach (string root in new[] { "Assets", "Packages" })
            {
                if (!Directory.Exists(root)) continue;

                foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                    if (!string.Equals(Path.GetFullPath(file), output, StringComparison.OrdinalIgnoreCase))
                        yield return file;
            }
        }
        #endregion



        #region Model
        /// <summary>
        /// A registry as code generation sees it.
        /// </summary>
        internal sealed class Registry
        {
            public TagGuid Id;
            public string Name;
            public bool IsMain;
            public readonly List<Tag> Tags = new();
        }

        internal readonly struct Tag
        {
            public readonly TagGuid Id;
            public readonly string Path;
            public readonly string Description;

            public Tag(TagGuid _Id, string _Path, string _Description = null)
            {
                Id = _Id;
                Path = _Path;
                Description = _Description ?? string.Empty;
            }
        }

        /// <summary>
        /// A member found in a generated file.
        /// </summary>
        internal readonly struct GeneratedMember
        {
            public readonly TagGuid RegistryId;
            public readonly string Name;
            public readonly TagGuid TagId;
            public readonly bool IsAlias;

            public GeneratedMember(TagGuid _RegistryId, string _Name, TagGuid _TagId, bool _IsAlias)
            {
                RegistryId = _RegistryId;
                Name = _Name;
                TagId = _TagId;
                IsAlias = _IsAlias;
            }
        }

        /// <summary>
        /// The registries that get a class: the main registry, and package and generated registries.
        /// Other main registries (e.g. copied in from another project) are skipped.
        /// </summary>
        /// <param name="_IncludeOff"> Include registries with code generation turned off. </param>
        internal static List<Registry> CollectRegistries(TagRegistrySnapshot _Snapshot, bool _IncludeOff = false)
        {
            var result = new List<Registry>();
            foreach (var registry in _Snapshot.Registries)
            {
                bool isMain = registry == _Snapshot.Main;
                if (!isMain && registry.Kind == TagRegistryKind.Main) continue;
                if (!_IncludeOff && !GameplayTagSettings.instance.IsCodeGenerated(registry.Id)) continue;

                var entry = new Registry { Id = registry.Id, Name = registry.Name, IsMain = isMain };
                foreach (var row in registry.Rows)
                {
                    string path = registry.GetPath(row.id);
                    if (path != null)
                        entry.Tags.Add(new Tag(row.id, path, row.description));
                }

                result.Add(entry);
            }

            return result;
        }
        #endregion



        #region Generation
        /// <summary>
        /// The source of the generated file.
        /// </summary>
        /// <param name="_PreviousSource"> The current generated file, read for the names of renamed tags. May be <see langword="null"/>. </param>
        /// <param name="_Namespace"> Namespace of the <c>Tags</c> class. Empty for the global namespace. </param>
        /// <param name="_Warnings"> Receives name clashes and dropped aliases. </param>
        /// <param name="_DropAliases"> Aliases (registry ID, name) to leave out: the prune step. </param>
        /// <returns> The source, or <see langword="null"/> when there is no registry. </returns>
        internal static string GenerateSource(
            IReadOnlyList<Registry> _Registries,
            string _PreviousSource,
            string _Namespace,
            ICollection<string> _Warnings = null,
            ISet<(TagGuid, string)> _DropAliases = null)
        {
            if (_Registries.Count == 0) return null;

            _Warnings ??= new List<string>();

            var previous = string.IsNullOrEmpty(_PreviousSource) ? new List<GeneratedMember>() : ParseGenerated(_PreviousSource);
            var classNames = AssignClassNames(_Registries);

            string ns = IsValidNamespace(_Namespace) ? _Namespace.Trim() : null;
            if (!string.IsNullOrWhiteSpace(_Namespace) && ns == null)
                _Warnings.Add($"\"{_Namespace}\" is not a valid namespace. The global namespace is used.");

            var code = new CodeWriter();
            code.Line("// <auto-generated>");
            code.Line("//     Generated by the Gameplay Tag Manager (com.lmk.gameplaytags) from the tag registries.");
            code.Line("//     Don't edit it: tag operations regenerate it. Commit it with the registries, because renamed");
            code.Line("//     tags keep their old names as [Obsolete] aliases only through this file.");
            code.Line("// </auto-generated>");
            code.Line();

            if (ns != null)
            {
                code.Line($"namespace {ns}");
                code.Open();
            }

            code.Line("/// <summary>");
            code.Line("/// Gameplay tag constants, one nested class per registry. A constant holds the tag's IDs, so it survives");
            code.Line("/// renames. Deleting a tag removes its constant.");
            code.Line("/// </summary>");
            code.Line($"public static partial class {ROOT_CLASS}");
            code.Open();

            for (int i = 0; i < _Registries.Count; i++)
            {
                if (i > 0) code.Line();
                WriteRegistry(code, _Registries[i], classNames[i], previous, _DropAliases, _Warnings);
            }

            code.Close();
            if (ns != null) code.Close();

            return code.ToString();
        }

        private static void WriteRegistry(
            CodeWriter _Code,
            Registry _Registry,
            string _ClassName,
            List<GeneratedMember> _Previous,
            ISet<(TagGuid, string)> _DropAliases,
            ICollection<string> _Warnings)
        {
            var tags = new List<Tag>(_Registry.Tags);
            tags.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

            var names = AssignMemberNames(tags, _ClassName, _Warnings);
            var nameOf = new Dictionary<TagGuid, string>();
            var pathOf = new Dictionary<TagGuid, string>();
            for (int i = 0; i < tags.Count; i++)
            {
                nameOf[tags[i].Id] = names[i];
                pathOf[tags[i].Id] = tags[i].Path;
            }

            var taken = new HashSet<string>(names, StringComparer.Ordinal) { REGISTRY_FIELD, _ClassName };
            var aliases = new SortedDictionary<string, TagGuid>(StringComparer.Ordinal);

            foreach (var member in _Previous)
            {
                if (!member.RegistryId.Equals(_Registry.Id)) continue;

                if (taken.Contains(member.Name))
                {
                    // The name now belongs to another tag, e.g. renamed "A" to "B", then added a new "A".
                    if (nameOf.TryGetValue(member.TagId, out string current) && current != member.Name
                        && names.Contains(member.Name))
                    {
                        _Warnings.Add($"{ROOT_CLASS}.{_ClassName}.{member.Name} now refers to another tag. " +
                                      $"Code that used it for \"{pathOf[member.TagId]}\" should use {ROOT_CLASS}.{_ClassName}.{current}.");
                    }

                    continue;
                }

                // Deleted tags lose every name. Pruned aliases go too.
                if (!nameOf.ContainsKey(member.TagId)) continue;
                if (_DropAliases != null && _DropAliases.Contains((_Registry.Id, member.Name))) continue;

                aliases[member.Name] = member.TagId;
            }

            _Code.Line("/// <summary>");
            _Code.Line($"/// Tags of the \"{XmlText(_Registry.Name)}\" registry.");
            _Code.Line("/// </summary>");
            _Code.Line($"public static partial class {_ClassName}");
            _Code.Open();

            _Code.Line($"/// <summary> ID of the \"{XmlText(_Registry.Name)}\" registry. </summary>");
            _Code.Line($"public static readonly {TAG_GUID} {REGISTRY_FIELD} = {GuidLiteral(_Registry.Id)};");

            for (int i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                string summary = string.IsNullOrWhiteSpace(tag.Description)
                    ? $"<c>{tag.Path}</c>"
                    : $"<c>{tag.Path}</c>: {XmlText(tag.Description)}";

                _Code.Line();
                _Code.Line($"/// <summary> {summary} </summary>");
                _Code.Line($"public static readonly {GAMEPLAY_TAG} {Escape(names[i])} = " +
                           $"{GAMEPLAY_TAG}.FromId({REGISTRY_FIELD}, {GuidLiteral(tag.Id)}, \"{tag.Path}\");");
            }

            if (aliases.Count > 0)
            {
                _Code.Line();
                _Code.Line("// Old names of renamed tags. Tools > GASU > Gameplay Tags > Prune Unused Code Aliases removes");
                _Code.Line("// the ones no script uses.");

                foreach (var (alias, id) in aliases)
                {
                    string target = nameOf[id];

                    _Code.Line();
                    _Code.Line($"[global::System.Obsolete(\"Renamed to {ROOT_CLASS}.{_ClassName}.{target} (\\\"{pathOf[id]}\\\").\")]");
                    _Code.Line($"public static readonly {GAMEPLAY_TAG} {Escape(alias)} = {Escape(target)}; // {ALIAS_MARKER} {id}");
                }
            }

            _Code.Close();
        }
        #endregion



        #region Names
        /// <summary>
        /// Class name per registry: <c>Main</c> for the main registry, the registry name for the others.
        /// Clashes get the start of the registry ID appended.
        /// </summary>
        internal static string[] AssignClassNames(IReadOnlyList<Registry> _Registries)
        {
            var result = new string[_Registries.Count];
            var bases = new string[_Registries.Count];

            for (int i = 0; i < _Registries.Count; i++)
                bases[i] = _Registries[i].IsMain ? MAIN_CLASS : ToIdentifier(_Registries[i].Name, "Registry");

            var counts = bases.GroupBy(e => e).ToDictionary(e => e.Key, e => e.Count());
            var used = new HashSet<string>(StringComparer.Ordinal) { ROOT_CLASS };

            // The main registry keeps "Main" even if a package registry is also called "Main".
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < _Registries.Count; i++)
                {
                    if (_Registries[i].IsMain != (pass == 0)) continue;

                    string name = bases[i];
                    bool clash = KEYWORDS.Contains(name) || used.Contains(name) || (counts[name] > 1 && !_Registries[i].IsMain);
                    if (clash)
                        name = $"{name}_{Short(_Registries[i].Id)}";

                    while (!used.Add(name))
                        name += "_";

                    result[i] = name;
                }
            }

            return result;
        }

        /// <summary>
        /// Member name per tag: the path with <c>.</c> replaced by <c>_</c>. Paths that map to the same name
        /// (<c>A.B_C</c> and <c>A_B.C</c>), or to a reserved name, get the start of the tag ID appended, so a new
        /// tag never takes over another tag's name silently.
        /// </summary>
        /// <param name="_Tags"> Tags sorted by path. </param>
        internal static List<string> AssignMemberNames(IReadOnlyList<Tag> _Tags, string _ClassName, ICollection<string> _Warnings)
        {
            var bases = _Tags.Select(e => ToIdentifier(e.Path.Replace(TagPath.SEPARATOR, '_'), "Tag")).ToList();
            var counts = bases.GroupBy(e => e).ToDictionary(e => e.Key, e => e.Count());
            var reserved = new HashSet<string>(StringComparer.Ordinal) { REGISTRY_FIELD, _ClassName };

            var result = new List<string>(_Tags.Count);
            var used = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < _Tags.Count; i++)
            {
                string name = bases[i];
                if (counts[name] > 1 || reserved.Contains(name))
                {
                    name = $"{bases[i]}_{Short(_Tags[i].Id)}";
                    _Warnings?.Add($"\"{_Tags[i].Path}\" is generated as {ROOT_CLASS}.{_ClassName}.{name}, because {bases[i]} " +
                                   (reserved.Contains(bases[i]) ? "is reserved." : "is the name of several tags."));
                }

                // Two IDs sharing their first digits: use the whole ID.
                if (used.Contains(name))
                    name = $"{bases[i]}_{_Tags[i].Id}";

                used.Add(name);
                result.Add(name);
            }

            return result;
        }

        /// <summary>
        /// A C# identifier made of letters, digits and <c>_</c>. Keywords are returned as they are: use
        /// <see cref="Escape"/> to write them.
        /// </summary>
        private static string ToIdentifier(string _Text, string _Fallback)
        {
            var builder = new StringBuilder();
            foreach (char c in _Text ?? string.Empty)
                builder.Append(TagPath.IsValidSegmentChar(c) ? c : '_');

            if (builder.Length == 0) return _Fallback;
            if (char.IsDigit(builder[0])) builder.Insert(0, '_');

            return builder.ToString();
        }

        private static string Escape(string _Name) => KEYWORDS.Contains(_Name) ? "@" + _Name : _Name;

        private static string Short(TagGuid _Id) => _Id.ToString()[..SHORT_ID_LENGTH];

        private static bool IsValidNamespace(string _Namespace)
        {
            if (string.IsNullOrWhiteSpace(_Namespace)) return false;

            foreach (string part in _Namespace.Trim().Split('.'))
            {
                if (part.Length == 0 || char.IsDigit(part[0]) || KEYWORDS.Contains(part)) return false;
                if (!part.All(TagPath.IsValidSegmentChar)) return false;
            }

            return true;
        }
        #endregion



        #region Parsing and usage
        /// <summary>
        /// The constants and aliases of a generated file, with the registry each belongs to.
        /// </summary>
        internal static List<GeneratedMember> ParseGenerated(string _Source)
        {
            var result = new List<GeneratedMember>();
            var registry = TagGuid.Zero;

            foreach (string line in (_Source ?? string.Empty).Split('\n'))
            {
                var match = REGISTRY_LINE.Match(line);
                if (match.Success)
                {
                    registry = FromHex(match.Groups["hi"].Value, match.Groups["lo"].Value);
                    continue;
                }

                if (registry.IsZero) continue;

                match = CONSTANT_LINE.Match(line);
                if (match.Success)
                {
                    result.Add(new GeneratedMember(registry, match.Groups["name"].Value,
                        FromHex(match.Groups["hi"].Value, match.Groups["lo"].Value), false));
                    continue;
                }

                match = ALIAS_LINE.Match(line);
                if (match.Success && TagGuid.TryParse(match.Groups["id"].Value, out var id))
                    result.Add(new GeneratedMember(registry, match.Groups["name"].Value, id, true));
            }

            return result;
        }

        /// <summary>
        /// Which of <paramref name="_Names"/> appear as whole words in the given scripts. Comments count as uses,
        /// which only ever keeps an alias longer.
        /// </summary>
        internal static HashSet<string> FindUsedNames(IEnumerable<string> _Names, IEnumerable<string> _Files)
        {
            var names = new HashSet<string>(_Names, StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            if (names.Count == 0) return used;

            var pattern = new Regex($@"\b(?:{string.Join("|", names.Select(Regex.Escape))})\b", RegexOptions.CultureInvariant);

            foreach (string file in _Files)
            {
                string text;
                try { text = File.ReadAllText(file); }
                catch (IOException) { continue; }

                foreach (Match match in pattern.Matches(text))
                    used.Add(match.Value);

                if (used.Count == names.Count) break;
            }

            return used;
        }

        private static TagGuid FromHex(string _Hi, string _Lo)
            => TagGuid.Parse(_Hi + _Lo);

        private static string GuidLiteral(TagGuid _Id)
            => $"new {TAG_GUID}(0x{_Id.Hi:x16}UL, 0x{_Id.Lo:x16}UL)";

        private static string XmlText(string _Text)
            => (_Text ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        #endregion



        /// <summary>
        /// Indented source with LF line endings.
        /// </summary>
        private sealed class CodeWriter
        {
            private readonly StringBuilder builder = new();
            private int indent;

            public void Line(string _Text = "")
            {
                if (_Text.Length > 0)
                    builder.Append(' ', indent * 4).Append(_Text);

                builder.Append('\n');
            }

            public void Open()
            {
                Line("{");
                indent++;
            }

            public void Close()
            {
                indent--;
                Line("}");
            }

            public override string ToString() => builder.ToString();
        }
    }
}
