using System.Collections.Generic;

using UnityEngine;

namespace LMK.Editor.Registry.Tests
{
    /// <summary>
    /// Asset with references in every shape the tooling must find: a field, a list, a list of structs, a managed
    /// reference, and a reference of the second kind. In its own file so it has a script asset.
    /// </summary>
    public sealed class ReferenceHolder : ScriptableObject
    {
        public TreeRef single;
        public List<TreeRef> list = new();
        public List<TreeRefEntry> entries = new();

        [SerializeReference]
        public TreeRefManaged managed;

        public TreeRefB other;
    }
}
