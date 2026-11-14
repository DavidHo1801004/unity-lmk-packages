using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// The user's tag markers file, a JSON file under <c>UserSettings/</c>. That folder is per user and not
    /// committed, so markers are never shared. <see cref="TagMarkers"/> writes it after every change.
    /// </summary>
    /// <remarks>
    /// Version 2 holds named marker definitions and their assignments. Version 1 (gameplay tags 1.0 and 1.1, written
    /// by the registry core) held one colour per tag. It is converted when loaded: each distinct colour becomes a
    /// definition named "Marker 1", "Marker 2"..., and the file is written again as version 2.
    /// </remarks>
    internal static class TagMarkerStore
    {
        public const string DEFAULT_FILE_PATH = "UserSettings/LMKGameplayTags/Markers.json";

        public const int VERSION = 2;

        private const string LEGACY_NAME = "Marker";

        /// <summary>
        /// Where the markers are saved. Only tests change it.
        /// </summary>
        internal static string FilePath { get; set; } = DEFAULT_FILE_PATH;



        // What a version 2 file holds.
        [Serializable]
        private class FileData
        {
            public int version;
            public List<DefinitionEntry> definitions = new();
            public List<AssignmentEntry> assignments = new();
        }

        // What any version may hold, for reading: version 1 files have only markers.
        [Serializable]
        private sealed class AnyFileData : FileData
        {
            public List<LegacyEntry> markers = new();
        }

        [Serializable]
        private sealed class DefinitionEntry
        {
            public string id;
            public string text;
            public string color;
        }

        [Serializable]
        private sealed class AssignmentEntry
        {
            public string registry;
            public string tag;
            public string marker;
        }

        // Only read, so only JsonUtility assigns its fields.
#pragma warning disable CS0649
        [Serializable]
        private sealed class LegacyEntry
        {
            public string registry;
            public string tag;
            public string color;
        }
#pragma warning restore CS0649



        /// <summary>
        /// Read the markers. A missing or unreadable file gives no markers. A version 1 file is converted and
        /// written again as version 2.
        /// </summary>
        public static TagMarkerSet Load() => Load(FilePath);

        /// <inheritdoc cref="Load()"/>
        internal static TagMarkerSet Load(string _FilePath)
        {
            var set = new TagMarkerSet();
            if (string.IsNullOrEmpty(_FilePath) || !File.Exists(_FilePath)) return set;

            AnyFileData data;
            try
            {
                data = JsonUtility.FromJson<AnyFileData>(File.ReadAllText(_FilePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogWarning($"Gameplay tag markers could not be read from \"{_FilePath}\": {exception.Message}");
                return set;
            }

            if (data == null) return set;

            var warnings = new List<string>();
            if (data.version < VERSION)
            {
                ReadLegacy(data, set, warnings);
                Save(_FilePath, set);
            }
            else
            {
                Read(data, set, warnings);
            }

            if (warnings.Count > 0)
                Debug.LogWarning($"Some gameplay tag markers in \"{_FilePath}\" were skipped or renamed:\n{string.Join("\n", warnings)}");

            return set;
        }

        /// <summary>
        /// Replace the file with the markers. No markers deletes it.
        /// </summary>
        public static void Save(TagMarkerSet _Set) => Save(FilePath, _Set);

        /// <inheritdoc cref="Save(TagMarkerSet)"/>
        internal static void Save(string _FilePath, TagMarkerSet _Set)
        {
            if (string.IsNullOrEmpty(_FilePath)) return;

            if (_Set == null || _Set.IsEmpty)
            {
                if (File.Exists(_FilePath))
                    File.Delete(_FilePath);
                return;
            }

            var data = new FileData { version = VERSION };
            foreach (var definition in _Set.definitions)
                data.definitions.Add(new DefinitionEntry
                {
                    id = definition.id.ToString(),
                    text = definition.text,
                    color = ColorUtility.ToHtmlStringRGB(definition.color),
                });

            foreach (var assignment in _Set.assignments)
                data.assignments.Add(new AssignmentEntry
                {
                    registry = assignment.registry.ToString(),
                    tag = assignment.tag.ToString(),
                    marker = assignment.marker.ToString(),
                });

            string folder = Path.GetDirectoryName(_FilePath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllText(_FilePath, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
        }



        #region Reading
        private static void Read(AnyFileData _Data, TagMarkerSet _Set, List<string> _Warnings)
        {
            var texts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in _Data.definitions ?? new List<DefinitionEntry>())
            {
                if (entry == null || !RegistryGuid.TryParse(entry.id, out var id) || id.IsZero || _Set.IndexOfDefinition(id) >= 0)
                {
                    _Warnings.Add($"Marker with an invalid or repeated ID \"{entry?.id}\".");
                    continue;
                }

                if (!TryParseColor(entry.color, out var color))
                {
                    _Warnings.Add($"Marker \"{entry.text}\" has an invalid colour \"{entry.color}\": white is used.");
                    color = Color.white;
                }

                string text = UniqueText(Clean(entry.text), texts, _Set.definitions.Count + 1);
                if (text != Clean(entry.text))
                    _Warnings.Add($"Marker \"{entry.text}\" was renamed \"{text}\": names must be unique and not empty.");

                texts.Add(text);
                _Set.definitions.Add(new TagMarkerDefinition(id, text, TagMarkerSet.Normalize(color)));
            }

            foreach (var entry in _Data.assignments ?? new List<AssignmentEntry>())
            {
                if (entry == null
                    || !RegistryGuid.TryParse(entry.registry, out var registry)
                    || !RegistryGuid.TryParse(entry.tag, out var tag)
                    || !RegistryGuid.TryParse(entry.marker, out var marker))
                {
                    _Warnings.Add("An assignment with an invalid ID.");
                    continue;
                }

                if (_Set.IndexOfDefinition(marker) < 0)
                {
                    _Warnings.Add($"An assignment of the unknown marker {marker}.");
                    continue;
                }

                AddAssignment(_Set, new TagMarkerAssignment(registry, tag, marker));
            }
        }

        /// <summary>
        /// One colour per tag: each distinct colour becomes a definition, in the order first seen.
        /// </summary>
        private static void ReadLegacy(AnyFileData _Data, TagMarkerSet _Set, List<string> _Warnings)
        {
            var byColor = new Dictionary<Color32, RegistryGuid>();

            foreach (var entry in _Data.markers ?? new List<LegacyEntry>())
            {
                if (entry == null
                    || !RegistryGuid.TryParse(entry.registry, out var registry)
                    || !RegistryGuid.TryParse(entry.tag, out var tag)
                    || !TryParseColor(entry.color, out var color))
                {
                    _Warnings.Add("A marker with an invalid ID or colour.");
                    continue;
                }

                // Fully transparent meant no marker.
                if (color.a <= 0f) continue;

                Color32 key = TagMarkerSet.Normalize(color);
                if (!byColor.TryGetValue(key, out var marker))
                {
                    marker = RegistryGuid.NewGuid();
                    byColor.Add(key, marker);
                    _Set.definitions.Add(new TagMarkerDefinition(marker, $"{LEGACY_NAME} {byColor.Count}", key));
                }

                AddAssignment(_Set, new TagMarkerAssignment(registry, tag, marker));
            }
        }

        private static void AddAssignment(TagMarkerSet _Set, TagMarkerAssignment _Assignment)
        {
            foreach (var existing in _Set.assignments)
                if (existing.Is(_Assignment.registry, _Assignment.tag, _Assignment.marker))
                    return;

            _Set.assignments.Add(_Assignment);
        }

        private static bool TryParseColor(string _Text, out Color _Color)
        {
            _Color = Color.white;
            return !string.IsNullOrEmpty(_Text) && ColorUtility.TryParseHtmlString("#" + _Text, out _Color);
        }

        private static string Clean(string _Text) => (_Text ?? string.Empty).Trim();

        /// <summary>
        /// <paramref name="_Text"/>, or "Marker N" if it's empty, with " (2)", " (3)"... if it's taken.
        /// </summary>
        private static string UniqueText(string _Text, HashSet<string> _Taken, int _Number)
        {
            if (string.IsNullOrEmpty(_Text))
                _Text = $"{LEGACY_NAME} {_Number}";

            if (!_Taken.Contains(_Text)) return _Text;

            for (int i = 2; ; i++)
            {
                string candidate = $"{_Text} ({i})";
                if (!_Taken.Contains(candidate)) return candidate;
            }
        }
        #endregion
    }
}
