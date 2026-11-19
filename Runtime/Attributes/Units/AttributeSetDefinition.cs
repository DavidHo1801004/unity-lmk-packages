using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Attributes;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Which attributes a unit has, and the behaviours that react to them. Takes the place of UE's <c>UAttributeSet</c>
    /// class. Read-only at runtime: one asset serves every unit.
    /// </summary>
    /// <remarks>
    /// An attribute listed by two sets of a unit exists once. Attributes start at their registry default, or at the
    /// value of the unit's <see cref="AttributeInitialValues"/>, so one set can serve units with different starting
    /// values. Behaviours are inline-instanced and shared by every unit that uses the set, so they must hold no
    /// per-unit state.
    /// </remarks>
    [CreateAssetMenu(menuName = "LMK/Ability System/Attribute Set", fileName = "AttributeSet")]
    [Icon(ICON_PATH)]
    public sealed class AttributeSetDefinition : ScriptableObject
    {
        internal const string ICON_PATH = "Packages/com.lmk.abilitysystem/Editor/Assets/Icons/AttributeSet.png";

        /// <summary>
        /// One attribute of the set.
        /// </summary>
        [Serializable]
        public struct Entry
        {
            public GameplayAttribute attribute;

            public Entry(GameplayAttribute _Attribute)
            {
                attribute = _Attribute;
            }
        }

        [SerializeField]
        private List<Entry> entries = new();

        [SerializeReference, InlineInstanced]
        private List<AttributeBehaviour> behaviours = new();

        public IReadOnlyList<Entry> Entries => entries;

        public IReadOnlyList<AttributeBehaviour> Behaviours => behaviours;

        /// <summary>
        /// Whether the set lists <paramref name="_Attribute"/>.
        /// </summary>
        public bool Contains(GameplayAttribute _Attribute) => entries.Exists(e => e.attribute == _Attribute);

        /// <summary>
        /// Add an attribute. For tools and tests that build a set in code; game code treats sets as read-only.
        /// </summary>
        internal AttributeSetDefinition Add(GameplayAttribute _Attribute)
        {
            entries.Add(new Entry(_Attribute));
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
