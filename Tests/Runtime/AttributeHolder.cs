using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.AbilitySystem;
using LMK.Runtime.GameplayTags;

namespace LMK.AbilitySystem.Tests
{
    /// <summary>
    /// Asset with a tag and attributes in the shapes the registry tooling must find: fields, a list, and a managed
    /// reference. In its own file so it has a script asset.
    /// </summary>
    public sealed class AttributeHolder : ScriptableObject
    {
        public GameplayTag tag;

        public GameplayAttribute attribute;

        public List<GameplayAttribute> attributes = new();

        [SerializeReference]
        public AttributeManagedEntry managed;
    }

    [Serializable]
    public class AttributeManagedEntry
    {
        public GameplayAttribute attribute;
    }
}
