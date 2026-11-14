using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using UnityEditor;

using LMK.Editor.Registry;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// A lock that is always held by someone else.
    /// </summary>
    internal sealed class RefusingLockProvider : RegistryLockProvider
    {
        public override string Name => "Test";

        public override bool IsHeld(IReadOnlyList<string> _Paths) => false;

        public override bool TryAcquire(IReadOnlyList<string> _Paths, out string _Error)
        {
            _Error = "Held by a teammate.";
            return false;
        }
    }



    /// <summary>
    /// Records every lock request, and grants them.
    /// </summary>
    internal sealed class RecordingLockProvider : RegistryLockProvider
    {
        public readonly List<List<string>> requests = new();

        public override string Name => "Recording";

        public override bool IsHeld(IReadOnlyList<string> _Paths) => true;

        public override bool TryAcquire(IReadOnlyList<string> _Paths, out string _Error)
        {
            requests.Add(_Paths.ToList());
            _Error = null;
            return true;
        }
    }



    public sealed class AttributeOperationTests : AttributeKindTestBase
    {
        #region Add, Rename, Edit
        [Test]
        public void Add_StagesWithoutWriting_AndResolvesAtOnce()
        {
            string saved = FileText(Attributes);

            var id = AddAttribute("Vitals.Health", 100f);

            Assert.AreEqual(saved, FileText(Attributes), "staged edits write nothing");
            Assert.IsTrue(AttributeOperations.HasPendingChanges);

            var health = GameplayAttribute.FromId(AttributeRegistryId, id, "Vitals.Health");
            Assert.IsTrue(health.IsLive, "the editor database shows staged edits");
            Assert.AreEqual(100f, GameplayAttributeDatabase.Active.GetDefault(health.Handle));

            SaveAll();
            StringAssert.Contains("Vitals.Health", FileText(Attributes));
            StringAssert.Contains("Vitals_Health", File.ReadAllText(ATTRIBUTE_CODE), "saving writes the generated code");
        }

        [Test]
        public void Add_RefusesInvalidAndDuplicateNames()
        {
            AddAttribute("Vitals.Health");

            StringAssert.Contains("already exists", AttributeOperations.Add("Vitals.Health").Error);
            Assert.IsFalse(AttributeOperations.Add("Vitals..Health").Success);
            Assert.IsFalse(AttributeOperations.Add("Vitals.Hea lth").Success);
            Assert.AreEqual(1, MainAttributes.Count);
        }

        [Test]
        public void Rename_ChangesTheWholeName_AndReferencesFollow()
        {
            var id = AddAttribute("Vitals.Health");
            AddAttribute("Vitals.Mana");
            var reference = GameplayAttribute.FromId(AttributeRegistryId, id, "Vitals.Health");

            var result = AttributeOperations.Rename(id, "Life.Hitpoints");

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual("Life.Hitpoints", reference.Name, "references hold the ID");
            Assert.AreEqual("Vitals.Health", reference.LastKnownName);
            StringAssert.Contains("already named", AttributeOperations.Rename(id, "Vitals.Mana").Error);
        }

        [Test]
        public void EditMetadata_ChangesDefaultBoundsPolicyFlagsAndDescription()
        {
            AddAttribute("Vitals.MaxHealth", 100f);
            AddAttribute("Vitals.Health", 100f);

            var metadata = AttributeMetadata.Of(AttributeRow("Vitals.Health"));
            metadata.defaultValue = 80f;
            metadata.min = AttributeBound.FromConstant(0f);
            metadata.max = BoundTo("Vitals.MaxHealth");
            metadata.onMaxChange = AttributeMaxChange.KeepRatio;
            metadata.flags = AttributeFlags.Integer;
            metadata.description = "Current hit points";
            Assert.IsTrue(AttributeOperations.EditMetadata(AttributeId("Vitals.Health"), metadata).Success);

            var row = AttributeRow("Vitals.Health");
            Assert.AreEqual(80f, row.defaultValue);
            Assert.AreEqual(AttributeBound.FromConstant(0f), row.min);
            Assert.AreEqual(BoundTo("Vitals.MaxHealth"), row.max);
            Assert.AreEqual(AttributeMaxChange.KeepRatio, row.onMaxChange);
            Assert.AreEqual(AttributeFlags.Integer, row.flags);
            Assert.AreEqual("Current hit points", row.description);

            var database = GameplayAttributeDatabase.Active;
            var health = database.FindAttribute(AttributeRegistryId, "Vitals.Health");
            Assert.AreEqual(database.FindAttribute(AttributeRegistryId, "Vitals.MaxHealth"), database.GetMax(health).Handle);
        }

        [Test]
        public void EditMetadata_RefusesInvalidResults_AndStagesNothing()
        {
            AddAttribute("Health", 50f);
            AddAttribute("Meta.Damage");
            var damage = AttributeMetadata.Of(AttributeRow("Meta.Damage"));
            damage.flags = AttributeFlags.Meta;
            Assert.IsTrue(AttributeOperations.EditMetadata(AttributeId("Meta.Damage"), damage).Success);
            string staged = string.Join("|", MainAttributes.Data.rows.Select(e => e.data.name + e.data.max));

            var health = AttributeMetadata.Of(AttributeRow("Health"));
            health.max = AttributeBound.FromConstant(10f);
            StringAssert.Contains("Range error", AttributeOperations.EditMetadata(AttributeId("Health"), health).Error);

            health.max = BoundTo("Meta.Damage");
            StringAssert.Contains("Meta misuse", AttributeOperations.EditMetadata(AttributeId("Health"), health).Error);

            damage.max = AttributeBound.FromConstant(1f);
            StringAssert.Contains("Meta misuse", AttributeOperations.EditMetadata(AttributeId("Meta.Damage"), damage).Error);

            Assert.AreEqual(staged, string.Join("|", MainAttributes.Data.rows.Select(e => e.data.name + e.data.max)));
        }
        #endregion



        #region Clamp cycles and deleted bounds
        [Test]
        public void ClampCycle_IsRefused()
        {
            AddAttribute("X");
            AddAttribute("Y");
            SetBounds("X", AttributeBound.None, BoundTo("Y"));

            var metadata = AttributeMetadata.Of(AttributeRow("Y"));
            metadata.max = BoundTo("X");
            var result = AttributeOperations.EditMetadata(AttributeId("Y"), metadata);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("Clamp cycle", result.Error);
            Assert.IsTrue(AttributeRow("Y").max.IsNone);
        }

        [Test]
        public void HandEditedCycle_IsRejected_AndRefusesEditing()
        {
            var x = RegistryGuid.NewGuid();
            var y = RegistryGuid.NewGuid();
            var data = new RegistryData<AttributeRegistryRow> { registryId = AttributeRegistryId, name = "Main", origin = RegistryOrigin.Main };
            data.rows.Add(AttributeRegistryRow.Create(x, "X", _Max: AttributeBound.FromAttribute(AttributeRegistryId, y)));
            data.rows.Add(AttributeRegistryRow.Create(y, "Y", _Max: AttributeBound.FromAttribute(AttributeRegistryId, x)));

            // Written bypassing validation, as a hand edit or a bad merge would.
            // The importer reports the rejected file as an error.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            File.WriteAllText(Attributes.MainRegistryPath, RegistryFile.Write(data, Attributes));
            AssetDatabase.ImportAsset(Attributes.MainRegistryPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var result = AttributeOperations.Add("Z");
            Assert.IsFalse(result.Success);
            StringAssert.Contains("Clamp cycle", result.Error);
            Assert.IsFalse(MainAttributes.Contains(x), "the rejected file is not loaded");
        }

        [Test]
        public void DeletingABound_DependentLosesIt_AndThePlanListsItFirst()
        {
            AddAttribute("Vitals.MaxHealth", 100f);
            AddAttribute("Vitals.Health", 100f);
            SetBounds("Vitals.Health", AttributeBound.FromConstant(0f), BoundTo("Vitals.MaxHealth"));
            var maxHealth = AttributeId("Vitals.MaxHealth");

            var plan = AttributeOperations.PlanDelete(maxHealth, false);

            CollectionAssert.AreEqual(new[] { "Vitals.Health" }, plan.Dependents);
            Assert.Less(plan.Message.IndexOf("Vitals.Health"), plan.Message.IndexOf("asset"), "the dependents are listed first");

            Assert.IsTrue(AttributeOperations.Delete(maxHealth).Success);
            Assert.IsTrue(AttributeRow("Vitals.Health").max.IsNone, "the dependent loses that bound");
            Assert.AreEqual(AttributeBound.FromConstant(0f), AttributeRow("Vitals.Health").min, "its other bound stays");

            Undo.PerformUndo();
            Assert.AreEqual(BoundTo("Vitals.MaxHealth"), AttributeRow("Vitals.Health").max, "undo restores both");
        }
        #endregion



        #region Bounds in other registries
        private static readonly RegistryGuid Shields = RegistryGuid.Parse("d0000000000000000000000000000001");
        private static readonly RegistryGuid OtherMain = RegistryGuid.Parse("d0000000000000000000000000000002");
        private static readonly RegistryGuid Cap = RegistryGuid.Parse("e0000000000000000000000000000001");
        private static readonly RegistryGuid Scratch = RegistryGuid.Parse("e0000000000000000000000000000002");

        private static void WriteOtherRegistries()
        {
            var package = new RegistryData<AttributeRegistryRow> { registryId = Shields, name = "Shields", origin = RegistryOrigin.Package };
            package.rows.Add(AttributeRegistryRow.Create(Cap, "Shield.Cap", 30f));
            package.rows.Add(AttributeRegistryRow.Create(Scratch, "Shield.Scratch", _Flags: AttributeFlags.Meta));
            WriteRegistry($"{FOLDER}/Shields/Shields.{AttributeKind.EXTENSION}", package);

            var other = new RegistryData<AttributeRegistryRow> { registryId = OtherMain, name = "Elsewhere", origin = RegistryOrigin.Main };
            other.rows.Add(AttributeRegistryRow.Create(Cap, "Copied.Cap"));
            WriteRegistry($"{FOLDER}/Elsewhere/Main.{AttributeKind.EXTENSION}", other);
        }

        [Test]
        public void Bounds_MayNamePackageAttributes_ButNotMetaOrUnknownOrOtherMains()
        {
            WriteOtherRegistries();
            AddAttribute("Vitals.Shield");
            var id = AttributeId("Vitals.Shield");
            var metadata = AttributeMetadata.Of(AttributeRow("Vitals.Shield"));

            metadata.max = AttributeBound.FromAttribute(Shields, Cap);
            Assert.IsTrue(AttributeOperations.EditMetadata(id, metadata).Success);
            var database = GameplayAttributeDatabase.Active;
            Assert.AreEqual(database.Resolve(Shields, Cap).Handle, database.GetMax(database.Resolve(AttributeRegistryId, id).Handle).Handle);

            metadata.max = AttributeBound.FromAttribute(Shields, Scratch);
            StringAssert.Contains("meta attribute", AttributeOperations.EditMetadata(id, metadata).Error);

            metadata.max = AttributeBound.FromAttribute(Shields, RegistryGuid.NewGuid());
            StringAssert.Contains("isn't in", AttributeOperations.EditMetadata(id, metadata).Error);

            metadata.max = AttributeBound.FromAttribute(RegistryGuid.NewGuid(), Cap);
            StringAssert.Contains("isn't loaded", AttributeOperations.EditMetadata(id, metadata).Error);

            metadata.max = AttributeBound.FromAttribute(OtherMain, Cap);
            StringAssert.Contains("main registry", AttributeOperations.EditMetadata(id, metadata).Error);

            Assert.AreEqual(AttributeBound.FromAttribute(Shields, Cap), AttributeRow("Vitals.Shield").max);
        }

        [Test]
        public void PackageRegistries_AreReadOnly_AndListedAsDependents()
        {
            WriteOtherRegistries();
            AddAttribute("Vitals.Shield");
            SetBounds("Vitals.Shield", AttributeBound.None, AttributeBound.FromAttribute(Shields, Cap));

            StringAssert.Contains("read-only", AttributeOperations.Delete(Cap).Error);
            StringAssert.Contains("read-only", AttributeOperations.Rename(Cap, "Shield.Max").Error);
            Assert.IsNull(AttributeOperations.PlanDelete(Cap, false), "only main registry attributes can be deleted");
            CollectionAssert.AreEqual(new[] { (AttributeRegistryId, AttributeId("Vitals.Shield")) }, AttributeOperations.FindDependents(Shields, Cap));
        }
        #endregion



        #region Lock and orphans
        [Test]
        public void RefusingLock_RefusesEveryOperation_WithItsMessage()
        {
            var id = AddAttribute("Vitals.Health");
            RegistryLockProvider.Current = new RefusingLockProvider();

            foreach (var result in new[]
                     {
                         AttributeOperations.Add("Vitals.Mana"),
                         AttributeOperations.Rename(id, "Vitals.Life"),
                         AttributeOperations.EditMetadata(id, AttributeMetadata.Of(AttributeRow("Vitals.Health"))),
                         AttributeOperations.Delete(id),
                         AttributeOperations.Save(),
                     })
            {
                Assert.IsFalse(result.Success);
                StringAssert.Contains("Held by a teammate.", result.Error);
            }
        }

        [Test]
        public void OrphanRows_ReuseTheName_OrTakeThePreferredId()
        {
            var data = new RegistryData<AttributeRegistryRow> { registryId = RegistryGuid.NewGuid(), origin = RegistryOrigin.Generated };
            var preferred = RegistryGuid.NewGuid();

            Assert.AreEqual(preferred, Attributes.AddRowForOrphan(data, "Vitals.Shield", preferred, null, out _));
            Assert.AreEqual(preferred, Attributes.AddRowForOrphan(data, "Vitals.Shield", RegistryGuid.NewGuid(), null, out _), "the name is reused");
            Assert.AreNotEqual(preferred, Attributes.AddRowForOrphan(data, "Vitals.Ward", preferred, null, out _), "a taken ID is not reused");

            Assert.IsTrue(Attributes.AddRowForOrphan(data, "Bad Name", RegistryGuid.NewGuid(), null, out string warning).IsZero);
            StringAssert.Contains("Bad Name", warning);
            Assert.AreEqual(2, data.rows.Count);
        }
        #endregion
    }
}
