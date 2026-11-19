using System;
using System.Collections.Generic;

using UnityEngine;

namespace LMK.Editor.Tests
{
    /// <summary>
    /// Target of the <see cref="UIElements.AlignedField"/> layout tests: <see cref="Picked"/> is drawn as an
    /// AlignedField by <see cref="AlignedFieldTestDrawer"/>, at the top level, in nested structs and in lists.
    /// In its own file so it has a script asset.
    /// </summary>
    public class AlignedFieldTestHost : ScriptableObject
    {
        [Serializable]
        public struct Picked
        {
            public int id;
        }

        [Serializable]
        public struct Inner
        {
            public float deep;
            public Picked picked;
        }

        [Serializable]
        public struct Nested
        {
            public float number;
            public Picked picked;
            public Inner inner;
        }

        public float plain;
        public Picked picked;
        public Nested nested;
        public List<Picked> pickedList = new() { new Picked(), new Picked() };
        public List<Nested> nestedList = new() { new Nested() };
    }
}
