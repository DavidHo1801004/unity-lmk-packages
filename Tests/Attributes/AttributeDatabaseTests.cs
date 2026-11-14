using System.Linq;

using NUnit.Framework;

using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

using static LMK.Editor.AbilitySystem.Tests.AttributeIds;

using Row = LMK.Runtime.AbilitySystem.GameplayAttributeDatabaseBuilder.Row;

namespace LMK.Editor.AbilitySystem.Tests
{
    public sealed class AttributeDatabaseTests : AttributeDatabaseTestBase
    {
        private static AttributeHandle Handle(GameplayAttributeDatabase _Database, int _Id) => _Database.Resolve(MainRegistry, Id(_Id)).Handle;



        #region Resolution
        [Test]
        public void Resolve_LiveDeletedMissingRegistryAndNone()
        {
            var database = Activate(MainBuilder());

            var health = Main(HEALTH, "Old.Name");
            Assert.AreEqual(AttributeStatus.Live, health.Resolve().Status);
            Assert.AreEqual("Vitals.Health", health.Name, "the current name, not the stored one");

            var ghost = GameplayAttribute.FromId(MainRegistry, RegistryGuid.NewGuid(), "Vitals.Ghost");
            Assert.AreEqual(AttributeStatus.Deleted, ghost.Resolve().Status);
            Assert.AreEqual(AttributeHandle.None, ghost.Handle);
            Assert.AreEqual("Vitals.Ghost", ghost.Name, "a missing attribute shows its last known name");

            var elsewhere = GameplayAttribute.FromId(RegistryGuid.NewGuid(), Id(HEALTH), "Vitals.Health");
            Assert.AreEqual(AttributeStatus.MissingRegistry, elsewhere.Resolve().Status);

            Assert.AreEqual(AttributeStatus.None, GameplayAttribute.None.Resolve().Status);
            Assert.IsTrue(GameplayAttribute.FromId(MainRegistry, RegistryGuid.Zero, "x").IsNone, "a zero ID is None");
            Assert.AreEqual(9, database.AttributeCount);
        }

        [Test]
        public void NoActiveDatabase_AssignedReferenceIsMissingRegistry()
        {
            GameplayAttributeDatabase.SetActive(null);

            Assert.AreEqual(AttributeStatus.MissingRegistry, Main(HEALTH).Resolve().Status);
            Assert.AreEqual(AttributeHandle.None, Main(HEALTH).Handle);
            Assert.AreEqual(GameplayAttribute.None, GameplayAttribute.FromName("Vitals.Health"));
        }

        [Test]
        public void Equality_IsRegistryAndId_WhateverTheName()
        {
            var a = GameplayAttribute.FromId(MainRegistry, Id(HEALTH), "Vitals.Health");
            var renamed = GameplayAttribute.FromId(MainRegistry, Id(HEALTH), "Life.Health");
            var otherRegistry = GameplayAttribute.FromId(PackageRegistry, Id(HEALTH), "Vitals.Health");

            Assert.IsTrue(a == renamed);
            Assert.AreEqual(a.GetHashCode(), renamed.GetHashCode());
            Assert.IsTrue(a != otherRegistry, "the same name or ID in another registry is another attribute");
        }

        [Test]
        public void FromName_IsPerRegistry_AndGetAttributeRoundTrips()
        {
            var database = Activate(MainBuilder().AddRegistry(PackageRegistry, "Package", RegistryOrigin.Package,
                new[] { new Row(Id(HEALTH), "Vitals.Health", 5f) }));

            var main = GameplayAttribute.FromName("Vitals.Health");
            var package = GameplayAttribute.FromName(PackageRegistry, "Vitals.Health");

            Assert.AreEqual(Main(HEALTH), main);
            Assert.AreEqual(PackageRegistry, package.RegistryId);
            Assert.AreNotEqual(main, package, "two attributes");
            Assert.AreEqual(5f, database.GetDefault(package.Handle));
            Assert.AreEqual(main, database.GetAttribute(main.Handle));
            Assert.AreEqual("Vitals.Health", database.GetAttribute(main.Handle).LastKnownName);
        }
        #endregion



        #region Metadata
        [Test]
        public void Metadata_DefaultsBoundsFlagsAndPolicy()
        {
            var database = Activate(MainBuilder());
            var health = Handle(database, HEALTH);
            var mana = Handle(database, MANA);

            Assert.AreEqual(100f, database.GetDefault(health));
            Assert.AreEqual(AttributeBoundKind.Constant, database.GetMin(health).Kind);
            Assert.AreEqual(0f, database.GetMin(health).Constant);
            Assert.AreEqual(Handle(database, MAX_HEALTH), database.GetMax(health).Handle);
            Assert.AreEqual(AttributeMaxChange.Clamp, database.GetOnMaxChange(health));
            Assert.AreEqual(AttributeMaxChange.KeepRatio, database.GetOnMaxChange(mana));
            Assert.IsTrue(database.IsMeta(Handle(database, INCOMING_DAMAGE)));
            Assert.IsFalse(database.IsMeta(health));
            Assert.IsTrue(database.GetMax(Handle(database, A)).IsNone);

            Assert.AreEqual(0f, database.GetDefault(AttributeHandle.None), "operations on None are no-ops");
            Assert.IsTrue(database.GetMax(AttributeHandle.None).IsNone);
            CollectionAssert.IsEmpty(database.GetDependents(AttributeHandle.None));
        }

        [Test]
        public void Dependents_AndInitializationOrder_PutBoundsFirst()
        {
            var database = Activate(MainBuilder());
            var order = database.InitializationOrder.ToList();

            Assert.AreEqual(database.AttributeCount, order.Count);
            Assert.Less(order.IndexOf(Handle(database, MAX_HEALTH)), order.IndexOf(Handle(database, HEALTH)));
            Assert.Less(order.IndexOf(Handle(database, MAX_MANA)), order.IndexOf(Handle(database, MANA)));
            CollectionAssert.AreEqual(new[] { Handle(database, HEALTH) }, database.GetDependents(Handle(database, MAX_HEALTH)));
            CollectionAssert.IsEmpty(database.GetDependents(Handle(database, HEALTH)));
            Assert.IsFalse(database.HasClampCycle);
        }

        [Test]
        public void Build_IsDeterministic_RegardlessOfInputOrder()
        {
            var first = Track(MainBuilder().Build());
            var second = Track(new GameplayAttributeDatabaseBuilder()
                .AddRegistry(MainRegistry, "Main", RegistryOrigin.Main, MainRows().Reverse())
                .Build());

            Assert.AreEqual(first.ContentHash, second.ContentHash);
            CollectionAssert.AreEqual(first.InitializationOrder, second.InitializationOrder);

            var changed = Track(new GameplayAttributeDatabaseBuilder()
                .AddRegistry(MainRegistry, "Main", RegistryOrigin.Main, MainRows().Select(e => e.Id == Id(HEALTH) ? new Row(e.Id, e.Name, 99f) : e))
                .Build());
            Assert.AreNotEqual(first.ContentHash, changed.ContentHash, "the hash covers metadata");
        }

        [Test]
        public void Bounds_ResolveAcrossRegistries_AndUnresolvedBoundsAreIgnored()
        {
            var cap = RegistryGuid.NewGuid();
            var database = Activate(new GameplayAttributeDatabaseBuilder()
                .AddRegistry(PackageRegistry, "Shields", RegistryOrigin.Package, new[] { new Row(cap, "Shield.Cap", 30f) })
                .AddRegistry(MainRegistry, "Main", RegistryOrigin.Main, new[]
                {
                    new Row(Id(1), "Vitals.Shield", 0f, _Max: AttributeBound.FromAttribute(PackageRegistry, cap)),
                    new Row(Id(2), "Vitals.Ward", 0f, _Max: AttributeBound.FromAttribute(RegistryGuid.NewGuid(), RegistryGuid.NewGuid())),
                    new Row(Id(3), "Vitals.Gone", 0f, _Max: AttributeBound.FromAttribute(MainRegistry, RegistryGuid.NewGuid())),
                }));

            var shieldCap = database.Resolve(PackageRegistry, cap).Handle;
            Assert.AreEqual(shieldCap, database.GetMax(Handle(database, 1)).Handle);
            CollectionAssert.AreEqual(new[] { Handle(database, 1) }, database.GetDependents(shieldCap));

            Assert.IsTrue(database.GetMax(Handle(database, 2)).IsNone, "a bound in an unloaded registry is ignored");
            Assert.IsTrue(database.GetMax(Handle(database, 3)).IsNone, "a bound on a deleted attribute is ignored");
            Assert.AreEqual(AttributeBound.FromAttribute(PackageRegistry, cap), database.GetRow(Handle(database, 1)).Max, "the stored bound is kept");
        }
        #endregion



        #region Clamp Cycles
        [Test]
        public void ClampCycle_FailsTheBake()
        {
            var builder = new GameplayAttributeDatabaseBuilder().AddRegistry(MainRegistry, "Main", RegistryOrigin.Main, new[]
            {
                new Row(Id(1), "X", _Max: Bound(2)),
                new Row(Id(2), "Y", _Max: Bound(1)),
                new Row(Id(3), "Z", _Max: Bound(1)),
            });

            var exception = Assert.Throws<AttributeClampCycleException>(() => builder.Build(), "a hand-edited file cannot reach runtime");
            CollectionAssert.AreEquivalent(new[] { "X", "Y" }, exception.Attributes, "Z only follows the cycle");
        }

        [Test]
        public void LenientBuild_IgnoresTheBoundsThatCloseTheCycle()
        {
            var database = Track(new GameplayAttributeDatabaseBuilder().AddRegistry(MainRegistry, "Main", RegistryOrigin.Main, new[]
            {
                new Row(Id(1), "X", _Max: Bound(2)),
                new Row(Id(2), "Y", _Max: Bound(1)),
                new Row(Id(3), "Self", _Min: Bound(3)),
                new Row(Id(4), "Z", _Max: Bound(1)),
            }).BuildLenient());

            Assert.IsTrue(database.HasClampCycle);
            CollectionAssert.AreEquivalent(new[] { "X", "Y", "Self" }, database.ClampCycle.Select(database.GetName));
            Assert.IsTrue(database.GetMax(Handle(database, 1)).IsNone);
            Assert.IsTrue(database.GetMax(Handle(database, 2)).IsNone);
            Assert.IsTrue(database.GetMin(Handle(database, 3)).IsNone);
            Assert.AreEqual(Handle(database, 1), database.GetMax(Handle(database, 4)).Handle, "bounds outside the cycle stay");
            Assert.AreEqual(4, database.InitializationOrder.Count, "every attribute is still initialised");
        }

        [Test]
        public void CycleAcrossRegistries_FailsTheBake()
        {
            var builder = new GameplayAttributeDatabaseBuilder()
                .AddRegistry(MainRegistry, "Main", RegistryOrigin.Main, new[] { new Row(Id(1), "X", _Max: AttributeBound.FromAttribute(PackageRegistry, Id(2))) })
                .AddRegistry(PackageRegistry, "Package", RegistryOrigin.Package, new[] { new Row(Id(2), "Y", _Max: Bound(1)) });

            Assert.Throws<AttributeClampCycleException>(() => builder.Build());
        }
        #endregion
    }
}
