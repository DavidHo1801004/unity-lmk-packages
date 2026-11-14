using UnityEngine;

using LMK.Runtime.Attributes;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// Asset holding a tree of inline-instanced objects. In its own file so it has a script asset.
    /// </summary>
    public class SpikeHost : ScriptableObject
    {
        [SerializeReference, InlineInstanced] public SpikeNode root;
    }
}
