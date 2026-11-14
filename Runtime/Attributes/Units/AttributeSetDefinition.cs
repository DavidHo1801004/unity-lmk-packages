using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Attributes;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Which attributes a unit has, with optional initial values, and the behaviours that react to them. Takes the
    /// place of UE's <c>UAttributeSet</c> class. Read-only at runtime: one asset serves every unit.
    /// </summary>
    /// <remarks>
    /// An attribute listed by two sets of a unit exists once; the first set's initial value wins. Behaviours are
    /// inline-instanced and shared by every unit that uses the set, so they must hold no per-unit state.
    /// </remarks>
    [CreateAssetMenu(menuName = "LMK/Ability System/Attribute Set", fileName = "AttributeSet")]
    public sealed class AttributeSetDefinition : ScriptableObject
    {
        /// <summary>
        /// One attribute of the set.
        /// </summary>
        [Serializable]
        public struct Entry
        {
            public GameplayAttribute attribute;

            [Tooltip("Start from the initial value below instead of the registry default.")]
            public bool overrideDefault;

            public ScalableFloat initialValue;

            public Entry(GameplayAttribute _Attribute, bool _OverrideDefault = false, ScalableFloat _InitialValue = default)
            {
                attribute = _Attribute;
                overrideDefault = _OverrideDefault;
                initialValue = _InitialValue;
            }
        }

        [SerializeField]
        private List<Entry> entries = new();

        [SerializeReference, InlineInstanced]
        private List<AttributeBehaviour> behaviours = new();

        public IReadOnlyList<Entry> Entries => entries;

        public IReadOnlyList<AttributeBehaviour> Behaviours => behaviours;

        /// <summary>
        /// Add an entry. For tools and tests that build a set in code; game code treats sets as read-only.
        /// </summary>
        internal AttributeSetDefinition Add(GameplayAttribute _Attribute, float? _InitialValue = null)
        {
            entries.Add(new Entry(_Attribute, _InitialValue.HasValue, _InitialValue ?? 0f));
            return this;
        }

        /// <summary>
        /// Add an entry. For tools and tests that build a set in code; game code treats sets as read-only.
        /// </summary>
        internal AttributeSetDefinition Add(Entry _Entry)
        {
            entries.Add(_Entry);
            return this;
        }

        /// <summary>
        /// Add a behaviour. For tools and tests that build a set in code; game code treats sets as read-only.
        /// </summary>
        internal AttributeSetDefinition Add(AttributeBehaviour _Behaviour)
        {
            behaviours.Add(_Behaviour);
            return this;
        }
    }
}
