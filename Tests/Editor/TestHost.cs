using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Attributes;

namespace LMK.Editor.Attributes.Tests
{
    /// <summary>
    /// Target of the tests. In its own file so it has a script asset, which saved assets need.
    /// </summary>
    public class TestHost : ScriptableObject
    {
        [SerializeReference, InlineInstanced] public TestBase single;
        [SerializeReference, InlineInstanced] public List<TestBase> list = new();
    }
}
