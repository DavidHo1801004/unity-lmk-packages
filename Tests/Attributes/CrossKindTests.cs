using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using LMK.AbilitySystem.Tests;
using LMK.Editor.Registry;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

using Object = UnityEngine.Object;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// The registry acceptance criteria that need two real kinds: gameplay tags and gameplay attributes on one registry
    /// core.
    /// </summary>
    public sealed class CrossKindTests : CrossKindTestBase
    {
        private const string VENDOR = "com.vendor.combat";
        private const string VENDOR_BASE = "com.vendor.base";

        private static Dictionary<string, string> ReadAll(string _Folder)
            => Directory.GetFiles(_Folder, "*", SearchOption.AllDirectories).ToDictionary(e => e, File.ReadAllText);



        #region AC-3 Identity
        [Test]
        public void AC3_SameIdsInATagAndAnAttributeRegistry_AreUnrelatedRows()
        {
            var registry = RegistryGuid.Parse("f0000000000000000000000000000001");
            var row = RegistryGuid.Parse("f0000000000000000000000000000002");
            var tagOnly = RegistryGuid.Parse("f0000000000000000000000000000003");

            var tags = new GameplayTagDatabaseBuilder().AddRegistry(registry, "Shared", TagRegistryKind.Main, new[]
            {
                new GameplayTagDatabaseBuilder.Row(row, RegistryGuid.Zero, "Stun"),
                new GameplayTagDatabaseBuilder.Row(tagOnly, RegistryGuid.Zero, "Burning"),
            }).Build();
            var attributes = new GameplayAttributeDatabaseBuilder().AddRegistry(registry, "Shared", RegistryOrigin.Main, new[]
            {
                new GameplayAttributeDatabaseBuilder.Row(row, "Vitals.Health", 100f),
            }).Build();
            var previous = GameplayAttributeDatabase.Active;

            try
            {
                Assert.AreEqual("Stun", tags.GetPath(tags.Resolve(registry, row).Handle));
                Assert.AreEqual("Vitals.Health", attributes.GetName(attributes.Resolve(registry, row).Handle));
                Assert.AreEqual(AttributeStatus.Deleted, attributes.Resolve(registry, tagOnly).Status, "a tag's ID is no attribute");

                // Each reference resolves against its own kind's active database only.
                GameplayAttributeDatabase.SetActive(attributes);
                Assert.IsTrue(GameplayAttribute.FromId(registry, row).IsLive);
                Assert.AreEqual(TagStatus.MissingRegistry, GameplayTag.FromId(registry, row).Resolve().Status);
            }
            finally
            {
                GameplayAttributeDatabase.SetActive(previous);
                Object.DestroyImmediate(tags);
                Object.DestroyImmediate(attributes);
            }

            // The same through the editor kinds.
            var tag = AddTag("Status.Stun");
            var attribute = AddAttribute("Vitals.Health");
            Assert.AreEqual(RegistryStatus.Live, Tags.GetStatus(TagRegistryId, tag));
            Assert.AreEqual(RegistryStatus.MissingRegistry, Attributes.GetStatus(TagRegistryId, tag));
            Assert.AreEqual(RegistryStatus.MissingRegistry, Tags.GetStatus(AttributeRegistryId, attribute));
        }
        #endregion



        #region AC-4, AC-5 Staging
        [Test]
        public void AC4_SaveAcrossKinds_LocksEveryFileOnce_AndWritesBoth()
        {
            string savedTags = FileText(Tags), savedAttributes = FileText(Attributes);
            AddTag("Status.Stun");
            AddAttribute("Vitals.Health");
            var provider = new RecordingLockProvider();
            RegistryLockProvider.Current = provider;

            SaveAll();

            CollectionAssert.AreEquivalent(new[] { Tags.MainRegistryPath, Attributes.MainRegistryPath, TAG_CODE, ATTRIBUTE_CODE }, provider.requests[0],
                "one request for both registries and both generated files");
            Assert.IsTrue(provider.requests.Skip(1).All(e => e.All(path => path == TAG_CODE || path == ATTRIBUTE_CODE)),
                "writing the code only confirms the lock on files the save already held");
            StringAssert.Contains("Stun", FileText(Tags));
            StringAssert.Contains("Vitals.Health", FileText(Attributes));
            Assert.AreNotEqual(savedTags, FileText(Tags));
            Assert.AreNotEqual(savedAttributes, FileText(Attributes));
            Assert.IsTrue(File.Exists(TAG_CODE) && File.Exists(ATTRIBUTE_CODE));
        }

        [Test]
        public void AC4_DiscardAcrossKinds_DropsBoth()
        {
            string savedTags = FileText(Tags), savedAttributes = FileText(Attributes);
            AddTag("Status.Stun");
            AddAttribute("Vitals.Health");

            RegistrySession.Discard();

            Assert.IsFalse(RegistrySession.HasPendingChanges);
            Assert.IsFalse(MainAttributes.TryFind("Vitals.Health", out _));
            Assert.IsNull(Tags.EnumerateRows().FirstOrDefault(e => e.Registry == TagRegistryId && e.Name == "Status.Stun").Name);
            Assert.AreEqual(savedTags, FileText(Tags));
            Assert.AreEqual(savedAttributes, FileText(Attributes));
        }

        [Test]
        public void AC5_UndoAcrossKinds_RestoresInReverseOrder_AndWritesNothing()
        {
            string savedTags = FileText(Tags), savedAttributes = FileText(Attributes);
            var tag = AddTag("Status.Stun");
            AddAttribute("Vitals.Health");

            Undo.PerformUndo();
            Assert.AreEqual(RegistryStatus.Live, Tags.GetStatus(TagRegistryId, tag), "the first undo reverts the last edit only");
            Assert.IsFalse(MainAttributes.TryFind("Vitals.Health", out _));

            Undo.PerformUndo();
            Assert.AreEqual(RegistryStatus.Deleted, Tags.GetStatus(TagRegistryId, tag));
            Assert.IsFalse(RegistrySession.HasPendingChanges);
            Assert.AreEqual(savedTags, FileText(Tags));
            Assert.AreEqual(savedAttributes, FileText(Attributes));
        }
        #endregion



        #region AC-6, AC-7 Packages
        private static RegistryPackage Package(string _Name, string _Folder, params string[] _Dependencies) => new()
        {
            Name = _Name,
            Version = "1.0.0",
            DisplayName = _Name,
            Root = CreateFolder(_Folder),
            Dependencies = _Dependencies.ToList(),
        };

        [Test]
        public void AC6_OneGenerateCommand_WritesOneRegistryPerKindAndId()
        {
            RegistryPackages.InstalledPackagesOverride = () => new Dictionary<string, string> { [VENDOR] = "1.0.0" };
            var vendorTags = RegistryGuid.Parse("b0000000000000000000000000000001");
            var vendorAttributes = RegistryGuid.Parse("b0000000000000000000000000000002");
            var fire = RegistryGuid.NewGuid();
            var shield = RegistryGuid.NewGuid();
            var rage = RegistryGuid.NewGuid();

            var vendor = Package(VENDOR, "VendorCombat");
            string holder = CreateHolder(vendor.Root, "Abilities", h =>
            {
                h.tag = GameplayTag.FromId(vendorTags, fire, "Combat.Fire");
                h.attribute = GameplayAttribute.FromId(vendorAttributes, shield, "Vitals.Shield");
                h.managed = new AttributeManagedEntry { attribute = GameplayAttribute.FromId(vendorAttributes, rage, "Combat.Rage") };
            });
            var before = ReadAll(vendor.Root);

            var result = RegistryPackageService.Generate(vendor, false);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(2, result.Written.Count, result.Message);
            CollectionAssert.AreEquivalent(before, ReadAll(vendor.Root), "the package is not changed");

            var generated = Attributes.Snapshot.Find(vendorAttributes);
            Assert.IsNotNull(generated);
            Assert.AreEqual(RegistryOrigin.Generated, generated.Origin);
            Assert.AreEqual(VENDOR, generated.Data.package);
            Assert.AreEqual("Vitals.Shield", generated.GetName(shield));
            Assert.AreEqual("Combat.Rage", generated.GetName(rage), "managed references are found too");
            StringAssert.EndsWith("." + AttributeKind.EXTENSION, generated.AssetPath);
            Assert.IsTrue(Tags.KnowsRegistry(vendorTags));
            Assert.IsFalse(Attributes.KnowsRegistry(vendorTags), "each registry belongs to one kind");

            var asset = AssetDatabase.LoadAssetAtPath<AttributeHolder>(holder);
            Assert.IsTrue(asset.attribute.IsLive);
            Assert.IsTrue(asset.managed.attribute.IsLive);
            Assert.IsTrue(asset.tag.IsLive);
        }

        [Test]
        public void AC7_PackValidation_FailsOnTheMainAttributeRegistry_NamingTheKindAndAsset()
        {
            var health = AddAttribute("Vitals.Health");
            SaveAll();
            var package = Package("com.me.abilities", "MyAbilities");
            var own = new RegistryData<AttributeRegistryRow> { registryId = RegistryGuid.NewGuid(), name = "My Abilities", origin = RegistryOrigin.Package };
            own.rows.Add(AttributeRegistryRow.Create(RegistryGuid.NewGuid(), "Shield.Cap", 30f));
            WriteRegistry($"{package.Root}/Attributes.{AttributeKind.EXTENSION}", own);
            string holder = CreateHolder(package.Root, "Holder", h => h.attribute = GameplayAttribute.FromId(AttributeRegistryId, health, "Vitals.Health"));

            var errors = RegistryPackageService.Validate(package);

            Assert.IsTrue(errors.Any(e => e.Contains(Attributes.DisplayName) && e.Contains(holder) && e.Contains("main registry")), string.Join("\n", errors));
            Assert.IsFalse(errors.Any(e => e.Contains(Tags.DisplayName)), "the package uses no tags");
        }

        [Test]
        public void PackValidation_PackageBoundsNeverNameTheMainRegistry()
        {
            var health = AddAttribute("Vitals.Health");
            SaveAll();
            RegistryPackages.InstalledPackagesOverride = () => new Dictionary<string, string> { [VENDOR_BASE] = "1.0.0" };

            // A dependency's registry, generated in the project.
            var baseRegistry = new RegistryData<AttributeRegistryRow>
            {
                registryId = RegistryGuid.NewGuid(), name = "Base", origin = RegistryOrigin.Generated, package = VENDOR_BASE, version = "1.0.0",
            };
            var baseCap = RegistryGuid.NewGuid();
            baseRegistry.rows.Add(AttributeRegistryRow.Create(baseCap, "Base.Cap", 10f));
            WriteRegistry($"{FOLDER}/Attributes/Packages/{VENDOR_BASE}.{AttributeKind.EXTENSION}", baseRegistry);

            var package = Package("com.me.abilities", "MyAbilities", VENDOR_BASE);
            var own = new RegistryData<AttributeRegistryRow> { registryId = RegistryGuid.NewGuid(), name = "My Abilities", origin = RegistryOrigin.Package };
            var cap = RegistryGuid.NewGuid();
            own.rows.Add(AttributeRegistryRow.Create(cap, "Shield.Cap", 30f));
            own.rows.Add(AttributeRegistryRow.Create(RegistryGuid.NewGuid(), "Shield.Own", _Max: AttributeBound.FromAttribute(own.registryId, cap)));
            own.rows.Add(AttributeRegistryRow.Create(RegistryGuid.NewGuid(), "Shield.Dependency", _Max: AttributeBound.FromAttribute(baseRegistry.registryId, baseCap)));
            own.rows.Add(AttributeRegistryRow.Create(RegistryGuid.NewGuid(), "Shield.Local", _Max: AttributeBound.FromAttribute(AttributeRegistryId, health)));
            own.rows.Add(AttributeRegistryRow.Create(RegistryGuid.NewGuid(), "Shield.Stranger", _Max: AttributeBound.FromAttribute(RegistryGuid.NewGuid(), cap)));
            WriteRegistry($"{package.Root}/Attributes.{AttributeKind.EXTENSION}", own);

            var errors = RegistryPackageService.Validate(package);

            Assert.IsTrue(errors.Any(e => e.Contains(Attributes.DisplayName) && e.Contains("Shield.Local") && e.Contains("main registry")), string.Join("\n", errors));
            Assert.IsTrue(errors.Any(e => e.Contains("Shield.Stranger") && e.Contains("dependencies")), string.Join("\n", errors));
            Assert.IsFalse(errors.Any(e => e.Contains("Shield.Own") || e.Contains("Shield.Dependency")), string.Join("\n", errors));
        }
        #endregion



        #region AC-8 References
        [Test]
        public void AC8_DeleteDialogs_ListOnlyTheirKindsAssets_FromOneScan()
        {
            var stun = AddTag("Status.Stun");
            var health = AddAttribute("Vitals.Health");
            SaveAll();

            string folder = CreateFolder("Holders");
            var tagHolders = Enumerable.Range(0, 3)
                .Select(i => CreateHolder(folder, $"Tagged{i}", h => h.tag = GameplayTag.FromId(TagRegistryId, stun, "Status.Stun")))
                .ToList();
            var attributeHolders = new List<string>
            {
                CreateHolder(folder, "Attributed0", h => h.attribute = GameplayAttribute.FromId(AttributeRegistryId, health, "Vitals.Health")),
                CreateHolder(folder, "Attributed1", h => h.managed = new AttributeManagedEntry { attribute = GameplayAttribute.FromId(AttributeRegistryId, health) }),
            };
            RegistryReferenceIndex.Clear();
            Assert.IsTrue(RegistryReferenceIndex.TryFindAssets(Tags, new[] { (TagRegistryId, stun) }, false, out var tagAssets));
            Assert.Greater(RegistryReferenceIndex.LastScanCount, 0);

            var plan = AttributeOperations.PlanDelete(health, false);
            Assert.AreEqual(0, RegistryReferenceIndex.LastScanCount, "the tag query's scan served the attribute query");

            CollectionAssert.AreEqual(tagHolders.OrderBy(e => e, System.StringComparer.Ordinal), tagAssets, string.Join("\n", tagAssets));
            CollectionAssert.AreEqual(attributeHolders.OrderBy(e => e, System.StringComparer.Ordinal), plan.ReferencingAssets, string.Join("\n", plan.ReferencingAssets));
            foreach (string path in attributeHolders)
                StringAssert.Contains(path, plan.Message);
            foreach (string path in tagHolders)
                StringAssert.DoesNotContain(path, plan.Message);
        }
        #endregion



        #region AC-9 Builds
        [Test]
        public void AC9_UnresolvedTagAndAttribute_BecomeNoneInTheBuildCopy_AndFailStopsTheBuild()
        {
            var health = AddAttribute("Vitals.Health");
            SaveAll();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var holder = new GameObject("Holder").AddComponent<AttributeHolderBehaviour>();
                SceneManager.MoveGameObjectToScene(holder.gameObject, scene);
                holder.tag = GameplayTag.FromId(TagRegistryId, RegistryGuid.NewGuid(), "Status.Gone");
                holder.attribute = GameplayAttribute.FromId(AttributeRegistryId, RegistryGuid.NewGuid(), "Vitals.Gone");
                holder.attributes.Add(GameplayAttribute.FromId(AttributeRegistryId, health, "Vitals.Health"));

                var reported = RegistryBuildProcessor.ProcessScene(scene, false);
                Assert.AreEqual(2, reported.Count);
                Assert.AreEqual("Vitals.Gone", holder.attribute.LastKnownName, "validation alone changes nothing");

                var issues = RegistryBuildProcessor.ProcessScene(scene, true);

                Assert.AreEqual(1, issues.Count(e => e.Reference.Kind == Tags));
                Assert.AreEqual(1, issues.Count(e => e.Reference.Kind == Attributes));
                Assert.IsTrue(holder.tag.IsNone && holder.attribute.IsNone, "missing references become None");
                Assert.AreEqual(health, holder.attributes[0].Id, "live references stay");

                var exception = Assert.Throws<BuildFailedException>(() => RegistryBuildProcessor.Report(issues, "test", _ => MissingReferencePolicy.Fail));
                StringAssert.Contains("Status.Gone", exception.Message);
                StringAssert.Contains("Vitals.Gone", exception.Message);
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void Bake_HoldsTheSavedAttributes_AndFailsOnACycleThroughAPackage()
        {
            var health = AddAttribute("Vitals.Health");
            SaveAll();
            var ward = AddAttribute("Vitals.Ward");

            var database = (GameplayAttributeDatabase)RegistryBuildProcessor.Bake(Attributes);
            Assert.AreEqual(nameof(GameplayAttributeDatabase), database.name);
            Assert.IsTrue(database.Resolve(AttributeRegistryId, health).IsLive);
            Assert.IsFalse(database.Resolve(AttributeRegistryId, ward).IsLive, "unsaved edits are not baked");

            // A package registry bounded by the main registry: pack validation refuses it, but a project can still hold one.
            var packageId = RegistryGuid.NewGuid();
            var cap = RegistryGuid.NewGuid();
            var package = new RegistryData<AttributeRegistryRow> { registryId = packageId, name = "Loop", origin = RegistryOrigin.Package };
            package.rows.Add(AttributeRegistryRow.Create(cap, "Loop.Cap", _Max: AttributeBound.FromAttribute(AttributeRegistryId, health)));
            WriteRegistry($"{FOLDER}/Loop/Loop.{AttributeKind.EXTENSION}", package);
            SetBounds("Vitals.Health", AttributeBound.None, AttributeBound.FromAttribute(packageId, cap));
            SaveAll();

            Assert.IsTrue(GameplayAttributeDatabase.Active.HasClampCycle, "the editor database ignores the cycle's bounds");
            Assert.Throws<AttributeClampCycleException>(() => RegistryBuildProcessor.Bake(Attributes), "the build bake refuses it");
        }
        #endregion



        #region AC-12 Code generation
        [Test]
        public void AC12_CodeForBothKinds_IsDeterministic_AndRenamesKeepObsoleteAliases()
        {
            var stun = AddTag("Status.Stun");
            var health = AddAttribute("Vitals.Health");
            SaveAll();

            string tagCode = File.ReadAllText(TAG_CODE);
            string attributeCode = File.ReadAllText(ATTRIBUTE_CODE);
            StringAssert.Contains("Status_Stun", tagCode);
            StringAssert.Contains("public static partial class Attributes", attributeCode);
            StringAssert.Contains("Vitals_Health", attributeCode);
            StringAssert.Contains("global::LMK.Runtime.AbilitySystem.GameplayAttribute.FromId(RegistryId", attributeCode);
            Assert.IsFalse(attributeCode.Contains('\r'), "LF line endings");

            Assert.IsTrue(RegistryCodeGen.Generate(Tags).Success);
            Assert.IsTrue(RegistryCodeGen.Generate(Attributes).Success);
            Assert.AreEqual(tagCode, File.ReadAllText(TAG_CODE), "the same registries give the same file");
            Assert.AreEqual(attributeCode, File.ReadAllText(ATTRIBUTE_CODE));

            Assert.IsTrue(TagKindAccess.Rename(stun, "Stunned").Success);
            Assert.IsTrue(AttributeOperations.Rename(health, "Vitals.Life").Success);
            SaveAll();

            tagCode = File.ReadAllText(TAG_CODE);
            attributeCode = File.ReadAllText(ATTRIBUTE_CODE);
            StringAssert.Contains("Status_Stunned", tagCode);
            StringAssert.Contains("Status_Stun = Status_Stunned;", tagCode);
            StringAssert.Contains("Vitals_Life", attributeCode);
            StringAssert.Contains("Vitals_Health = Vitals_Life;", attributeCode);
            StringAssert.Contains("Obsolete", attributeCode);
        }
        #endregion
    }
}
