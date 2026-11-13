using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using UnityEditor;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry.Tests
{
    public sealed class ScannerTests : KindTestBase
    {
        private static List<RegistryReference> Scan(string _Path)
        {
            var result = new List<RegistryReference>();
            Assert.IsTrue(RegistryAssetScanner.Scan(_Path, result));
            return result;
        }

        [Test]
        public void Walker_FindsEveryShape_WithItsKind()
        {
            var holder = UnityEngine.ScriptableObject.CreateInstance<ReferenceHolder>();
            try
            {
                using var serializedObject = new SerializedObject(holder);
                var found = RegistryPropertyWalker.Enumerate(serializedObject).Select(e => (e.Property.propertyPath, e.Kind)).ToList();

                CollectionAssert.Contains(found, ("single", (RegistryKind)a));
                CollectionAssert.Contains(found, ("other", (RegistryKind)b));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void TextScan_PlacesEachReference_ByItsKindsRegistry()
        {
            var mainA = a.Snapshot.Main.Id;
            var mainB = b.Snapshot.Main.Id;
            string path = CreateHolder(CreateFolder("Scan"), "Holder", holder =>
            {
                holder.single = new TreeRef(mainA, Id(1), "Fire");
                holder.list.Add(new TreeRef(mainA, Id(2), "Ice"));
                holder.entries.Add(new TreeRefEntry { reference = new TreeRef(mainA, Id(3), "Wind") });
                holder.managed = new TreeRefManaged { reference = new TreeRef(mainA, Id(4), "Earth") };
                holder.other = new TreeRefB(mainB, Id(5), "Stun");
            });

            StringAssert.StartsWith("%YAML", File.ReadAllText(path));
            var references = Scan(path);

            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4 }.Select(Id), references.Where(e => e.Kind == a).Select(e => e.Id));
            Assert.AreEqual(Id(5), references.Single(e => e.Kind == b).Id);
            Assert.AreEqual("Fire", references.Single(e => e.Id == Id(1)).Name);
        }

        [Test]
        public void UnknownRegistries_WithSeveralKinds_AreReadFromTheLoadedAsset()
        {
            var unknown = RegistryGuid.NewGuid();
            string path = CreateHolder(CreateFolder("Scan"), "Holder", holder =>
            {
                holder.single = new TreeRef(unknown, Id(1), "Fire");
                holder.other = new TreeRefB(unknown, Id(2), "Stun");
            });

            var references = Scan(path);

            Assert.AreEqual(a, references.Single(e => e.Id == Id(1)).Kind);
            Assert.AreEqual(b, references.Single(e => e.Id == Id(2)).Kind);
        }

        [Test]
        public void WithOneKind_EveryTextReferenceIsThatKinds()
        {
            RegistryKinds.Unregister(b);
            string path = CreateHolder(CreateFolder("Scan"), "Holder", holder => holder.single = new TreeRef(RegistryGuid.NewGuid(), Id(1), "Fire"));

            Assert.AreEqual(a, Scan(path).Single().Kind);
        }

        [Test]
        public void NoneReferences_AreSkipped()
        {
            string path = CreateHolder(CreateFolder("Scan"), "Holder", _ => { });

            CollectionAssert.IsEmpty(Scan(path));
        }
    }



    public sealed class PackageTests : KindTestBase
    {
        private const string VENDOR = "com.vendor.combat";

        private static readonly RegistryGuid VendorTags = RegistryGuid.Parse("b0000000000000000000000000000001");
        private static readonly RegistryGuid VendorEffects = RegistryGuid.Parse("b0000000000000000000000000000002");

        private string installedVersion;

        [SetUp]
        public void FakePackages()
        {
            installedVersion = "1.0.0";
            RegistryPackages.InstalledPackagesOverride = () => new Dictionary<string, string> { [VENDOR] = installedVersion };
        }

        private RegistryPackage Vendor() => new()
        {
            Name = VENDOR,
            Version = installedVersion,
            DisplayName = "Vendor Combat",
            Root = CreateFolder("VendorCombat"),
        };

        /// <summary>
        /// The vendor package references two registries it doesn't ship, one per kind.
        /// </summary>
        private string CreateVendorAssets(string _Root)
        {
            return CreateHolder(_Root, "Abilities", holder =>
            {
                holder.single = new TreeRef(VendorTags, Id(1), "Combat.Fire");
                holder.list.Add(new TreeRef(VendorTags, Id(2), "Combat.Ice"));
                holder.other = new TreeRefB(VendorEffects, Id(3), "Burn");
            });
        }

        private static Dictionary<string, string> ReadAll(string _Folder)
            => Directory.GetFiles(_Folder, "*", SearchOption.AllDirectories).ToDictionary(e => e, File.ReadAllText);



        #region Generate
        [Test]
        public void Generate_MakesOneRegistryPerKindAndId_WithTheReferencedIds()
        {
            var vendor = Vendor();
            string holder = CreateVendorAssets(vendor.Root);
            var before = ReadAll(vendor.Root);

            var result = RegistryPackageService.Generate(vendor, false);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(2, result.Written.Count, result.Message);
            CollectionAssert.AreEquivalent(before, ReadAll(vendor.Root), "the package is not changed");

            var tags = a.Snapshot.Find(VendorTags);
            Assert.IsNotNull(tags);
            Assert.AreEqual(RegistryOrigin.Generated, tags.Origin);
            Assert.AreEqual(VENDOR, tags.Data.package);
            Assert.AreEqual("Combat.Fire", tags.GetName(Id(1)));
            Assert.AreEqual("Combat.Ice", tags.GetName(Id(2)));
            Assert.IsTrue(tags.TryFind("Combat", out _), "the parent is added implicitly");

            var effects = b.Snapshot.Find(VendorEffects);
            Assert.IsNotNull(effects);
            Assert.AreEqual("Burn", effects.GetName(Id(3)));
            Assert.IsNull(a.Snapshot.Find(VendorEffects), "each registry belongs to one kind");

            StringAssert.EndsWith("." + a.FileExtension, tags.AssetPath);
            StringAssert.EndsWith("." + b.FileExtension, effects.AssetPath);
            Assert.AreEqual(1, a.savedCount, "generated files follow the registries");
        }

        [Test]
        public void Generate_NothingToDo_WhenEveryReferenceResolves()
        {
            var vendor = Vendor();
            var mainA = a.Snapshot.Main.Id;
            CreateHolder(vendor.Root, "Holder", holder => holder.single = new TreeRef(mainA, Id(1), "Fire"));

            var result = RegistryPackageService.Generate(vendor, false);

            Assert.IsTrue(result.Success);
            Assert.IsEmpty(result.Written);
            StringAssert.Contains("needs no generated registry", result.Message);
        }

        [Test]
        public void PackageUpdate_OnlyPrompts_AndRegenerateKeepsOrRemovesRows()
        {
            var vendor = Vendor();
            string holder = CreateVendorAssets(vendor.Root);
            RegistryPackageService.Generate(vendor, false);
            string generated = a.Snapshot.Find(VendorTags).AssetPath;
            string text = File.ReadAllText(generated);

            installedVersion = "1.1.0";
            var prompts = RegistryPackageService.OnPackagesChanged(new[] { VENDOR });

            Assert.AreEqual(2, prompts.Count, "one per kind");
            Assert.IsTrue(prompts.All(e => e.Contains("1.1.0") && e.Contains("1.0.0")));
            Assert.AreEqual(text, File.ReadAllText(generated), "package events write nothing");

            // The package drops its Ice reference.
            var asset = AssetDatabase.LoadAssetAtPath<ReferenceHolder>(holder);
            asset.list.Clear();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            RegistryPackageService.Generate(Vendor(), false);
            Assert.IsTrue(a.Snapshot.Find(VendorTags).TryFind("Combat.Ice", out _), "rows are kept by default");
            Assert.AreEqual("1.1.0", a.Snapshot.Find(VendorTags).Data.version);

            RegistryPackageService.Generate(Vendor(), true);
            var tags = a.Snapshot.Find(VendorTags);
            Assert.IsFalse(tags.TryFind("Combat.Ice", out _));
            Assert.IsTrue(tags.TryFind("Combat.Fire", out _));
            Assert.IsTrue(tags.TryFind("Combat", out _), "parents of kept rows stay");
            Assert.AreEqual(generated, tags.AssetPath, "regenerated in place");
        }

        [Test]
        public void Generate_AsksTheLockOnce_AndIsRefusedWithoutIt()
        {
            var vendor = Vendor();
            CreateVendorAssets(vendor.Root);
            var provider = new RecordingLockProvider { refuse = true };
            RegistryLockProvider.Current = provider;

            var result = RegistryPackageService.Generate(vendor, false);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("Held by someone else.", result.Error);
            Assert.AreEqual(1, provider.requests.Count);
            Assert.AreEqual(2, provider.requests[0].Count, "both kinds' files in one request");
            Assert.IsFalse(AssetDatabase.IsValidFolder(a.GeneratedRegistryFolder));
        }

        [Test]
        public void InvalidReferencedName_IsReported_AndSkipped()
        {
            var vendor = Vendor();
            CreateHolder(vendor.Root, "Holder", holder =>
            {
                holder.single = new TreeRef(VendorTags, Id(1), "Bad-Name");
                holder.list.Add(new TreeRef(VendorTags, Id(2), "Good"));
            });

            var result = RegistryPackageService.Generate(vendor, false);

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsTrue(result.Warnings.Any(e => e.Contains("Bad-Name")));
            Assert.IsFalse(a.Snapshot.Find(VendorTags).Contains(Id(1)));
            Assert.IsTrue(a.Snapshot.Find(VendorTags).Contains(Id(2)));
        }
        #endregion



        #region Pack Validation
        private static readonly RegistryGuid OwnTags = RegistryGuid.Parse("c0000000000000000000000000000001");

        private RegistryPackage Authored()
        {
            var package = new RegistryPackage
            {
                Name = "com.me.abilities",
                Version = "1.0.0",
                DisplayName = "My Abilities",
                Root = CreateFolder("MyAbilities"),
            };

            WriteRegistry(a, $"{package.Root}/Tags.{a.FileExtension}", NewRegistry(OwnTags, RegistryOrigin.Package, (10, 0, "Fire")));
            return package;
        }

        [Test]
        public void PackValidation_PassesForOwnRows()
        {
            var package = Authored();
            CreateHolder(package.Root, "Holder", holder => holder.single = new TreeRef(OwnTags, Id(10), "Fire"));

            CollectionAssert.IsEmpty(RegistryPackageService.Validate(package));
        }

        [Test]
        public void PackValidation_FailsOnTheMainRegistry_NamingTheKindAndAsset()
        {
            var package = Authored();
            var mainA = a.Snapshot.Main.Id;
            string holder = CreateHolder(package.Root, "Holder", h => h.single = new TreeRef(mainA, Id(1), "Local"));

            var errors = RegistryPackageService.Validate(package);

            Assert.IsTrue(errors.Any(e => e.Contains(a.DisplayName) && e.Contains(holder) && e.Contains("main registry")),
                string.Join("\n", errors));
        }

        [Test]
        public void PackValidation_KindWithoutPackageRegistry_UnknownIdAndChangedId()
        {
            var package = Authored();
            CreateHolder(package.Root, "Holder", holder =>
            {
                holder.single = new TreeRef(OwnTags, Id(99), "Missing");
                holder.other = new TreeRefB(RegistryGuid.NewGuid(), Id(1), "Stun");
            });
            RegistrySettings.instance.SetPublishedRegistry(package.Name, a.Id, RegistryGuid.NewGuid(), false);

            try
            {
                var errors = RegistryPackageService.Validate(package);

                Assert.IsTrue(errors.Any(e => e.Contains(a.DisplayName) && e.Contains("doesn't exist there")), string.Join("\n", errors));
                Assert.IsTrue(errors.Any(e => e.Contains(b.DisplayName) && e.Contains("contains no registry of kind \"package\"")), string.Join("\n", errors));
                Assert.IsTrue(errors.Any(e => e.Contains(a.DisplayName) && e.Contains("registry ID changed")), string.Join("\n", errors));
            }
            finally
            {
                RegistrySettings.instance.SetPublishedRegistry(package.Name, a.Id, RegistryGuid.Zero, false);
            }
        }

        [Test]
        public void PackValidation_RejectsAHandEditedRegistry()
        {
            var package = Authored();
            CreateHolder(package.Root, "Holder", holder => holder.single = new TreeRef(OwnTags, Id(10), "Fire"));

            string path = $"{package.Root}/Tags.{a.FileExtension}";
            File.WriteAllText(path, File.ReadAllText(path).Replace("| Fire", "| Fyre"));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var errors = RegistryPackageService.Validate(package);

            Assert.IsTrue(errors.Any(e => e.Contains("no valid checksum")), string.Join("\n", errors));
        }
        #endregion
    }
}
