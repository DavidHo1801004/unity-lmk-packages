using System;
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;

using LMK.Editor.Attributes;

using Object = UnityEngine.Object;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// Spike: can the ability system rely on nested [InlineInstanced] objects, and on Object.Instantiate to clone them? <br/>
    /// The drawer's assign and read helpers are internal to com.lmk.attributes, so they are reached by reflection.
    /// A test that cannot find an API it needs is inconclusive, not failed.
    /// </summary>
    public sealed class NestedInlineInstancedSpikeTests
    {
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly List<Object> created = new();

        private SpikeHost CreateHost(SpikeNode _Root = null)
        {
            var host = ScriptableObject.CreateInstance<SpikeHost>();
            host.root = _Root;
            created.Add(host);
            return host;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var target in created)
            {
                if (target != null) Object.DestroyImmediate(target);
            }
            created.Clear();
        }



        #region Drawer helpers (reflection)
        private static void AssignType(SerializedObject _SerializedObject, string _Path, Type _Type)
        {
            var method = typeof(InlineInstancedAttributeDrawer).GetMethod("AssignType", STATIC);
            if (method == null) Assert.Inconclusive("InlineInstancedAttributeDrawer.AssignType not found.");

            _SerializedObject.Update();
            method.Invoke(null, new object[] { _SerializedObject, _Path, _Type });
        }

        private static Type ReadType(SerializedObject _SerializedObject, string _Path)
        {
            var method = typeof(InlineInstancedAttributeDrawer).GetMethod("GetReferenceState", STATIC);
            if (method == null) Assert.Inconclusive("InlineInstancedAttributeDrawer.GetReferenceState not found.");

            _SerializedObject.Update();
            var property = _SerializedObject.FindProperty(_Path);
            Assert.IsNotNull(property, $"No property at {_Path}");

            object state = method.Invoke(null, new object[] { property, true });
            return (Type)state.GetType().GetField("type", INSTANCE).GetValue(state);
        }

        /// <summary>
        /// The drawer Unity's inspector picks for a property, through its internal ScriptAttributeUtility.
        /// </summary>
        private static Type ResolvedDrawerType(SerializedProperty _Property)
        {
            var utility = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.ScriptAttributeUtility");
            var getHandler = utility?.GetMethod("GetHandler", STATIC, null, new[] { typeof(SerializedProperty) }, null);
            if (getHandler == null) Assert.Inconclusive("ScriptAttributeUtility.GetHandler(SerializedProperty) not found.");

            object handler = getHandler.Invoke(null, new object[] { _Property });
            var drawerProperty = handler?.GetType().GetProperty("propertyDrawer", INSTANCE);
            if (drawerProperty == null) Assert.Inconclusive("PropertyHandler.propertyDrawer not found.");

            return drawerProperty.GetValue(handler)?.GetType();
        }
        #endregion



        #region Drawer on nested fields
        [TestCase("root")]
        [TestCase("root.inner")]
        [TestCase("root.items.Array.data[0]")]
        [TestCase("root.inner.inner")]
        [TestCase("root.inner.items.Array.data[0]")]
        public void Inspector_UsesInlineInstancedDrawer_AtEveryDepth(string _Path)
        {
            var host = CreateHost(new SpikeBranch
            {
                inner = new SpikeBranch { items = { null } },
                items = { null },
            });
            using var serializedObject = new SerializedObject(host);
            var property = serializedObject.FindProperty(_Path);
            Assert.IsNotNull(property, $"No property at {_Path}");

            Assert.AreEqual(typeof(InlineInstancedAttributeDrawer), ResolvedDrawerType(property));
        }

        [Test]
        public void AssignType_OnNestedField_CreatesTheInstance()
        {
            var host = CreateHost(new SpikeBranch());
            using var serializedObject = new SerializedObject(host);

            AssignType(serializedObject, "root.inner", typeof(SpikeLeaf));

            Assert.IsInstanceOf<SpikeLeaf>(((SpikeBranch)host.root).inner);
            Assert.AreEqual(typeof(SpikeLeaf), ReadType(serializedObject, "root.inner"));
        }

        [Test]
        public void AssignType_OnNestedListElement_CreatesTheInstance()
        {
            var host = CreateHost(new SpikeBranch { items = { null } });
            using var serializedObject = new SerializedObject(host);

            AssignType(serializedObject, "root.items.Array.data[0]", typeof(SpikeLeaf));

            Assert.IsInstanceOf<SpikeLeaf>(((SpikeBranch)host.root).items[0]);
            Assert.AreEqual(typeof(SpikeLeaf), ReadType(serializedObject, "root.items.Array.data[0]"));
        }

        [Test]
        public void AssignType_TwoLevelsDeep_CreatesTheInstance_AndKeepsTheParents()
        {
            var middle = new SpikeBranch { value = 2 };
            var host = CreateHost(new SpikeBranch { value = 1, inner = middle });
            using var serializedObject = new SerializedObject(host);

            AssignType(serializedObject, "root.inner.inner", typeof(SpikeLeaf));

            var root = (SpikeBranch)host.root;
            Assert.AreEqual(1, root.value);
            Assert.AreEqual(2, root.inner.value);
            Assert.IsInstanceOf<SpikeLeaf>(((SpikeBranch)root.inner).inner);
        }
        #endregion



        #region Cloning with Object.Instantiate
        [Test]
        public void Instantiate_DeepCopiesTheNestedGraph()
        {
            var leaf = new SpikeLeaf { value = 3, number = 1.5f };
            var listed = new SpikeLeaf { value = 4 };
            var host = CreateHost(new SpikeBranch { value = 1, inner = leaf, items = { listed } });

            var copy = Object.Instantiate(host);
            created.Add(copy);

            var root = (SpikeBranch)copy.root;
            Assert.AreNotSame(host.root, root);
            Assert.AreNotSame(leaf, root.inner);
            Assert.AreNotSame(listed, root.items[0]);
            Assert.AreEqual(3, root.inner.value);
            Assert.AreEqual(1.5f, ((SpikeLeaf)root.inner).number);
            Assert.AreEqual(4, root.items[0].value);
        }

        [Test]
        public void Instantiate_KeepsReferencesSharedWithinTheGraph()
        {
            var shared = new SpikeLeaf { value = 5 };
            var host = CreateHost(new SpikeBranch { items = { shared, shared } });

            var copy = Object.Instantiate(host);
            created.Add(copy);

            var items = ((SpikeBranch)copy.root).items;
            Assert.AreSame(items[0], items[1]);
            Assert.AreNotSame(shared, items[0]);
        }

        [Test]
        public void Instantiate_LeavesNonSerializedFieldsAtTheirDefaults()
        {
            var leaf = new SpikeLeaf { runtimeCount = 7 };
            var host = CreateHost(new SpikeBranch { inner = leaf });

            var copy = Object.Instantiate(host);
            created.Add(copy);

            Assert.AreEqual(0, ((SpikeLeaf)((SpikeBranch)copy.root).inner).runtimeCount);
            Assert.AreEqual(7, leaf.runtimeCount);
        }
        #endregion
    }
}
