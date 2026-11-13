using System.Collections.Generic;

using UnityEngine;

namespace LMK.Editor.Registry.Tests
{
    /// <summary>
    /// Component with references of both test kinds, for scene tests. In its own file so it has a script asset.
    /// </summary>
    public sealed class ReferenceHolderBehaviour : MonoBehaviour
    {
        public TreeRef single;
        public List<TreeRef> list = new();
        public TreeRefB other;
    }
}
