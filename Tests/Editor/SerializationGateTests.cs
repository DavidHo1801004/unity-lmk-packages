using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;

using LMK.Runtime.GameplayTags;

using static LMK.Editor.GameplayTags.Tests.TestIds;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// Saved tag data must survive every version of the package unchanged. The text fixture was written by 1.0.0 and
    /// must be written back byte for byte; the binary fixture was written by 1.0.0 too, and must read back exactly.
    /// </summary>
    /// <remarks>
    /// A binary file also records the type names of its fields, so a re-saved binary file names the current types:
    /// only its values are compared.
    /// </remarks>
    public sealed class SerializationGateTests
    {
        private const string TEXT_FIXTURE = "Packages/com.lmk.gameplaytags/Tests/Fixtures/TagSerializationFixture.asset";
        private const string BINARY_FIXTURE = "Packages/com.lmk.gameplaytags/Tests/Fixtures/TagSerializationFixtureBinary.asset";
        private const string ASSET_FOLDER = "Assets/__GameplayTagSerializationGate";

        private static readonly ulong[] FIRE = { 4551706360823171711UL, 10100903309785135041UL };
        private static readonly ulong[] BURNING = { 17357386176853808775UL, 8676565436284608015UL };

        [TearDown]
        public void DeleteCopy()
        {
            if (AssetDatabase.IsValidFolder(ASSET_FOLDER))
                AssetDatabase.DeleteAsset(ASSET_FOLDER);
        }

        [Test]
        public void TextFixture_IsWrittenBackByteForByte()
        {
            // A copy in the project, under the same name, re-serialized by the current code.
            if (!AssetDatabase.IsValidFolder(ASSET_FOLDER))
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(ASSET_FOLDER));

            string copy = $"{ASSET_FOLDER}/{Path.GetFileName(TEXT_FIXTURE)}";
            Assert.IsTrue(AssetDatabase.CopyAsset(TEXT_FIXTURE, copy));
            AssetDatabase.ForceReserializeAssets(new[] { copy }, ForceReserializeAssetsOptions.ReserializeAssets);

            Assert.AreEqual(File.ReadAllText(TEXT_FIXTURE), File.ReadAllText(copy));
            CollectionAssert.AreEqual(File.ReadAllBytes(TEXT_FIXTURE), File.ReadAllBytes(copy));
        }

        [Test]
        public void BinaryFixtureOf10_ReadsBackExactly()
        {
            var fixture = AssetDatabase.LoadAssetAtPath<TagSerializationFixture>(BINARY_FIXTURE);
            Assert.IsNotNull(fixture, "the binary fixture failed to load");

            using var serialized = new SerializedObject(fixture);
            AssertTag(serialized, "single", MainRegistry.Hi, MainRegistry.Lo, FIRE, "Ability.Fire");
            Assert.AreEqual(3, serialized.FindProperty("container._tags").arraySize);
            AssertTag(serialized, "container._tags.Array.data[0]", MainRegistry.Hi, MainRegistry.Lo, BURNING, "Status.Burning");
            AssertTag(serialized, "container._tags.Array.data[1]", 0, 0, new ulong[] { 0, 0 }, string.Empty);
            AssertTag(serialized, "container._tags.Array.data[2]", MainRegistry.Hi, MainRegistry.Lo, FIRE, "Ability.Fire");

            var tags = new List<GameplayTag>();
            foreach (var tag in fixture.container)
                tags.Add(tag);

            Assert.AreEqual(2, tags.Count, "the None entry is not counted");
            Assert.AreEqual("Status.Burning", tags[0].LastKnownName);
        }

        private static void AssertTag(SerializedObject _Serialized, string _Path, ulong _RegistryHi, ulong _RegistryLo, ulong[] _Id, string _Name)
        {
            Assert.AreEqual(_RegistryHi, _Serialized.FindProperty($"{_Path}._registry._hi").ulongValue, _Path);
            Assert.AreEqual(_RegistryLo, _Serialized.FindProperty($"{_Path}._registry._lo").ulongValue, _Path);
            Assert.AreEqual(_Id[0], _Serialized.FindProperty($"{_Path}._id._hi").ulongValue, _Path);
            Assert.AreEqual(_Id[1], _Serialized.FindProperty($"{_Path}._id._lo").ulongValue, _Path);
            Assert.AreEqual(_Name, _Serialized.FindProperty($"{_Path}._lastKnownName").stringValue, _Path);
        }
    }
}
