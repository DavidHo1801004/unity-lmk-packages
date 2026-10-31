using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace LMK.Editor.Attributes.Tests
{
    public class InlineInstancedDrawerTests
    {
        private readonly List<Object> created = new();

        private TestHost CreateHost()
        {
            var host = ScriptableObject.CreateInstance<TestHost>();
            created.Add(host);
            return host;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var target in created)
            {
                if (target != null)
                    Object.DestroyImmediate(target);
            }
            created.Clear();
            TestThrowsOnSecond.Created = 0;
        }

        private static InlineInstancedAttributeDrawer.ReferenceState FreshState(SerializedObject _SerializedObject, string _Path)
        {
            _SerializedObject.Update();
            return InlineInstancedAttributeDrawer.GetReferenceState(_SerializedObject.FindProperty(_Path), true);
        }

        private static void Duplicate(SerializedObject _SerializedObject, int _Index)
        {
            _SerializedObject.Update();
            _SerializedObject.FindProperty("list").GetArrayElementAtIndex(_Index).DuplicateCommand();
            _SerializedObject.ApplyModifiedProperties();
            _SerializedObject.Update();
        }

        #region Assign
        [Test]
        public void AssignType_SameType_KeepsInstanceAndValues()
        {
            var host = CreateHost();
            var instance = new TestLeafA { value = 42 };
            host.single = instance;

            using var serializedObject = new SerializedObject(host);
            InlineInstancedAttributeDrawer.AssignType(serializedObject, "single", typeof(TestLeafA));

            Assert.That(host.single, Is.SameAs(instance));
            Assert.That(host.single.value, Is.EqualTo(42));
        }

        [Test]
        public void AssignType_MultiTarget_ChangesOnlyTargetsOfOtherTypes()
        {
            var first = CreateHost();
            var second = CreateHost();
            var kept = new TestLeafA { value = 42 };
            first.single = kept;
            second.single = new TestLeafB();

            using var serializedObject = new SerializedObject(new Object[] { first, second });
            InlineInstancedAttributeDrawer.AssignType(serializedObject, "single", typeof(TestLeafA));

            Assert.That(first.single, Is.SameAs(kept));
            Assert.That(second.single, Is.TypeOf<TestLeafA>());
        }

        [Test]
        public void AssignType_MultiTarget_GivesEachTargetItsOwnInstance()
        {
            var first = CreateHost();
            var second = CreateHost();

            using var serializedObject = new SerializedObject(new Object[] { first, second });
            InlineInstancedAttributeDrawer.AssignType(serializedObject, "single", typeof(TestLeafA));

            Assert.That(first.single, Is.TypeOf<TestLeafA>());
            Assert.That(second.single, Is.TypeOf<TestLeafA>());
            Assert.That(first.single, Is.Not.SameAs(second.single));
        }

        [Test]
        public void AssignType_NullOnEmptyField_RecordsNoUndo()
        {
            var host = CreateHost();
            using var serializedObject = new SerializedObject(host);

            int groupBefore = Undo.GetCurrentGroup();
            InlineInstancedAttributeDrawer.AssignType(serializedObject, "single", null);

            Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(groupBefore));
        }

        [Test]
        public void AssignType_ThrowingConstructor_WritesNothingAndDoesNotThrow()
        {
            var host = CreateHost();
            var instance = new TestLeafA();
            host.single = instance;

            LogAssert.Expect(LogType.Exception, new Regex("Constructor failure expected"));
            using var serializedObject = new SerializedObject(host);
            InlineInstancedAttributeDrawer.AssignType(serializedObject, "single", typeof(TestThrows));

            Assert.That(host.single, Is.SameAs(instance));
        }

        [Test]
        public void AssignType_ThrowingConstructorOnSecondTarget_LeavesFirstTargetUnchanged()
        {
            var first = CreateHost();
            var second = CreateHost();
            var firstInstance = new TestLeafA();
            first.single = firstInstance;
            second.single = new TestLeafB();

            LogAssert.Expect(LogType.Exception, new Regex("Constructor failure expected"));
            using var serializedObject = new SerializedObject(new Object[] { first, second });
            InlineInstancedAttributeDrawer.AssignType(serializedObject, "single", typeof(TestThrowsOnSecond));

            Assert.That(first.single, Is.SameAs(firstInstance));
            Assert.That(second.single, Is.TypeOf<TestLeafB>());
        }
        #endregion

        #region Shared instances
        [Test]
        public void DuplicatedElement_IsReportedAsShared_OnTheLaterRowOnly()
        {
            var host = CreateHost();
            host.list.Add(new TestLeafA());
            using var serializedObject = new SerializedObject(host);

            Duplicate(serializedObject, 0);

            Assume.That(host.list[1], Is.SameAs(host.list[0]));
            Assert.That(FreshState(serializedObject, "list.Array.data[0]").isShared, Is.False);
            Assert.That(FreshState(serializedObject, "list.Array.data[1]").isShared, Is.True);
        }

        [Test]
        public void SharedElement_IsFoundAcrossOtherElements()
        {
            var host = CreateHost();
            host.list.Add(new TestLeafA());
            host.list.Add(new TestLeafB());
            host.list.Add(null);
            using var serializedObject = new SerializedObject(host);

            serializedObject.Update();
            var list = serializedObject.FindProperty("list");
            list.GetArrayElementAtIndex(2).managedReferenceValue = list.GetArrayElementAtIndex(0).managedReferenceValue;
            serializedObject.ApplyModifiedProperties();

            Assert.That(FreshState(serializedObject, "list.Array.data[1]").isShared, Is.False);
            Assert.That(FreshState(serializedObject, "list.Array.data[2]").isShared, Is.True);
        }

        [Test]
        public void EmptyElementsAndPlainFields_AreNotShared()
        {
            var host = CreateHost();
            host.single = new TestLeafA();
            host.list.Add(null);
            host.list.Add(null);
            using var serializedObject = new SerializedObject(host);

            Assert.That(FreshState(serializedObject, "single").isShared, Is.False);
            Assert.That(FreshState(serializedObject, "list.Array.data[1]").isShared, Is.False);
        }

        [Test]
        public void GuiEventState_SkipsSharedCheck()
        {
            var host = CreateHost();
            host.list.Add(new TestLeafA());
            using var serializedObject = new SerializedObject(host);
            Duplicate(serializedObject, 0);

            var property = serializedObject.FindProperty("list").GetArrayElementAtIndex(1);

            Assert.That(InlineInstancedAttributeDrawer.GetReferenceState(property, false).isShared, Is.False);
        }

        [Test]
        public void MakeUnique_CreatesAnIndependentCopyWithTheSameValues()
        {
            var host = CreateHost();
            host.list.Add(new TestLeafA { value = 7, number = 1.5f });
            using var serializedObject = new SerializedObject(host);
            Duplicate(serializedObject, 0);

            InlineInstancedAttributeDrawer.MakeUnique(serializedObject, "list.Array.data[1]");

            Assert.That(host.list[1], Is.Not.SameAs(host.list[0]));
            Assert.That(host.list[1], Is.TypeOf<TestLeafA>());
            Assert.That(host.list[1].value, Is.EqualTo(7));
            Assert.That(((TestLeafA)host.list[1]).number, Is.EqualTo(1.5f));
            Assert.That(FreshState(serializedObject, "list.Array.data[1]").isShared, Is.False);
        }

        [Test]
        public void MakeUnique_CopiesNestedReferences()
        {
            var host = CreateHost();
            var node = new TestNode { value = 1, child = new TestLeafA { value = 2 } };
            node.children.Add(new TestLeafB { value = 3 });
            host.list.Add(node);
            using var serializedObject = new SerializedObject(host);
            Duplicate(serializedObject, 0);

            InlineInstancedAttributeDrawer.MakeUnique(serializedObject, "list.Array.data[1]");

            var copy = (TestNode)host.list[1];
            Assert.That(copy.child, Is.TypeOf<TestLeafA>());
            Assert.That(copy.child.value, Is.EqualTo(2));
            Assert.That(copy.child, Is.Not.SameAs(node.child));
            Assert.That(copy.children, Has.Count.EqualTo(1));
            Assert.That(copy.children[0].value, Is.EqualTo(3));
            Assert.That(copy.children[0], Is.Not.SameAs(node.children[0]));
        }

        [Test]
        public void MakeUnique_CanBeUndone()
        {
            var host = CreateHost();
            host.list.Add(new TestLeafA());
            using var serializedObject = new SerializedObject(host);
            Duplicate(serializedObject, 0);

            InlineInstancedAttributeDrawer.MakeUnique(serializedObject, "list.Array.data[1]");
            Assume.That(host.list[1], Is.Not.SameAs(host.list[0]));

            Undo.PerformUndo();

            Assert.That(FreshState(serializedObject, "list.Array.data[1]").isShared, Is.True);
        }
        #endregion

        #region Multi-target state
        [Test]
        public void FreshState_SeesScriptWriteWithoutSetDirty()
        {
            var first = CreateHost();
            var second = CreateHost();
            first.single = new TestLeafA();
            second.single = new TestLeafA();

            using var serializedObject = new SerializedObject(new Object[] { first, second });
            var property = serializedObject.FindProperty("single");
            Assume.That(InlineInstancedAttributeDrawer.GetReferenceState(property, false).isMixed, Is.False);

            first.single = new TestLeafB();

            Assert.That(InlineInstancedAttributeDrawer.GetReferenceState(property, true).isMixed, Is.True);
        }

        [Test]
        public void CachedState_SeesChangesThatMarkTheTargetDirty()
        {
            var first = CreateHost();
            var second = CreateHost();
            first.single = new TestLeafA();
            second.single = new TestLeafA();

            using var serializedObject = new SerializedObject(new Object[] { first, second });
            var property = serializedObject.FindProperty("single");
            Assume.That(InlineInstancedAttributeDrawer.GetReferenceState(property, false).isMixed, Is.False);

            first.single = new TestLeafB();
            EditorUtility.SetDirty(first);

            Assert.That(InlineInstancedAttributeDrawer.GetReferenceState(property, false).isMixed, Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DestroyedTarget_ReadsAsMixed(bool _Fresh)
        {
            var first = CreateHost();
            var second = CreateHost();
            first.single = new TestLeafA();
            second.single = new TestLeafA();

            using var serializedObject = new SerializedObject(new Object[] { first, second });
            var property = serializedObject.FindProperty("single");
            Object.DestroyImmediate(second);

            Assert.That(InlineInstancedAttributeDrawer.GetReferenceState(property, _Fresh).isMixed, Is.True);
        }
        #endregion

        #region Missing references
        [Test]
        public void MissingReferencesMessage_IsNullWithoutMissingTypes()
        {
            var host = CreateHost();
            host.single = new TestLeafA();
            using var serializedObject = new SerializedObject(host);

            Assert.That(InlineInstancedAttributeDrawer.GetMissingReferencesMessage(serializedObject), Is.Null);
        }
        #endregion
    }
}
