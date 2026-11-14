using System.IO;

using NUnit.Framework;

using UnityEditor;

using static LMK.Editor.AbilitySystem.Tests.AttributeIds;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// The serialized layout of <c>GameplayAttribute</c> is frozen: the checked-in fixture must load with its values and
    /// be written back byte for byte.
    /// </summary>
    public sealed class AttributeSerializationTests
    {
        private const string FIXTURE = "Packages/com.lmk.abilitysystem/Tests/Fixtures/AttributeSerializationFixture.asset";
        private const string ASSET_FOLDER = "Assets/__AttributeSerializationFixture";

        [TearDown]
        public void DeleteCopy()
        {
            if (AssetDatabase.IsValidFolder(ASSET_FOLDER))
                AssetDatabase.DeleteAsset(ASSET_FOLDER);
        }

        [Test]
        public void Fixture_IsWrittenBackByteForByte()
        {
            if (!AssetDatabase.IsValidFolder(ASSET_FOLDER))
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(ASSET_FOLDER));

            string copy = $"{ASSET_FOLDER}/{Path.GetFileName(FIXTURE)}";
            Assert.IsTrue(AssetDatabase.CopyAsset(FIXTURE, copy));
            AssetDatabase.ForceReserializeAssets(new[] { copy }, ForceReserializeAssetsOptions.ReserializeAssets);

            Assert.AreEqual(File.ReadAllText(FIXTURE), File.ReadAllText(copy));
            CollectionAssert.AreEqual(File.ReadAllBytes(FIXTURE), File.ReadAllBytes(copy));
        }

        [Test]
        public void Fixture_ReadsBackExactly()
        {
            var fixture = AssetDatabase.LoadAssetAtPath<AttributeSerializationFixture>(FIXTURE);
            Assert.IsNotNull(fixture, "the fixture failed to load");

            Assert.AreEqual(Main(HEALTH), fixture.single);
            Assert.AreEqual(MainRegistry, fixture.single.RegistryId);
            Assert.AreEqual(Id(HEALTH), fixture.single.Id);
            Assert.AreEqual("Vitals.Health", fixture.single.LastKnownName);

            Assert.AreEqual(3, fixture.list.Count);
            Assert.AreEqual(Main(MAX_HEALTH), fixture.list[0]);
            Assert.IsTrue(fixture.list[1].IsNone);
            Assert.AreEqual(string.Empty, fixture.list[1].LastKnownName);
            Assert.AreEqual("Meta.IncomingDamage", fixture.list[2].LastKnownName);

            using var serialized = new SerializedObject(fixture);
            Assert.AreEqual(MainRegistry.Hi, serialized.FindProperty("single._registry._hi").ulongValue);
            Assert.AreEqual(MainRegistry.Lo, serialized.FindProperty("single._registry._lo").ulongValue);
            Assert.AreEqual(Id(HEALTH).Hi, serialized.FindProperty("single._id._hi").ulongValue);
            Assert.AreEqual(Id(HEALTH).Lo, serialized.FindProperty("single._id._lo").ulongValue);
            Assert.AreEqual("Vitals.Health", serialized.FindProperty("single._lastKnownName").stringValue);
        }
    }
}
