using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// Asset type of the attribute serialization fixture. The checked-in asset holds known IDs and names, so a change
    /// of the serialized layout of <see cref="RegistryGuid"/> or <see cref="GameplayAttribute"/> fails the tests.
    /// </summary>
    public sealed class AttributeSerializationFixture : ScriptableObject
    {
        public GameplayAttribute single;

        public List<GameplayAttribute> list = new();
    }
}
