using UnityEngine;

using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// Asset type of the serialization fixture. The checked-in asset holds known IDs and names,
    /// so a change of the serialized layout of <see cref="RegistryGuid"/>, <see cref="GameplayTag"/> or
    /// <see cref="GameplayTagContainer"/> fails the tests.
    /// </summary>
    public sealed class TagSerializationFixture : ScriptableObject
    {
        public GameplayTag single;

        public GameplayTagContainer container = new();
    }
}
