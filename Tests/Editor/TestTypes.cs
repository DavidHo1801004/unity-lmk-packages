using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Attributes;

namespace LMK.Editor.Attributes.Tests
{
    public interface ITestContract { }

    [Serializable, CanReferenceInline]
    public abstract class TestBase
    {
        public int value;
    }

    [Serializable] public class TestLeafA : TestBase, ITestContract { public float number; }
    [Serializable] public class TestLeafB : TestBase { public string text; }

    [Serializable, CanNotReferenceInline] public class TestHidden : TestBase { }
    [Serializable] public class TestHiddenChild : TestHidden { }
    [Serializable, CanReferenceInline] public class TestReexposed : TestHidden { }

    public class TestNotSerializable : TestBase { }

    [Serializable]
    public class TestNoDefaultConstructor : TestBase
    {
        public TestNoDefaultConstructor(int _Value) { value = _Value; }
    }

    [Serializable]
    public class TestNode : TestBase
    {
        [SerializeReference] public TestBase child;
        [SerializeReference] public List<TestBase> children = new();
    }

    [Serializable]
    public class TestThrows : TestBase
    {
        public TestThrows() { throw new InvalidOperationException("Constructor failure expected by the test."); }
    }

    /// <summary>
    /// Throws on its second construction, so the first target of a multi-object write succeeds.
    /// </summary>
    [Serializable]
    public class TestThrowsOnSecond : TestBase
    {
        public static int Created;

        public TestThrowsOnSecond()
        {
            if (++Created == 2)
                throw new InvalidOperationException("Constructor failure expected by the test.");
        }
    }
}
