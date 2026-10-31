using System.Linq;

using NUnit.Framework;

namespace LMK.Editor.Attributes.Tests
{
    public class InlineInstancedCacheTests
    {
        [Test]
        public void IsExposed_ClosestAttributeWins()
        {
            Assert.That(InlineInstancedReflectionCache.IsExposed(typeof(TestLeafA)), Is.True);
            Assert.That(InlineInstancedReflectionCache.IsExposed(typeof(TestHidden)), Is.False);
            Assert.That(InlineInstancedReflectionCache.IsExposed(typeof(TestHiddenChild)), Is.False);
            Assert.That(InlineInstancedReflectionCache.IsExposed(typeof(TestReexposed)), Is.True);
        }

        [Test]
        public void IsCreatable_AcceptsPlainSerializableClasses()
        {
            Assert.That(InlineInstancedReflectionCache.IsCreatable(typeof(TestLeafA)), Is.True);
        }

        [Test]
        public void IsCreatable_RejectsTypesUnityCanNotCreate()
        {
            Assert.That(InlineInstancedReflectionCache.IsCreatable(typeof(TestBase)), Is.False, "abstract");
            Assert.That(InlineInstancedReflectionCache.IsCreatable(typeof(ITestContract)), Is.False, "interface");
            Assert.That(InlineInstancedReflectionCache.IsCreatable(typeof(TestNotSerializable)), Is.False, "not serializable");
            Assert.That(InlineInstancedReflectionCache.IsCreatable(typeof(TestNoDefaultConstructor)), Is.False, "no constructor");
            Assert.That(InlineInstancedReflectionCache.IsCreatable(typeof(TestHost)), Is.False, "UnityEngine.Object");
        }

        // The test assembly is editor-only, so a class being rejected here shows that filter works.
        [Test]
        public void IsInstantiable_RejectsCreatableClassesFromEditorOnlyAssemblies()
        {
            Assume.That(InlineInstancedReflectionCache.IsCreatable(typeof(TestLeafA)), Is.True);

            Assert.That(InlineInstancedReflectionCache.IsInstantiable(typeof(TestLeafA)), Is.False);
        }

        [Test]
        public void Candidates_ExcludeEditorOnlyAssemblies()
        {
            var testAssembly = typeof(TestLeafA).Assembly;

            Assert.That(InlineInstancedReflectionCache.GetExposedCandidates().Any(e => e.Assembly == testAssembly), Is.False);
        }

        [Test]
        public void Get_NeverReturnsADefaultStruct()
        {
            var data = InlineInstancedReflectionCache.Get(null);

            Assert.That(data.isValid, Is.False);
            Assert.That(data.exposedTypes, Is.Empty);
            Assert.That(data.menuItems, Has.Length.EqualTo(1), "None is always listed");
        }
    }
}
