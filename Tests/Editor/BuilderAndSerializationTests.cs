using System.Collections.Generic;

using NUnit.Framework;
using UnityEditor;

using LMK.Runtime.GameplayTags;

using static LMK.Editor.GameplayTags.Tests.TestIds;

namespace LMK.Editor.GameplayTags.Tests
{
    public sealed class BuilderTests : DatabaseTestBase
    {
        [Test]
        public void RowsWithMissingParentsOrCycles_AreSkipped()
        {
            var database = Track(new GameplayTagDatabaseBuilder().AddRegistry(MainRegistry, "Main", TagRegistryKind.Main, new[]
            {
                Row(1, 0, "Root"),
                Row(2, 1, "Child"),
                Row(3, 50, "Orphan"),     // parent 50 doesn't exist
                Row(4, 3, "OrphanChild"),  // under an orphan
                Row(5, 6, "LoopA"),        // 5 <-> 6 cycle
                Row(6, 5, "LoopB"),
            }).Build());

            Assert.AreEqual(2, database.TagCount);
            Assert.AreEqual(TagStatus.Live, database.Resolve(MainRegistry, Id(2)).Status);
            Assert.AreEqual(TagStatus.Deleted, database.Resolve(MainRegistry, Id(4)).Status);
            Assert.AreEqual(TagStatus.Deleted, database.Resolve(MainRegistry, Id(5)).Status);
        }

        [Test]
        public void Build_IsDeterministic_RegardlessOfInputOrder()
        {
            var rows = new List<GameplayTagDatabaseBuilder.Row>(MainRows());
            var reversed = new List<GameplayTagDatabaseBuilder.Row>(rows);
            reversed.Reverse();

            var a = Track(new GameplayTagDatabaseBuilder()
                .AddRegistry(MainRegistry, "Main", TagRegistryKind.Main, rows)
                .AddRegistry(PackageRegistry, "Pkg", TagRegistryKind.Package, new[] { Row(1, 0, "P") })
                .Build());
            var b = Track(new GameplayTagDatabaseBuilder()
                .AddRegistry(PackageRegistry, "Pkg", TagRegistryKind.Package, new[] { Row(1, 0, "P") })
                .AddRegistry(MainRegistry, "Main", TagRegistryKind.Main, reversed)
                .Build());

            Assert.AreEqual(a.ContentHash, b.ContentHash);
            Assert.IsNotEmpty(a.ContentHash);
        }

        [Test]
        public void ParentsAlwaysHaveLowerHandlesThanChildren()
        {
            var database = Track(MainBuilder().Build());

            for (int i = 0; i < database.TagCount; i++)
            {
                var parent = database.GetParent(new TagHandle(i));
                Assert.IsTrue(!parent.IsValid || parent.Index < i);
            }
        }
    }



    public sealed class SerializationTests : DatabaseTestBase
    {
        private const string FIXTURE_PATH = "Packages/com.lmk.gameplaytags/Tests/Fixtures/TagSerializationFixture.asset";

        private static readonly TagGuid FireId = TagGuid.Parse("3f2ae8d1b6c04a7f8c2d9e0b1a4f77c1");
        private static readonly TagGuid BurningId = TagGuid.Parse("f0e1d2c3b4a5968778695a4b3c2d1e0f");

        [Test]
        public void Fixture_ReadsBackExactly()
        {
            var fixture = AssetDatabase.LoadAssetAtPath<TagSerializationFixture>(FIXTURE_PATH);
            Assert.IsNotNull(fixture, "fixture asset failed to load");

            Assert.AreEqual(MainRegistry, fixture.single.RegistryId);
            Assert.AreEqual(FireId, fixture.single.Id);
            Assert.AreEqual("Ability.Fire", fixture.single.LastKnownName);

            var tags = new List<GameplayTag>();
            foreach (var tag in fixture.container)
                tags.Add(tag);

            Assert.AreEqual(2, fixture.container.Count, "the None entry is not counted");
            Assert.AreEqual(BurningId, tags[0].Id);
            Assert.AreEqual("Status.Burning", tags[0].LastKnownName);
            Assert.AreEqual(FireId, tags[1].Id);
        }

        [Test]
        public void Fixture_FieldLayoutIsFrozen()
        {
            var fixture = AssetDatabase.LoadAssetAtPath<TagSerializationFixture>(FIXTURE_PATH);
            using var serialized = new SerializedObject(fixture);

            // Any renamed or retyped field makes these lookups fail.
            Assert.AreEqual(MainRegistry.Hi, serialized.FindProperty("single._registry._hi").ulongValue);
            Assert.AreEqual(MainRegistry.Lo, serialized.FindProperty("single._registry._lo").ulongValue);
            Assert.AreEqual(FireId.Hi, serialized.FindProperty("single._id._hi").ulongValue);
            Assert.AreEqual(FireId.Lo, serialized.FindProperty("single._id._lo").ulongValue);
            Assert.AreEqual("Ability.Fire", serialized.FindProperty("single._lastKnownName").stringValue);
            Assert.AreEqual(3, serialized.FindProperty("container._tags").arraySize);
        }
    }
}
