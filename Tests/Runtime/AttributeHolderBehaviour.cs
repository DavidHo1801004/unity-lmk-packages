using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.AbilitySystem;
using LMK.Runtime.GameplayTags;

namespace LMK.AbilitySystem.Tests
{
    /// <summary>
    /// Component with a tag and attributes, for scene tests across registry kinds. In its own file so it has a script
    /// asset.
    /// </summary>
    public sealed class AttributeHolderBehaviour : MonoBehaviour
    {
        public GameplayTag tag;

        public GameplayAttribute attribute;

        public List<GameplayAttribute> attributes = new();
    }
}
