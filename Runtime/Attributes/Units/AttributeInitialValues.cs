using System;
using System.Collections.Generic;

using UnityEngine;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Starting values of a unit's attributes, apart from its <see cref="AttributeSetDefinition"/>s, so one set can
    /// serve units that start differently (a mage and a knight with the same attributes). Read-only at runtime.
    /// </summary>
    /// <remarks>
    /// Applied when a set adds an attribute: the attribute starts at its value here instead of its registry default,
    /// then is clamped by its bounds. Attributes the unit doesn't have are ignored.
    /// </remarks>
    [CreateAssetMenu(menuName = "LMK/Ability System/Attribute Initial Values", fileName = "AttributeInitialValues")]
    [Icon(ICON_PATH)]
    public sealed class AttributeInitialValues : ScriptableObject
    {
        internal const string ICON_PATH = "Packages/com.lmk.abilitysystem/Editor/Assets/Icons/AttributeInitialValues.png";

        /// <summary>
        /// The starting value of one attribute.
        /// </summary>
        [Serializable]
        public struct Entry
        {
            public GameplayAttribute attribute;

            [Tooltip("The starting value, scaled by the level the set is added at when a curve is set.")]
            public ScalableFloat value;

            public Entry(GameplayAttribute _Attribute, ScalableFloat _Value)
            {
                attribute = _Attribute;
                value = _Value;
            }
        }

        [SerializeField]
        private List<Entry> entries = new();

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>
        /// The starting value of <paramref name="_Attribute"/> at <paramref name="_Level"/>, if listed. The first entry
        /// wins when an attribute is listed twice.
        /// </summary>
        public bool TryGet(GameplayAttribute _Attribute, float _Level, out float _Value)
        {
            foreach (var entry in entries)
            {
                if (entry.attribute != _Attribute) continue;

                _Value = entry.value.At(_Level);
                return true;
            }

            _Value = 0f;
            return false;
        }

        /// <summary>
        /// Whether <paramref name="_Attribute"/> has a starting value here.
        /// </summary>
        public bool Contains(GameplayAttribute _Attribute) => entries.Exists(e => e.attribute == _Attribute);

        /// <summary>
        /// Add a starting value. For tools and tests that build one in code; game code treats it as read-only.
        /// </summary>
        internal AttributeInitialValues Add(GameplayAttribute _Attribute, ScalableFloat _Value)
        {
            entries.Add(new Entry(_Attribute, _Value));
            return this;
        }
    }
}
