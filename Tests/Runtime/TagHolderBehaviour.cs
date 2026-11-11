using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.GameplayTags.Tests
{
    /// <summary>
    /// Component with tag fields in every shape the editor tooling must find: a field, a container, a list of
    /// structs holding containers, and a managed reference.
    /// </summary>
    public sealed class TagHolderBehaviour : MonoBehaviour
    {
        public GameplayTag single;

        public GameplayTagContainer container = new();

        public List<TagEntry> entries = new();

        [SerializeReference]
        public TagManagedEntry managed;
    }

    [Serializable]
    public struct TagEntry
    {
        public GameplayTagContainer tags;
    }

    [Serializable]
    public class TagManagedEntry
    {
        public GameplayTag tag;
    }
}
