using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// A colour one user gave a row, shown as a strip on its row in registry views. Editor-only and per user.
    /// </summary>
    [Serializable]
    public struct RegistryMarker
    {
        public RegistryGuid registry;
        public RegistryGuid row;
        public Color color;

        public RegistryMarker(RegistryGuid _Registry, RegistryGuid _Row, Color _Color)
        {
            registry = _Registry;
            row = _Row;
            color = _Color;
        }

        public bool Is(RegistryGuid _Registry, RegistryGuid _Row) => registry.Equals(_Registry) && row.Equals(_Row);
    }



    /// <summary>
    /// Reads and writes a kind's saved markers, a JSON file under <c>UserSettings/</c>. That folder is per user
    /// and not committed, so markers are never shared.
    /// </summary>
    /// <remarks>
    /// Only <see cref="RegistrySession.Save"/> writes the file. Edits are staged like registry edits.
    /// </remarks>
    public static class RegistryMarkerStore
    {
        [Serializable]
        private sealed class FileData
        {
            public List<Entry> markers = new();
        }

        // The field names match gameplay tags' Markers.json, which keeps working as it is.
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
        public static List<RegistryMarker> Load(string _FilePath)
        {
            var result = new List<RegistryMarker>();
            if (string.IsNullOrEmpty(_FilePath) || !File.Exists(_FilePath)) return result;

            FileData data;
            try
            {
                data = JsonUtility.FromJson<FileData>(File.ReadAllText(_FilePath));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogWarning($"Registry markers could not be read from \"{_FilePath}\": {exception.Message}");
                return result;
            }

            if (data?.markers == null) return result;

            foreach (var entry in data.markers)
            {
                if (entry == null
                    || !RegistryGuid.TryParse(entry.registry, out var registry)
                    || !RegistryGuid.TryParse(entry.tag, out var row)
                    || !ColorUtility.TryParseHtmlString("#" + entry.color, out var color))
                    continue;

                result.Add(new RegistryMarker(registry, row, color));
            }

            return result;
        }

        /// <summary>
        /// Replace the saved markers. An empty list deletes the file.
        /// </summary>
        public static void Save(string _FilePath, IReadOnlyList<RegistryMarker> _Markers)
        {
            if (string.IsNullOrEmpty(_FilePath)) return;

            if (_Markers == null || _Markers.Count == 0)
            {
                if (File.Exists(_FilePath))
                    File.Delete(_FilePath);
                return;
            }

            var data = new FileData();
            foreach (var marker in _Markers)
            {
                data.markers.Add(new Entry
                {
                    registry = marker.registry.ToString(),
                    tag = marker.row.ToString(),
                    color = ColorUtility.ToHtmlStringRGBA(marker.color),
                });
            }

            string folder = Path.GetDirectoryName(_FilePath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllText(_FilePath, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
        }

        /// <summary>
        /// Whether two marker lists hold the same colours for the same rows, in any order.
        /// </summary>
        public static bool AreEqual(IReadOnlyList<RegistryMarker> _A, IReadOnlyList<RegistryMarker> _B)
        {
            if (_A.Count != _B.Count) return false;

            foreach (var marker in _A)
            {
                bool found = false;
                foreach (var other in _B)
                {
                    if (!other.Is(marker.registry, marker.row)) continue;

                    found = other.color == marker.color;
                    break;
                }

                if (!found) return false;
            }

            return true;
        }
    }
}
