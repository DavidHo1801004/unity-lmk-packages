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

using LMK.Runtime.GameplayTags;

using Object = UnityEngine.Object;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Reads the tag references saved in an asset file, without changing it. Used by the reference index, registry
    /// generation for packages, pack validation and the mapping of imported assets.
    /// </summary>
    /// <remarks>
    /// Text-serialized files are read as text, which is fast. Binary files are loaded and walked instead.
    /// Values a prefab instance inherits from its prefab are not returned: the prefab holds them. A prefab instance
    /// override that changed only the tag ID has no registry: its <see cref="GameplayTag.RegistryId"/> is zero
    /// (see <see cref="TagKey"/>).
    /// </remarks>
    internal static class TagAssetScanner
    {
        /// <summary>
        /// File types that can hold serialized tag references.
        /// </summary>
        private static readonly HashSet<string> EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
        {
            ".unity", ".prefab", ".asset", ".playable", ".controller",
        };

        // A serialized GameplayTag in block or flow style: its registry ID, its tag ID, then its name.
        private static readonly Regex TAG_PATTERN = new(
            @"(?<![\w.])_registry:\s*\{?\s*_hi:\s*(\d+)\s*,?\s*_lo:\s*(\d+)\s*\}?\s*_id:\s*\{?\s*_hi:\s*(\d+)\s*,?\s*_lo:\s*(\d+)(?:\s*\})?(?:\s*_lastKnownName:[ \t]*([^\r\n]*))?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // One prefab instance property modification: target, property path and value.
        private static readonly Regex MODIFICATION_PATTERN = new(
            @"-\s*target:\s*\{([^}]*)\}\s*propertyPath:\s*(\S+)\s*value:[ \t]*([^\r\n]*)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private const string NAME_SUFFIX = "._lastKnownName";
        private static readonly string[] ID_SUFFIXES = { "._registry._hi", "._registry._lo", "._id._hi", "._id._lo" };



        /// <summary>
        /// Whether a file of this type can hold serialized tag references.
        /// </summary>
        public static bool CanHoldTags(string _Path)
            => !string.IsNullOrEmpty(_Path) && EXTENSIONS.Contains(Path.GetExtension(_Path));

        /// <summary>
        /// Read the tags an asset file references, None excluded.
        /// </summary>
        /// <returns> Whether the asset could be read completely. </returns>
        public static bool Scan(string _Path, List<GameplayTag> _Result)
        {
            try
            {
                if (IsTextFile(_Path))
                {
                    ScanText(File.ReadAllText(_Path), _Result);
                    return true;
                }

                return ScanLoaded(_Path, _Result);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning($"Gameplay tags: could not read \"{_Path}\" ({exception.Message}).");
                return false;
            }
        }

        private static bool IsTextFile(string _Path)
        {
            using var stream = File.OpenRead(_Path);

            var buffer = new byte[5];
            return stream.Read(buffer, 0, buffer.Length) == buffer.Length && Encoding.ASCII.GetString(buffer) == "%YAML";
        }



        #region Text
        /// <summary>
        /// The fast path for text-serialized files: tag values, and prefab instance overrides of tags.
        /// </summary>
        internal static void ScanText(string _Text, List<GameplayTag> _Result)
        {
            if (_Text.IndexOf("_registry:", StringComparison.Ordinal) >= 0)
            {
                foreach (Match match in TAG_PATTERN.Matches(_Text))
                {
                    var registry = ParseGuid(match.Groups[1].Value, match.Groups[2].Value);
                    var id = ParseGuid(match.Groups[3].Value, match.Groups[4].Value);

                    if (!id.IsZero)
                        _Result.Add(GameplayTag.FromId(registry, id, ParseName(match.Groups[5].Value)));
                }
            }

            if (_Text.IndexOf("._id._", StringComparison.Ordinal) >= 0)
                ScanOverrides(_Text, _Result);
        }

        /// <summary>
        /// Prefab instance overrides store one modification per changed field. A changed tag has both halves of its
        /// ID, but its registry only if that changed too.
        /// </summary>
        private static void ScanOverrides(string _Text, List<GameplayTag> _Result)
        {
            // Target and property path of the tag → registry hi, registry lo, ID hi, ID lo, and name.
            var tags = new Dictionary<string, (ulong?[] Parts, string Name)>();

            foreach (Match match in MODIFICATION_PATTERN.Matches(_Text))
            {
                string propertyPath = match.Groups[2].Value;
                string value = match.Groups[3].Value.Trim();

                int slot = Array.FindIndex(ID_SUFFIXES, e => propertyPath.EndsWith(e, StringComparison.Ordinal));
                bool isName = slot < 0 && propertyPath.EndsWith(NAME_SUFFIX, StringComparison.Ordinal);
                if (slot < 0 && !isName) continue;

                string prefix = propertyPath[..^(isName ? NAME_SUFFIX.Length : ID_SUFFIXES[slot].Length)];
                string key = match.Groups[1].Value + "|" + prefix;
                if (!tags.TryGetValue(key, out var tag))
                    tag = (new ulong?[4], null);

                if (isName)
                    tag.Name = ParseName(value);
                else if (ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong number))
                    tag.Parts[slot] = number;

                tags[key] = tag;
            }

            foreach (var (parts, name) in tags.Values)
            {
                if (parts[2] == null || parts[3] == null) continue;

                var id = new TagGuid(parts[2].Value, parts[3].Value);
                if (id.IsZero) continue;

                var registry = parts[0] != null && parts[1] != null ? new TagGuid(parts[0].Value, parts[1].Value) : TagGuid.Zero;
                _Result.Add(GameplayTag.FromId(registry, id, name));
            }
        }

        private static TagGuid ParseGuid(string _Hi, string _Lo)
            => new(ulong.Parse(_Hi, NumberStyles.None, CultureInfo.InvariantCulture), ulong.Parse(_Lo, NumberStyles.None, CultureInfo.InvariantCulture));

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
        /// The slow path for binary files (Force Binary or Mixed serialization): load the asset and walk it.
        /// </summary>
        private static bool ScanLoaded(string _Path, List<GameplayTag> _Result)
        {
            if (_Path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                return ScanScene(_Path, _Result);

            // Native assets (terrain, lighting data, meshes) can't hold tags: don't load them.
            var type = AssetDatabase.GetMainAssetTypeAtPath(_Path);
            if (type == null) return true;
            if (_Path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) && !typeof(ScriptableObject).IsAssignableFrom(type)) return true;

            Collect(TagPropertyWalker.GetAssetObjects(_Path), _Result);
            return true;
        }

        private static bool ScanScene(string _Path, List<GameplayTag> _Result)
        {
            var scene = SceneManager.GetSceneByPath(_Path);
            if (scene.isLoaded)
            {
                Collect(TagPropertyWalker.GetSceneObjects(scene), _Result);
                return true;
            }

            // Scenes can't be opened in Play Mode.
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;

            bool inSetup = scene.IsValid();
            scene = EditorSceneManager.OpenScene(_Path, OpenSceneMode.Additive);
            try
            {
                Collect(TagPropertyWalker.GetSceneObjects(scene), _Result);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, !inSetup);
            }

            return true;
        }

        /// <summary>
        /// The tags of loaded objects, without the values prefab instances inherit.
        /// </summary>
        public static void Collect(IEnumerable<Object> _Objects, List<GameplayTag> _Result)
        {
            foreach (var target in _Objects)
            {
                using var serializedObject = new SerializedObject(target);
                foreach (var property in TagPropertyWalker.Enumerate(serializedObject, true))
                {
                    var tag = GameplayTagDrawerHelper.Read(property);
                    if (!tag.IsNone)
                        _Result.Add(tag);
                }
            }
        }
        #endregion
    }
}
