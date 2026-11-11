using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// A colour one user gave a tag, shown as a strip on its row in tag trees. Editor-only and per user.
    /// </summary>
    [Serializable]
    internal struct TagMarker
    {
        public TagGuid registry;
        public TagGuid tag;
        public Color color;

        public TagMarker(TagGuid _Registry, TagGuid _Tag, Color _Color)
        {
            registry = _Registry;
            tag = _Tag;
            color = _Color;
        }

        public bool Is(TagGuid _Registry, TagGuid _Tag) => registry.Equals(_Registry) && tag.Equals(_Tag);
    }



    /// <summary>
    /// The saved tag markers of this user, in <c>UserSettings/LMKGameplayTags/Markers.json</c>. The folder is
    /// per user and not committed, so markers are never shared.
    /// </summary>
    /// <remarks>
    /// Only <see cref="TagOperations.Save"/> writes the file. Edits are staged in <see cref="TagRegistryState"/>
    /// like registry edits.
    /// </remarks>
    internal static class TagMarkerStore
    {
        public const string DEFAULT_FILE_PATH = "UserSettings/LMKGameplayTags/Markers.json";

        /// <summary>
        /// Where the markers are saved. Only tests change it.
        /// </summary>
        internal static string FilePath { get; set; } = DEFAULT_FILE_PATH;

        [Serializable]
        private sealed class FileData
        {
            public List<Entry> markers = new();
        }

        [Serializable]
        private sealed class Entry
        {
            public string registry;
            public string tag;
            public string color;
        }



        /// <summary>
        /// Read the saved markers. Missing or unreadable files give no markers.
        /// </summary>
        public static List<TagMarker> Load()
        {
            var result = new List<TagMarker>();
            if (!File.Exists(FilePath)) return result;

            FileData data;
            try
            {
                data = JsonUtility.FromJson<FileData>(File.ReadAllText(FilePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogWarning($"Gameplay tag markers could not be read from \"{FilePath}\": {exception.Message}");
                return result;
            }

            if (data?.markers == null) return result;

            foreach (var entry in data.markers)
            {
                if (entry == null
                    || !TagGuid.TryParse(entry.registry, out var registry)
                    || !TagGuid.TryParse(entry.tag, out var tag)
                    || !ColorUtility.TryParseHtmlString("#" + entry.color, out var color))
                    continue;

                result.Add(new TagMarker(registry, tag, color));
            }

            return result;
        }

        /// <summary>
        /// Replace the saved markers. An empty list deletes the file.
        /// </summary>
        public static void Save(IReadOnlyList<TagMarker> _Markers)
        {
            if (_Markers == null || _Markers.Count == 0)
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
                return;
            }

            var data = new FileData();
            foreach (var marker in _Markers)
            {
                data.markers.Add(new Entry
                {
                    registry = marker.registry.ToString(),
                    tag = marker.tag.ToString(),
                    color = ColorUtility.ToHtmlStringRGBA(marker.color),
                });
            }

            string folder = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllText(FilePath, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
        }

        /// <summary>
        /// Whether two marker lists hold the same colours for the same tags, in any order.
        /// </summary>
        public static bool AreEqual(IReadOnlyList<TagMarker> _A, IReadOnlyList<TagMarker> _B)
        {
            if (_A.Count != _B.Count) return false;

            foreach (var marker in _A)
            {
                bool found = false;
                foreach (var other in _B)
                {
                    if (!other.Is(marker.registry, marker.tag)) continue;

                    found = other.color == marker.color;
                    break;
                }

                if (!found) return false;
            }

            return true;
        }
    }
}
