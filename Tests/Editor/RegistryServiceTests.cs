using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

using LMK.Runtime.GameplayTags;

using static LMK.Editor.GameplayTags.Tests.TestIds;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// Registry files on disk, through the importer, the postprocessor and the service.
    /// </summary>
    public sealed class RegistryServiceTests
    {
        private const string FOLDER = "Assets/__GameplayTagTests";
        private const string PACKAGE_PATH = FOLDER + "/Vendor.tagregistry";

        private static readonly TagGuid VendorRegistry = TagGuid.Parse("b0000000000000000000000000000002");

        [SetUp]
        public void CreateFolder()
        {
            if (!AssetDatabase.IsValidFolder(FOLDER))
                AssetDatabase.CreateFolder("Assets", "__GameplayTagTests");
        }

        [TearDown]
        public void DeleteFolder()
        {
            AssetDatabase.DeleteAsset(FOLDER);
            TagRegistryService.Reload();
        }

        private static TagRegistryData VendorData(string _StunSegment = "Stun") => new()
        {
            registryId = VendorRegistry,
            name = "Vendor",
            kind = TagRegistryKind.Package,
            rows = new List<TagRegistryRow>
            {
                new(Id(1), TagGuid.Zero, "Vendor", true),
                new(Id(2), Id(1), _StunSegment, false, "Stun from the vendor"),
            },
        };

        internal static void WriteAndImport(string _Path, string _Text)
        {
            File.WriteAllText(_Path, _Text);
            AssetDatabase.ImportAsset(_Path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            TagRegistryService.Reload();
        }

        private static GameplayTag VendorStun => GameplayTag.FromId(VendorRegistry, Id(2), "Vendor.Stun");



        [Test]
        public void ImportedRegistry_IsLoadedAndResolves()
        {
            WriteAndImport(PACKAGE_PATH, TagRegistryFile.Write(VendorData()));

            var registry = TagRegistryService.Snapshot.Find(VendorRegistry);
            Assert.IsNotNull(registry);
            Assert.IsTrue(registry.IsReadOnly, "only the main registry is editable");
            Assert.AreEqual("Vendor.Stun", registry.GetPath(Id(2)));

            Assert.AreEqual(TagStatus.Live, VendorStun.Resolve().Status);
            Assert.AreEqual("Vendor.Stun", VendorStun.Path);
        }

        [Test]
        public void RenamedRow_ReferenceFollows()
        {
            WriteAndImport(PACKAGE_PATH, TagRegistryFile.Write(VendorData()));
            WriteAndImport(PACKAGE_PATH, TagRegistryFile.Write(VendorData("Stunned")));

            Assert.AreEqual(TagStatus.Live, VendorStun.Resolve().Status);
            Assert.AreEqual("Vendor.Stunned", VendorStun.Path);
        }

        [Test]
        public void DeletedRow_IsDeleted_EvenIfTheNameExistsAgain()
        {
            WriteAndImport(PACKAGE_PATH, TagRegistryFile.Write(VendorData()));

            // Same path "Vendor.Stun", new ID.
            var data = VendorData();
            data.rows[1] = new TagRegistryRow(Id(3), Id(1), "Stun", false);
            WriteAndImport(PACKAGE_PATH, TagRegistryFile.Write(data));

            Assert.AreEqual(TagStatus.Deleted, VendorStun.Resolve().Status);
        }

        [Test]
        public void RemovedRegistry_IsMissing_AndComesBackUnchanged()
        {
            string text = TagRegistryFile.Write(VendorData());
            WriteAndImport(PACKAGE_PATH, text);

            AssetDatabase.DeleteAsset(PACKAGE_PATH);
            TagRegistryService.Reload();
            Assert.AreEqual(TagStatus.MissingRegistry, VendorStun.Resolve().Status);

            WriteAndImport(PACKAGE_PATH, text);
            Assert.AreEqual(TagStatus.Live, VendorStun.Resolve().Status);
            Assert.AreEqual(text, File.ReadAllText(PACKAGE_PATH), "loading never writes the file");
        }

        [Test]
        public void RejectedFile_KeepsLastGoodVersion()
        {
            WriteAndImport(PACKAGE_PATH, TagRegistryFile.Write(VendorData()));

            LogAssert.ignoreFailingMessages = true;
            var broken = VendorData();
            broken.rows.Add(new TagRegistryRow(Id(9), Id(50), "Orphan", false));
            WriteAndImport(PACKAGE_PATH, TagRegistryFile.Write(broken));

            var registry = TagRegistryService.Snapshot.Find(VendorRegistry);
            Assert.IsNotNull(registry);
            Assert.IsTrue(registry.UsingLastGood);
            Assert.IsTrue(registry.Errors.Any(e => e.Contains("does not exist")));
            Assert.AreEqual(TagStatus.Live, VendorStun.Resolve().Status);
        }

        [Test]
        public void DuplicateRegistryIds_FirstFileWins()
        {
            WriteAndImport(PACKAGE_PATH, TagRegistryFile.Write(VendorData()));
            WriteAndImport(FOLDER + "/Vendor2.tagregistry", TagRegistryFile.Write(VendorData("Other")));

            var snapshot = TagRegistryService.Snapshot;

            Assert.AreEqual(1, snapshot.Registries.Count(e => e.Id.Equals(VendorRegistry)));
            Assert.AreEqual(PACKAGE_PATH, snapshot.Find(VendorRegistry).AssetPath);
            Assert.IsTrue(snapshot.Issues.Any(e => e.Contains("same registry ID")));
        }

        [Test]
        public void SnapshotTree_GroupsTagsByRegistry()
        {
            WriteAndImport(PACKAGE_PATH, TagRegistryFile.Write(VendorData()));

            var header = TagRegistryService.Snapshot.Root.Children.First(e => e.RegistryId.Equals(VendorRegistry));

            Assert.IsTrue(header.IsRegistryHeader);
            Assert.AreEqual("Vendor", header.ShortTag);
            Assert.AreEqual("Vendor", header.Children[0].FullTag);
            Assert.IsFalse(header.Children[0].IsExplicit);
            Assert.AreEqual("Vendor.Stun", header.Children[0].Children[0].FullTag);
        }
    }
}
