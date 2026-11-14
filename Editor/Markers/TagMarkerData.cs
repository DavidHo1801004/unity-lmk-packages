using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// A marker the user defined: a short text and a colour, shown on the rows of the tags it is assigned to.
    /// </summary>
    [Serializable]
    internal struct TagMarkerDefinition
    {
        public RegistryGuid id;
        public string text;
        public Color color;

        public TagMarkerDefinition(RegistryGuid _Id, string _Text, Color _Color)
        {
            id = _Id;
            text = _Text ?? string.Empty;
            color = _Color;
        }
    }



    /// <summary>
    /// A marker assigned to one tag, of any registry.
    /// </summary>
    [Serializable]
    internal struct TagMarkerAssignment
    {
        public RegistryGuid registry;
        public RegistryGuid tag;
        public RegistryGuid marker;

        public TagMarkerAssignment(RegistryGuid _Registry, RegistryGuid _Tag, RegistryGuid _Marker)
        {
            registry = _Registry;
            tag = _Tag;
            marker = _Marker;
        }

        public bool Is(RegistryGuid _Registry, RegistryGuid _Tag) => registry.Equals(_Registry) && tag.Equals(_Tag);

        public bool Is(RegistryGuid _Registry, RegistryGuid _Tag, RegistryGuid _Marker) => Is(_Registry, _Tag) && marker.Equals(_Marker);
    }



    /// <summary>
    /// One user's markers: the definitions, in the order rows show them, and their assignments to tags.
    /// </summary>
    [Serializable]
    internal sealed class TagMarkerSet
    {
        public List<TagMarkerDefinition> definitions = new();

        /// <summary>
        /// One entry per tag and marker, never twice.
        /// </summary>
        public List<TagMarkerAssignment> assignments = new();

        public bool IsEmpty => definitions.Count == 0 && assignments.Count == 0;

        public TagMarkerSet Clone() => new()
        {
            definitions = new List<TagMarkerDefinition>(definitions),
            assignments = new List<TagMarkerAssignment>(assignments),
        };

        public int IndexOfDefinition(RegistryGuid _Marker)
        {
            for (int i = 0; i < definitions.Count; i++)
                if (definitions[i].id.Equals(_Marker))
                    return i;

            return -1;
        }

        /// <summary>
        /// Same definitions in the same order, and the same assignments in any order.
        /// </summary>
        public bool ContentEquals(TagMarkerSet _Other)
        {
            if (_Other == null || definitions.Count != _Other.definitions.Count || assignments.Count != _Other.assignments.Count) return false;

            for (int i = 0; i < definitions.Count; i++)
            {
                var a = definitions[i];
                var b = _Other.definitions[i];
                if (!a.id.Equals(b.id) || a.text != b.text || !SameColor(a.color, b.color)) return false;
            }

            var others = new HashSet<(RegistryGuid, RegistryGuid, RegistryGuid)>();
            foreach (var assignment in _Other.assignments)
                others.Add((assignment.registry, assignment.tag, assignment.marker));

            foreach (var assignment in assignments)
                if (!others.Contains((assignment.registry, assignment.tag, assignment.marker)))
                    return false;

            return true;
        }

        /// <summary>
        /// A colour as markers store it: opaque, and rounded to 8 bits per channel, so it reads back unchanged
        /// from the file.
        /// </summary>
        public static Color Normalize(Color _Color)
        {
            Color32 rounded = _Color;
            rounded.a = 255;
            return rounded;
        }

        /// <summary>
        /// Whether two colours are the same once stored.
        /// </summary>
        public static bool SameColor(Color _A, Color _B)
        {
            Color32 a = Normalize(_A);
            Color32 b = Normalize(_B);
            return a.r == b.r && a.g == b.g && a.b == b.b;
        }
    }
}
