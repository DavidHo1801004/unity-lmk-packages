using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Attributes;

namespace LMK.Editor.AbilitySystem.Tests
{
    // Shapes the ability system will author with [InlineInstanced]: an inline object (effect component, ability logic)
    // that holds further inline objects, singly and in lists (targeting strategy, action list).

    [Serializable, CanReferenceInline]
    public abstract class SpikeNode
    {
        public int value;
    }

    [Serializable]
    public class SpikeLeaf : SpikeNode
    {
        public float number;

        [NonSerialized] public int runtimeCount;
    }

    [Serializable]
    public class SpikeBranch : SpikeNode
    {
        [SerializeReference, InlineInstanced] public SpikeNode inner;
        [SerializeReference, InlineInstanced] public List<SpikeNode> items = new();
    }
}
