using System;
using System.Collections.Generic;

using UnityEngine;

namespace LMK.Editor.Tests
{
    /// <summary>
    /// Target of the <see cref="UIElements.PropertyList"/> tests. In its own file so it has a script asset.
    /// </summary>
    public class PropertyListTestHost : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string name;
            public float value;
        }

        public List<int> numbers = new();
        public Entry[] entries = Array.Empty<Entry>();
        public string text;
    }
}
