using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using LMK.Runtime.Registry;

using Object = UnityEngine.Object;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Reads the references of every kind saved in an asset file, without changing it. Used by the reference
    /// index, registry generation for packages, pack validation and the mapping of imported assets.
    /// </summary>
    /// <remarks>
    /// Text-serialized files are read as text, which is fast. Every kind's reference has the same serialized
    /// layout, so a reference read as text gets its kind from its registry: the kind that knows that registry
    /// (or, for a reference without registry, that row). With a single kind registered, every reference is that
    /// kind's. When a reference can't be placed that way, the file is loaded and walked instead, which gives each
    /// reference its exact kind. Binary files are always loaded and walked. A reference is assumed to hold a
    /// registry ID of its own kind, which every editor writes; one holding another kind's registry ID (hand-edited
    /// data) is read as that other kind's when read as text. <br/>
    /// Values a prefab instance inherits from its prefab are not returned: the prefab holds them. A prefab instance
    /// override that changed only the row ID has no registry (see <see cref="RegistryReference"/>).
    /// </remarks>
    public static class RegistryAssetScanner
    {
        /// <summary>
        /// File types that can hold serialized references.
        /// </summary>
        private static readonly HashSet<string> EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
        {
            ".unity", ".prefab", ".asset", ".playable", ".controller",
        };

        // A serialized reference in block or flow style: its registry ID, its row ID, then its name.
        private static readonly Regex REFERENCE_PATTERN = new(
            @"(?<![\w.])_registry:\s*\{?\s*_hi:\s*(\d+)\s*,?\s*_lo:\s*(\d+)\s*\}?\s*_id:\s*\{?\s*_hi:\s*(\d+)\s*,?\s*_lo:\s*(\d+)(?:\s*\})?(?:\s*_lastKnownName:[ \t]*([^\r\n]*))?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // One prefab instance property modification: target, property path and value.
        private static readonly Regex MODIFICATION_PATTERN = new(
            @"-\s*target:\s*\{([^}]*)\}\s*propertyPath:\s*(\S+)\s*value:[ \t]*([^\r\n]*)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private const string NAME_SUFFIX = "._lastKnownName";
        private static readonly string[] ID_SUFFIXES = { "._registry._hi", "._registry._lo", "._id._hi", "._id._lo" };



        /// <summary>
        /// Whether a file of this type can hold serialized references.
        /// </summary>
        public static bool CanHoldReferences(string _Path)
            => !string.IsNullOrEmpty(_Path) && EXTENSIONS.Contains(Path.GetExtension(_Path));

        /// <summary>
        /// Read the references an asset file holds, None excluded, each with its kind.
        /// </summary>
        /// <returns> Whether the asset could be read completely. </returns>
        public static bool Scan(string _Path, List<RegistryReference> _Result)
        {
            var kinds = RegistryKinds.All;
            if (kinds.Count == 0) return true;

            try
            {
                if (!IsTextFile(_Path))
                    return ScanLoaded(_Path, _Result);

                var hits = new List<RegistryReference>();
                ScanText(File.ReadAllText(_Path), hits);

                bool placed = true;
                for (int i = 0; i < hits.Count; i++)
                {
                    var kind = Attribute(hits[i], kinds);
                    if (kind == null) placed = false;
                    hits[i] = hits[i].WithKind(kind);
                }

                if (placed)
                {
                    _Result.AddRange(hits);
                    return true;
                }

                // Some references can't be placed from their IDs: read their exact kinds from the loaded objects.
                var loaded = new List<RegistryReference>();
                if (ScanLoaded(_Path, loaded))
                {
                    _Result.AddRange(loaded);
                    return true;
                }

                _Result.AddRange(hits.FindAll(e => e.Kind != null));
                return false;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"Registry references: could not read \"{_Path}\" ({exception.Message}).");
                return false;
            }
        }

        /// <summary>
        /// The kind a reference read as text belongs to, or <see langword="null"/> if its IDs don't tell.
        /// </summary>
        private static RegistryKind Attribute(in RegistryReference _Reference, IReadOnlyList<RegistryKind> _Kinds)
        {
            if (_Kinds.Count == 1) return _Kinds[0];

            RegistryKind found = null;
            foreach (var kind in _Kinds)
            {
                bool matches = _Reference.HasRegistry ? kind.KnowsRegistry(_Reference.Registry) : kind.HasRowAnywhere(_Reference.Id);
                if (!matches) continue;
                if (found != null) return null; // known to several kinds

                found = kind;
            }

            return found;
        }

        private static bool IsTextFile(string _Path)
        {
            using var stream = File.OpenRead(_Path);

            var buffer = new byte[5];
            return stream.Read(buffer, 0, buffer.Length) == buffer.Length && Encoding.ASCII.GetString(buffer) == "%YAML";
        }



        #region Text
        /// <summary>
        /// The fast path for text-serialized files: reference values, and prefab instance overrides of references.
        /// The references have no kind yet.
        /// </summary>
        public static void ScanText(string _Text, List<RegistryReference> _Result)
        {
            if (_Text.IndexOf("_registry:", StringComparison.Ordinal) >= 0)
            {
                foreach (Match match in REFERENCE_PATTERN.Matches(_Text))
                {
                    var registry = RegistryProperties.ParseGuid(match.Groups[1].Value, match.Groups[2].Value);
                    var id = RegistryProperties.ParseGuid(match.Groups[3].Value, match.Groups[4].Value);

                    if (!id.IsZero)
                        _Result.Add(new RegistryReference(null, registry, id, ParseName(match.Groups[5].Value)));
                }
            }

            if (_Text.IndexOf("._id._", StringComparison.Ordinal) >= 0)
                ScanOverrides(_Text, _Result);
        }

        /// <summary>
        /// Prefab instance overrides store one modification per changed field. A changed reference has both halves
        /// of its ID, but its registry only if that changed too.
        /// </summary>
        private static void ScanOverrides(string _Text, List<RegistryReference> _Result)
        {
            // Target and property path of the reference → registry hi, registry lo, ID hi, ID lo, and name.
            var references = new Dictionary<string, (ulong?[] Parts, string Name)>();

            foreach (Match match in MODIFICATION_PATTERN.Matches(_Text))
            {
                string propertyPath = match.Groups[2].Value;
                string value = match.Groups[3].Value.Trim();

                int slot = Array.FindIndex(ID_SUFFIXES, e => propertyPath.EndsWith(e, StringComparison.Ordinal));
                bool isName = slot < 0 && propertyPath.EndsWith(NAME_SUFFIX, StringComparison.Ordinal);
                if (slot < 0 && !isName) continue;

                string prefix = propertyPath[..^(isName ? NAME_SUFFIX.Length : ID_SUFFIXES[slot].Length)];
                string key = match.Groups[1].Value + "|" + prefix;
                if (!references.TryGetValue(key, out var reference))
                    reference = (new ulong?[4], null);

                if (isName)
                    reference.Name = ParseName(value);
                else if (ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong number))
                    reference.Parts[slot] = number;

                references[key] = reference;
            }

            foreach (var (parts, name) in references.Values)
            {
                if (parts[2] == null || parts[3] == null) continue;

                var id = new RegistryGuid(parts[2].Value, parts[3].Value);
                if (id.IsZero) continue;

                var registry = parts[0] != null && parts[1] != null ? new RegistryGuid(parts[0].Value, parts[1].Value) : RegistryGuid.Zero;
                _Result.Add(new RegistryReference(null, registry, id, name));
            }
        }

        /// <summary>
        /// A YAML scalar: plain, or in single or double quotes.
        /// </summary>
        private static string ParseName(string _Value)
        {
            string value = _Value?.Trim() ?? string.Empty;
            if (value.Length >= 2 && (value[0] == '\'' || value[0] == '"') && value[^1] == value[0])
                value = value[1..^1];

            return value;
        }
        #endregion



        #region Loaded
        /// <summary>
        /// The exact path: load the asset and walk it. Used for binary files (Force Binary or Mixed serialization),
        /// and for text files whose references can't be placed from their IDs.
        /// </summary>
        private static bool ScanLoaded(string _Path, List<RegistryReference> _Result)
        {
            if (_Path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                return ScanScene(_Path, _Result);

            // Native assets (terrain, lighting data, meshes) can't hold references: don't load them.
            var type = AssetDatabase.GetMainAssetTypeAtPath(_Path);
            if (type == null) return true;
            if (_Path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) && !typeof(ScriptableObject).IsAssignableFrom(type)) return true;

            Collect(RegistryPropertyWalker.GetAssetObjects(_Path), _Result);
            return true;
        }

        private static bool ScanScene(string _Path, List<RegistryReference> _Result)
        {
            var scene = SceneManager.GetSceneByPath(_Path);
            if (scene.isLoaded)
            {
                Collect(RegistryPropertyWalker.GetSceneObjects(scene), _Result);
                return true;
            }

            // Scenes can't be opened in Play Mode.
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;

            bool inSetup = scene.IsValid();
            scene = EditorSceneManager.OpenScene(_Path, OpenSceneMode.Additive);
            try
            {
                Collect(RegistryPropertyWalker.GetSceneObjects(scene), _Result);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, !inSetup);
            }

            return true;
        }

        /// <summary>
        /// The references of loaded objects, without the values prefab instances inherit.
        /// </summary>
        public static void Collect(IEnumerable<Object> _Objects, List<RegistryReference> _Result)
        {
            foreach (var target in _Objects)
            {
                using var serializedObject = new SerializedObject(target);
                foreach (var (property, kind) in RegistryPropertyWalker.Enumerate(serializedObject, true))
                {
                    var reference = RegistryProperties.Read(property, kind);
                    if (!reference.Id.IsZero)
                        _Result.Add(reference);
                }
            }
        }
        #endregion
    }
}
