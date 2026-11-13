using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using UnityEditor;
using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Generates each kind's constants file, e.g. <c>GameplayTags.g.cs</c>: one constant per row, in one nested class
    /// per registry. A constant holds the registry ID, the row ID and the name, so it survives renames. The look of
    /// the file is the kind's <see cref="RegistryCodeStyle"/>.
    /// </summary>
    /// <remarks>
    /// <b>Renames (policy C):</b> when a row's constant name changes, the old name stays as an <c>[Obsolete]</c> alias
    /// of the new one, until <see cref="PruneAliases"/> finds no script using it. The previous generated file is the
    /// only record of old names, so it must be committed. <br/>
    /// <b>Deletes:</b> a deleted row's constant and aliases disappear, so any use is a compile error. <br/>
    /// <b>Which:</b> every registry with code generation on, as saved: staged edits are not included. <br/>
    /// <b>When:</b> saving a kind's edits writes its file. Other changes only mark it as pending, and it's written when
    /// the editor loses focus, before Play Mode, before a build and on quit. It's only written if its content changed,
    /// and always with LF line endings, the same content giving the same text.
    /// </remarks>
    [InitializeOnLoad]
    public static class RegistryCodeGen
    {
        public const string MAIN_CLASS = "Main";
        public const string REGISTRY_FIELD = "RegistryId";

        private const string SESSION_PENDING = "LMK.Registry.CodeGenPending:";
        private const string ALIAS_MARKER = "alias of";
        private const int SHORT_ID_LENGTH = 8;

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
        // A type, with an optional namespace qualifier, e.g. "global::LMK.Runtime.Registry.RegistryGuid".
        private const string TYPE = @"[\w:.]+";

        private static readonly Regex REGISTRY_LINE = new(
            $@"\b{REGISTRY_FIELD}\s*=\s*new\s+{TYPE}\(\s*0x(?<hi>{HEX16})UL\s*,\s*0x(?<lo>{HEX16})UL\s*\)",
            RegexOptions.Compiled);

        private static readonly Regex CONSTANT_LINE = new(
            $@"\bpublic\s+static\s+readonly\s+{TYPE}\s+@?(?<name>\w+)\s*=.*?\b{REGISTRY_FIELD}\s*,\s*new\s+{TYPE}\(\s*0x(?<hi>{HEX16})UL\s*,\s*0x(?<lo>{HEX16})UL",
            RegexOptions.Compiled);

        private static readonly Regex ALIAS_LINE = new(
            $@"\bpublic\s+static\s+readonly\s+{TYPE}\s+@?(?<name>\w+)\s*=\s*@?\w+\s*;\s*//\s*{ALIAS_MARKER}\s+(?<id>[0-9a-fA-F]{{32}})",
            RegexOptions.Compiled);



        static RegistryCodeGen()
        {
            EditorApplication.playModeStateChanged += _State =>
            {
                if (_State == PlayModeStateChange.ExitingEditMode)
                    FlushAll();
            };
            EditorApplication.focusChanged += _Focused =>
            {
                if (!_Focused)
                    FlushAll();
            };
            EditorApplication.quitting += FlushAll;
        }



        #region Pending
        /// <summary>
        /// Whether a kind's registries changed since its code was last written.
        /// </summary>
        public static bool IsPending(RegistryKind _Kind) => SessionState.GetBool(SESSION_PENDING + _Kind.Id, false);

        /// <summary>
        /// Called after every registry write of a kind. The code is written by the next <see cref="Flush"/>.
        /// </summary>
        public static void MarkPending(RegistryKind _Kind)
        {
            if (_Kind.CodeStyle != null)
                SessionState.SetBool(SESSION_PENDING + _Kind.Id, true);
        }

        /// <summary>
        /// Forget that a kind's code is pending, without writing it.
        /// </summary>
        public static void CancelPending(RegistryKind _Kind) => SessionState.EraseBool(SESSION_PENDING + _Kind.Id);

        /// <summary>
        /// Write a kind's code if its registries changed since it was last written.
        /// </summary>
        public static void Flush(RegistryKind _Kind)
        {
            if (!IsPending(_Kind)) return;

            var result = Generate(_Kind);
            if (!result.Success)
                Debug.LogWarning($"{_Kind.DisplayName} code was not generated: {result.Error}");
        }

        public static void FlushAll()
        {
            foreach (var kind in RegistryKinds.All)
                Flush(kind);
        }
        #endregion



        #region Commands
        /// <summary>
        /// Generate a kind's code from its saved registries now. The file is only written if its content changed.
        /// A kind without a <see cref="RegistryCodeStyle"/> generates nothing.
        /// </summary>
        public static RegistryResult Generate(RegistryKind _Kind)
        {
            var style = _Kind.CodeStyle;
            if (style == null) return RegistryResult.Ok();

            var warnings = new List<string>();
            string source = GenerateSource(style, CollectRegistries(_Kind), ReadOutput(_Kind), Namespace(_Kind), warnings);

            var result = WriteIfChanged(_Kind, source, out bool written);
            if (!result.Success) return result;

            CancelPending(_Kind);
            foreach (string warning in warnings)
                Debug.LogWarning($"{_Kind.DisplayName} code: {warning}");

            if (written)
                Debug.Log($"{_Kind.DisplayName} code written to \"{_Kind.CodeOutputPath}\".");

            return result;
        }

        /// <summary>
        /// Remove the <c>[Obsolete]</c> aliases of a kind that no script in the project uses (policy C).
        /// </summary>
        /// <param name="_Pruned"> The removed aliases. </param>
        public static RegistryResult PruneAliases(RegistryKind _Kind, out IReadOnlyList<string> _Pruned)
        {
            _Pruned = Array.Empty<string>();

            var style = _Kind.CodeStyle;
            string previous = ReadOutput(_Kind);
            if (style == null || previous == null) return RegistryResult.Ok();

            var aliases = ParseGenerated(previous).Where(e => e.IsAlias).ToList();
            if (aliases.Count == 0) return RegistryResult.Ok();

            var used = FindUsedNames(aliases.Select(e => e.Name), ProjectScripts(_Kind));
            var drop = new HashSet<(RegistryGuid, string)>(aliases.Where(e => !used.Contains(e.Name)).Select(e => (e.RegistryId, e.Name)));
            if (drop.Count == 0) return RegistryResult.Ok();

            string source = GenerateSource(style, CollectRegistries(_Kind), previous, Namespace(_Kind), new List<string>(), drop);

            var result = WriteIfChanged(_Kind, source, out _);
            if (!result.Success) return result;

            CancelPending(_Kind);

            // Report only what really went: an alias of a row deleted since the last write was dropped anyway.
            var remaining = new HashSet<(RegistryGuid, string)>(ParseGenerated(source).Select(e => (e.RegistryId, e.Name)));
            _Pruned = aliases.Where(e => drop.Contains((e.RegistryId, e.Name)) && !remaining.Contains((e.RegistryId, e.Name)))
                             .Select(e => e.Name)
                             .ToList();

            return result;
        }

        /// <summary>
        /// The class a registry gets in a kind's generated code, e.g. <c>Tags.Main</c>, or <see langword="null"/> if
        /// it isn't loaded. Computed as if every registry had code generation on.
        /// </summary>
        public static string ClassNameOf(RegistryKind _Kind, RegistryGuid _RegistryId)
        {
            var style = _Kind.CodeStyle;
            if (style == null) return null;

            var registries = _Kind.CollectCodeRegistries();
            var names = AssignClassNames(style, registries);

            for (int i = 0; i < registries.Count; i++)
                if (registries[i].Id.Equals(_RegistryId))
                    return $"{style.RootClass}.{names[i]}";

            return null;
        }

        private static List<Registry> CollectRegistries(RegistryKind _Kind)
            => _Kind.CollectCodeRegistries().Where(e => RegistrySettings.instance.IsCodeGenerated(_Kind.Id, e.Id)).ToList();

        private static string Namespace(RegistryKind _Kind) => RegistrySettings.instance.GetCodeNamespace(_Kind);

        private static string ReadOutput(RegistryKind _Kind)
            => File.Exists(_Kind.CodeOutputPath) ? File.ReadAllText(_Kind.CodeOutputPath) : null;

        private static RegistryResult WriteIfChanged(RegistryKind _Kind, string _Source, out bool _Written)
        {
            _Written = false;
            string path = _Kind.CodeOutputPath;
            string assemblyPath = _Kind.CodeAssemblyPath;
            string assembly = assemblyPath != null ? AssemblyDefinition(_Kind) : null;

            string existing = ReadOutput(_Kind);
            bool sourceChanged = !(existing != null && existing.Replace("\r", string.Empty) == _Source) && !(existing == null && _Source == null);
            bool assemblyChanged = assemblyPath != null && _Source != null
                && (!File.Exists(assemblyPath) || File.ReadAllText(assemblyPath).Replace("\r", string.Empty) != assembly);

            if (!sourceChanged && !assemblyChanged) return RegistryResult.Ok();

            var paths = new List<string> { path };
            if (assemblyPath != null) paths.Add(assemblyPath);
            if (!RegistryLockProvider.Current.TryAcquire(paths, out string lockError))
                return RegistryResult.Fail(RegistryLockProvider.RefusedMessage(lockError));

            bool isAsset = path.StartsWith("Assets/", StringComparison.Ordinal);
            string folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (isAsset) RegistryAssets.EnsureFolder(folder);
            else if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            if (sourceChanged)
                File.WriteAllText(path, _Source ?? string.Empty, new UTF8Encoding(false));
            if (assemblyChanged)
                File.WriteAllText(assemblyPath, assembly, new UTF8Encoding(false));

            _Written = true;

            if (isAsset)
            {
                if (assemblyChanged) AssetDatabase.ImportAsset(assemblyPath);
                if (sourceChanged) AssetDatabase.ImportAsset(path);
            }

            return RegistryResult.Ok();
        }

        /// <summary>
        /// The assembly definition of a kind's generated code, when the project gives it its own assembly.
        /// </summary>
        private static string AssemblyDefinition(RegistryKind _Kind)
        {
            var references = _Kind.CodeStyle.AssemblyReferences.Select(e => $"        \"{e}\"");

            return "{\n" +
                   $"    \"name\": \"{RegistrySettings.instance.GetCodeAssembly(_Kind)}\",\n" +
                   "    \"rootNamespace\": \"\",\n" +
                   "    \"references\": [\n" +
                   string.Join(",\n", references) + (_Kind.CodeStyle.AssemblyReferences.Count > 0 ? "\n" : string.Empty) +
                   "    ],\n" +
                   "    \"autoReferenced\": true,\n" +
                   "    \"noEngineReferences\": false\n" +
                   "}\n";
        }

        /// <summary>
        /// Scripts that can use the generated code: everything under <c>Assets/</c> and in embedded packages.
        /// </summary>
        private static IEnumerable<string> ProjectScripts(RegistryKind _Kind)
        {
            string output = Path.GetFullPath(_Kind.CodeOutputPath);

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
        public sealed class Registry
        {
            public RegistryGuid Id;
            public string Name;
            public bool IsMain;
            public readonly List<Row> Rows = new();
        }

        public readonly struct Row
        {
            public readonly RegistryGuid Id;
            public readonly string Name;
            public readonly string Description;

            public Row(RegistryGuid _Id, string _Name, string _Description = null)
            {
                Id = _Id;
                Name = _Name;
                Description = _Description ?? string.Empty;
            }
        }

        /// <summary>
        /// A member found in a generated file.
        /// </summary>
        public readonly struct GeneratedMember
        {
            public readonly RegistryGuid RegistryId;
            public readonly string Name;
            public readonly RegistryGuid RowId;
            public readonly bool IsAlias;

            public GeneratedMember(RegistryGuid _RegistryId, string _Name, RegistryGuid _RowId, bool _IsAlias)
            {
                RegistryId = _RegistryId;
                Name = _Name;
                RowId = _RowId;
                IsAlias = _IsAlias;
            }
        }
        #endregion



        #region Generation
        /// <summary>
        /// The source of a generated file.
        /// </summary>
        /// <param name="_PreviousSource"> The current generated file, read for the names of renamed rows. May be <see langword="null"/>. </param>
        /// <param name="_Namespace"> Namespace of the root class. Empty for the global namespace. </param>
        /// <param name="_Warnings"> Receives name clashes and dropped aliases. </param>
        /// <param name="_DropAliases"> Aliases (registry ID, name) to leave out: the prune step. </param>
        /// <returns> The source, or <see langword="null"/> when there is no registry. </returns>
        public static string GenerateSource(
            RegistryCodeStyle _Style,
            IReadOnlyList<Registry> _Registries,
            string _PreviousSource,
            string _Namespace,
            ICollection<string> _Warnings = null,
            ISet<(RegistryGuid, string)> _DropAliases = null)
        {
            if (_Registries.Count == 0) return null;

            _Warnings ??= new List<string>();

            var previous = string.IsNullOrEmpty(_PreviousSource) ? new List<GeneratedMember>() : ParseGenerated(_PreviousSource);
            var classNames = AssignClassNames(_Style, _Registries);

            string ns = IsValidNamespace(_Namespace) ? _Namespace.Trim() : null;
            if (!string.IsNullOrWhiteSpace(_Namespace) && ns == null)
                _Warnings.Add($"\"{_Namespace}\" is not a valid namespace. The global namespace is used.");

            var code = new CodeWriter();
            code.Line("// <auto-generated>");
            foreach (string line in _Style.Header)
                code.Line("//     " + line);
            code.Line("// </auto-generated>");
            code.Line();

            if (ns != null)
            {
                code.Line($"namespace {ns}");
                code.Open();
            }

            if (!string.IsNullOrEmpty(_Style.RootSummary))
            {
                code.Line("/// <summary>");
                foreach (string line in _Style.RootSummary.Split('\n'))
                    code.Line("/// " + line);
                code.Line("/// </summary>");
            }

            code.Line($"public static partial class {_Style.RootClass}");
            code.Open();

            for (int i = 0; i < _Registries.Count; i++)
            {
                if (i > 0) code.Line();
                WriteRegistry(code, _Style, _Registries[i], classNames[i], previous, _DropAliases, _Warnings);
            }

            code.Close();
            if (ns != null) code.Close();

            return code.ToString();
        }

        private static void WriteRegistry(
            CodeWriter _Code,
            RegistryCodeStyle _Style,
            Registry _Registry,
            string _ClassName,
            List<GeneratedMember> _Previous,
            ISet<(RegistryGuid, string)> _DropAliases,
            ICollection<string> _Warnings)
        {
            var rows = new List<Row>(_Registry.Rows);
            rows.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            var names = AssignMemberNames(_Style, rows, _ClassName, _Warnings);
            var nameOf = new Dictionary<RegistryGuid, string>();
            var rowNameOf = new Dictionary<RegistryGuid, string>();
            for (int i = 0; i < rows.Count; i++)
            {
                nameOf[rows[i].Id] = names[i];
                rowNameOf[rows[i].Id] = rows[i].Name;
            }

            var taken = new HashSet<string>(names, StringComparer.Ordinal) { REGISTRY_FIELD, _ClassName };
            var aliases = new SortedDictionary<string, RegistryGuid>(StringComparer.Ordinal);

            foreach (var member in _Previous)
            {
                if (!member.RegistryId.Equals(_Registry.Id)) continue;

                if (taken.Contains(member.Name))
                {
                    // The name now belongs to another row, e.g. renamed "A" to "B", then added a new "A".
                    if (nameOf.TryGetValue(member.RowId, out string current) && current != member.Name && names.Contains(member.Name))
                    {
                        _Warnings.Add($"{_Style.RootClass}.{_ClassName}.{member.Name} now refers to another row. " +
                                      $"Code that used it for \"{rowNameOf[member.RowId]}\" should use {_Style.RootClass}.{_ClassName}.{current}.");
                    }

                    continue;
                }

                // Deleted rows lose every name. Pruned aliases go too.
                if (!nameOf.ContainsKey(member.RowId)) continue;
                if (_DropAliases != null && _DropAliases.Contains((_Registry.Id, member.Name))) continue;

                aliases[member.Name] = member.RowId;
            }

            _Code.Line("/// <summary>");
            _Code.Line($"/// {_Style.RegistrySummaryNoun} of the \"{XmlText(_Registry.Name)}\" registry.");
            _Code.Line("/// </summary>");
            _Code.Line($"public static partial class {_ClassName}");
            _Code.Open();

            _Code.Line($"/// <summary> ID of the \"{XmlText(_Registry.Name)}\" registry. </summary>");
            _Code.Line($"public static readonly {_Style.GuidType} {REGISTRY_FIELD} = {GuidLiteral(_Style, _Registry.Id)};");

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                string summary = string.IsNullOrWhiteSpace(row.Description)
                    ? $"<c>{XmlText(row.Name)}</c>"
                    : $"<c>{XmlText(row.Name)}</c>: {XmlText(row.Description)}";

                _Code.Line();
                _Code.Line($"/// <summary> {summary} </summary>");
                _Code.Line($"public static readonly {_Style.MemberType} {Escape(names[i])} = " +
                           $"{_Style.Initializer(REGISTRY_FIELD, GuidLiteral(_Style, row.Id), StringLiteral(row.Name))};");
            }

            if (aliases.Count > 0)
            {
                _Code.Line();
                foreach (string line in _Style.AliasComment)
                    _Code.Line("// " + line);

                foreach (var (alias, id) in aliases)
                {
                    string target = nameOf[id];

                    _Code.Line();
                    _Code.Line($"[global::System.Obsolete(\"Renamed to {_Style.RootClass}.{_ClassName}.{target} (\\\"{rowNameOf[id]}\\\").\")]");
                    _Code.Line($"public static readonly {_Style.MemberType} {Escape(alias)} = {Escape(target)}; // {ALIAS_MARKER} {id}");
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
        internal static string[] AssignClassNames(RegistryCodeStyle _Style, IReadOnlyList<Registry> _Registries)
        {
            var result = new string[_Registries.Count];
            var bases = new string[_Registries.Count];

            for (int i = 0; i < _Registries.Count; i++)
                bases[i] = _Registries[i].IsMain ? MAIN_CLASS : ToIdentifier(_Registries[i].Name, "Registry");

            var counts = bases.GroupBy(e => e).ToDictionary(e => e.Key, e => e.Count());
            var used = new HashSet<string>(StringComparer.Ordinal) { _Style.RootClass };

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
        /// Member name per row: the style's member name of the row name. Names that map to the same member, or to a
        /// reserved name, get the start of the row ID appended, so a new row never takes over another row's name
        /// silently.
        /// </summary>
        /// <param name="_Rows"> Rows sorted by name. </param>
        internal static List<string> AssignMemberNames(RegistryCodeStyle _Style, IReadOnlyList<Row> _Rows, string _ClassName, ICollection<string> _Warnings)
        {
            var bases = _Rows.Select(e => ToIdentifier(_Style.MemberName(e.Name), "Row")).ToList();
            var counts = bases.GroupBy(e => e).ToDictionary(e => e.Key, e => e.Count());
            var reserved = new HashSet<string>(StringComparer.Ordinal) { REGISTRY_FIELD, _ClassName };

            var result = new List<string>(_Rows.Count);
            var used = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < _Rows.Count; i++)
            {
                string name = bases[i];
                if (counts[name] > 1 || reserved.Contains(name))
                {
                    name = $"{bases[i]}_{Short(_Rows[i].Id)}";
                    _Warnings?.Add($"\"{_Rows[i].Name}\" is generated as {_Style.RootClass}.{_ClassName}.{name}, because {bases[i]} " +
                                   (reserved.Contains(bases[i]) ? "is reserved." : "is the name of several rows."));
                }

                // Two IDs sharing their first digits: use the whole ID.
                if (used.Contains(name))
                    name = $"{bases[i]}_{_Rows[i].Id}";

                used.Add(name);
                result.Add(name);
            }

            return result;
        }

        private static bool IsIdentifierChar(char _Char)
            => (_Char >= 'A' && _Char <= 'Z') || (_Char >= 'a' && _Char <= 'z') || (_Char >= '0' && _Char <= '9') || _Char == '_';

        /// <summary>
        /// A C# identifier made of letters, digits and <c>_</c>. Keywords are returned as they are: use
        /// <see cref="Escape"/> to write them.
        /// </summary>
        private static string ToIdentifier(string _Text, string _Fallback)
        {
            var builder = new StringBuilder();
            foreach (char c in _Text ?? string.Empty)
                builder.Append(IsIdentifierChar(c) ? c : '_');

            if (builder.Length == 0) return _Fallback;
            if (char.IsDigit(builder[0])) builder.Insert(0, '_');

            return builder.ToString();
        }

        private static string Escape(string _Name) => KEYWORDS.Contains(_Name) ? "@" + _Name : _Name;

        private static string Short(RegistryGuid _Id) => _Id.ToString()[..SHORT_ID_LENGTH];

        private static bool IsValidNamespace(string _Namespace)
        {
            if (string.IsNullOrWhiteSpace(_Namespace)) return false;

            foreach (string part in _Namespace.Trim().Split('.'))
            {
                if (part.Length == 0 || char.IsDigit(part[0]) || KEYWORDS.Contains(part)) return false;
                if (!part.All(IsIdentifierChar)) return false;
            }

            return true;
        }
        #endregion



        #region Parsing and usage
        /// <summary>
        /// The constants and aliases of a generated file, with the registry each belongs to. Any constant and GUID
        /// types are recognized, so files written by earlier versions keep their aliases.
        /// </summary>
        public static List<GeneratedMember> ParseGenerated(string _Source)
        {
            var result = new List<GeneratedMember>();
            var registry = RegistryGuid.Zero;

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
                if (match.Success && RegistryGuid.TryParse(match.Groups["id"].Value, out var id))
                    result.Add(new GeneratedMember(registry, match.Groups["name"].Value, id, true));
            }

            return result;
        }

        /// <summary>
        /// Which of <paramref name="_Names"/> appear as whole words in the given scripts. Comments count as uses,
        /// which only ever keeps an alias longer.
        /// </summary>
        public static HashSet<string> FindUsedNames(IEnumerable<string> _Names, IEnumerable<string> _Files)
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

        private static RegistryGuid FromHex(string _Hi, string _Lo) => RegistryGuid.Parse(_Hi + _Lo);

        private static string GuidLiteral(RegistryCodeStyle _Style, RegistryGuid _Id)
            => $"new {_Style.GuidType}(0x{_Id.Hi:x16}UL, 0x{_Id.Lo:x16}UL)";

        private static string StringLiteral(string _Text)
            => "\"" + (_Text ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

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
