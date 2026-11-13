using System;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using LMK.Runtime.Registry;

using Object = UnityEngine.Object;
using Row = LMK.Runtime.Registry.RegistryDatabaseBuilder<LMK.Editor.Registry.Tests.TestRow>.Row;

namespace LMK.Editor.Registry.Tests
{
    public sealed class DatabaseTests
    {
        private static readonly RegistryGuid Main = RegistryGuid.Parse("6b1f0c2a9d7e4b3f8a5c1d0e2f3a4b5c");
        private static readonly RegistryGuid Package = RegistryGuid.Parse("a0000000000000000000000000000001");

        private static RegistryGuid Id(int _N) => new(0x1000_0000_0000_0000UL, (ulong)_N);

        private static Row R(int _Id, string _Name, float _Value = 0, int _Bound = 0)
            => new(Id(_Id), _Name, new TestRow(_Value, _Bound == 0 ? RegistryGuid.Zero : Id(_Bound)));

        private readonly List<Object> created = new();
        private Action previousEnsureActive;

        [SetUp]
        public void SetUp()
        {
            previousEnsureActive = TestDatabase.EditorEnsureActive;
            TestDatabase.EditorEnsureActive = null;
            TestDatabase.SetActive(null);
        }

        [TearDown]
        public void TearDown()
        {
            TestDatabase.SetActive(null);
            TestDatabase.EditorEnsureActive = previousEnsureActive;

            foreach (var target in created)
                if (target != null)
                    Object.DestroyImmediate(target);

            created.Clear();
        }

        private TestDatabase Build(RegistryDatabaseBuilder<TestRow> _Builder)
        {
            var database = _Builder.Build<TestDatabase>();
            created.Add(database);
            return database;
        }

        private TestDatabase BuildMain(params Row[] _Rows)
            => Build(TestDatabase.NewBuilder().AddRegistry(Main, "Main", RegistryOrigin.Main, _Rows));



        #region Resolution
        [Test]
        public void Resolve_LiveDeletedMissingAndNone()
        {
            var database = BuildMain(R(1, "Health"));

            var live = database.Resolve(Main, Id(1));
            Assert.IsTrue(live.IsLive);
            Assert.AreEqual("Health", database.GetRowName(live.Row));

            Assert.AreEqual(RegistryStatus.Deleted, database.Resolve(Main, Id(2)).Status);
            Assert.AreEqual(RegistryStatus.MissingRegistry, database.Resolve(Package, Id(1)).Status);
            Assert.AreEqual(RegistryStatus.None, database.Resolve(Main, RegistryGuid.Zero).Status);
        }

        [Test]
        public void SameIdInTwoRegistries_IsTwoRows()
        {
            var database = Build(TestDatabase.NewBuilder()
                .AddRegistry(Main, "Main", RegistryOrigin.Main, new[] { R(1, "Health", 1) })
                .AddRegistry(Package, "Pkg", RegistryOrigin.Package, new[] { R(1, "Health", 2) }));

            int mainRow = database.Resolve(Main, Id(1)).Row;
            int packageRow = database.Resolve(Package, Id(1)).Row;

            Assert.AreNotEqual(mainRow, packageRow);
            Assert.AreEqual(1f, database.GetRowData(mainRow).value);
            Assert.AreEqual(2f, database.GetRowData(packageRow).value);
            Assert.AreEqual(Package, database.GetRowRegistryId(packageRow));
        }

        [Test]
        public void FindByName_IsPerRegistry()
        {
            var database = Build(TestDatabase.NewBuilder()
                .AddRegistry(Main, "Main", RegistryOrigin.Main, new[] { R(1, "Health") })
                .AddRegistry(Package, "Pkg", RegistryOrigin.Package, new[] { R(2, "Health") }));

            Assert.AreEqual(Id(1), database.GetRowId(database.FindByName(Main, "Health")));
            Assert.AreEqual(Id(2), database.GetRowId(database.FindByName(Package, "Health")));
            Assert.AreEqual(RegistryResolution.NO_ROW, database.FindByName(Main, "Mana"));
            Assert.AreEqual(RegistryResolution.NO_ROW, database.FindByName(RegistryGuid.NewGuid(), "Health"));
        }

        [Test]
        public void Registries_ExposeIdentityAndRows()
        {
            var database = Build(TestDatabase.NewBuilder()
                .AddRegistry(Package, "Pkg", RegistryOrigin.Package, new[] { R(2, "A"), R(3, "B") })
                .AddRegistry(Main, "Main", RegistryOrigin.Main, new[] { R(1, "A") }));

            Assert.AreEqual(2, database.RegistryCount);
            Assert.AreEqual(Main, database.MainRegistryId);
            Assert.IsTrue(database.HasRegistry(Package));
            Assert.AreEqual("Pkg", database.GetRegistryName(Package));

            int packageIndex = database.FindRegistry(Package);
            Assert.AreEqual(RegistryOrigin.Package, database.GetRegistry(packageIndex).Origin);
            Assert.AreEqual(2, database.GetRegistryRows(packageIndex).rowCount);
        }
        #endregion



        #region Builder
        [Test]
        public void Build_IsDeterministic_RegardlessOfInputOrder()
        {
            var a = Build(TestDatabase.NewBuilder()
                .AddRegistry(Main, "Main", RegistryOrigin.Main, new[] { R(1, "B", 1), R(2, "A", 2) })
                .AddRegistry(Package, "Pkg", RegistryOrigin.Package, new[] { R(3, "C", 3) }));
            var b = Build(TestDatabase.NewBuilder()
                .AddRegistry(Package, "Pkg", RegistryOrigin.Package, new[] { R(3, "C", 3) })
                .AddRegistry(Main, "Main", RegistryOrigin.Main, new[] { R(2, "A", 2), R(1, "B", 1) }));

            Assert.AreEqual(a.ContentHash, b.ContentHash);
            for (int i = 0; i < a.RowCount; i++)
            {
                Assert.AreEqual(a.GetRowId(i), b.GetRowId(i));
                Assert.AreEqual(a.GetRowName(i), b.GetRowName(i));
            }
        }

        [Test]
        public void Rows_AreOrderedByNameWithinARegistry_AndRegistriesById()
        {
            var database = Build(TestDatabase.NewBuilder()
                .AddRegistry(Package, "Pkg", RegistryOrigin.Package, new[] { R(5, "Z") })
                .AddRegistry(Main, "Main", RegistryOrigin.Main, new[] { R(1, "Mana"), R(2, "Health"), R(3, "Armor") }));

            // Main's ID sorts before Package's.
            CollectionAssert.AreEqual(new[] { "Armor", "Health", "Mana", "Z" },
                new[] { database.GetRowName(0), database.GetRowName(1), database.GetRowName(2), database.GetRowName(3) });
        }

        [Test]
        public void ZeroAndRepeatedIds_AreSkipped()
        {
            var database = BuildMain(new Row(RegistryGuid.Zero, "Zero", default), R(1, "First", 1), R(1, "Again", 2));

            Assert.AreEqual(1, database.RowCount);
            Assert.AreEqual("First", database.GetRowName(0));
        }

        [Test]
        public void Link_SeesFinalIndices()
        {
            // Names put the bound row after the row that refers to it in the input, but before it in the database.
            var database = BuildMain(R(1, "B", 0, _Bound: 2), R(2, "A"), R(3, "C", 0, _Bound: 99));

            int b = database.Resolve(Main, Id(1)).Row;
            Assert.AreEqual(database.Resolve(Main, Id(2)).Row, database.GetRowData(b).boundRow);
            Assert.AreEqual(RegistryResolution.NO_ROW, database.GetRowData(database.Resolve(Main, Id(3)).Row).boundRow);
        }

        [Test]
        public void ContentHash_CoversTheKindsData()
        {
            var a = BuildMain(R(1, "Health", 100));
            var b = BuildMain(R(1, "Health", 150));
            var c = BuildMain(R(1, "Health", 100));

            Assert.AreNotEqual(a.ContentHash, b.ContentHash);
            Assert.AreEqual(a.ContentHash, c.ContentHash);
            Assert.AreEqual(64, a.ContentHash.Length);
        }

        [Test]
        public void Build_NamesTheDatabaseAfterItsType()
        {
            Assert.AreEqual(nameof(TestDatabase), BuildMain().name);
            Assert.AreEqual(nameof(TestDatabase), TestDatabase.ResourceName);
        }

        [Test]
        public void AddRegistry_RejectsZeroAndRepeatedIds()
        {
            var builder = TestDatabase.NewBuilder().AddRegistry(Main, "Main", RegistryOrigin.Main, null);

            Assert.Throws<ArgumentException>(() => builder.AddRegistry(RegistryGuid.Zero, "Zero", RegistryOrigin.Main, null));
            Assert.Throws<ArgumentException>(() => builder.AddRegistry(Main, "Again", RegistryOrigin.Main, null));
        }
        #endregion



        #region Lookups and serialization
        [Test]
        public void KindTables_AreBuiltOncePerContent_AndMayResolve()
        {
            var database = BuildMain(R(1, "Health"));

            database.Resolve(Main, Id(1));
            database.FindByName(Main, "Health");
            Assert.AreEqual(1, database.lookupsBuilt);
            Assert.IsTrue(database.firstRowDuringBuild.IsLive);

            TestDatabase.NewBuilder().AddRegistry(Main, "Main", RegistryOrigin.Main, new[] { R(1, "Health"), R(2, "Mana") }).BuildInto(database);
            Assert.IsTrue(database.Resolve(Main, Id(2)).IsLive);
            Assert.AreEqual(2, database.lookupsBuilt);
        }

        [Test]
        public void Instantiate_KeepsCoreAndKindData()
        {
            var original = BuildMain(R(1, "B", 7, _Bound: 2), R(2, "A", 3));

            var copy = Object.Instantiate(original);
            created.Add(copy);

            Assert.AreEqual(original.ContentHash, copy.ContentHash);
            int b = copy.Resolve(Main, Id(1)).Row;
            Assert.AreEqual("B", copy.GetRowName(b));
            Assert.AreEqual(7f, copy.GetRowData(b).value);
            Assert.AreEqual(copy.Resolve(Main, Id(2)).Row, copy.GetRowData(b).boundRow);
        }
        #endregion



        #region Active database
        [Test]
        public void NoActiveDatabase_AssignedReferenceIsMissingRegistry_AndGenerationIsZero()
        {
            Assert.IsNull(TestDatabase.Active);
            Assert.AreEqual(0, TestDatabase.Generation);
            Assert.AreEqual(RegistryStatus.MissingRegistry, TestDatabase.ResolveActive(Main, Id(1)).Status);
            Assert.AreEqual(RegistryStatus.None, TestDatabase.ResolveActive(Main, RegistryGuid.Zero).Status);
        }

        [Test]
        public void SetActive_ChangesGeneration_AndResolvesAgainstIt()
        {
            var first = BuildMain(R(1, "Health"));
            TestDatabase.SetActive(first);
            int firstGeneration = TestDatabase.Generation;

            var second = BuildMain();
            TestDatabase.SetActive(second);

            Assert.AreNotEqual(0, firstGeneration);
            Assert.AreNotEqual(firstGeneration, TestDatabase.Generation);
            Assert.AreEqual(RegistryStatus.Deleted, TestDatabase.ResolveActive(Main, Id(1)).Status);
        }

        [Test]
        public void ActiveChanged_PassesPreviousAndCurrent_AndPreviousOutlivesTheEvent()
        {
            var first = BuildMain(R(1, "Health"));
            var second = BuildMain(R(1, "Health"));
            TestDatabase.SetActive(first);

            TestDatabase seenPrevious = null, seenCurrent = null;
            string nameReadFromPrevious = null;
            void OnChanged(TestDatabase _Previous, TestDatabase _Current)
            {
                seenPrevious = _Previous;
                seenCurrent = _Current;
                nameReadFromPrevious = _Previous.GetRowName(_Previous.Resolve(Main, Id(1)).Row);
            }

            TestDatabase.ActiveChanged += OnChanged;
            try
            {
                TestDatabase.SetActive(second, _DestroyPrevious: true);
            }
            finally
            {
                TestDatabase.ActiveChanged -= OnChanged;
            }

            Assert.AreSame(first, seenPrevious);
            Assert.AreSame(second, seenCurrent);
            Assert.AreEqual("Health", nameReadFromPrevious);
            Assert.IsTrue(first == null, "The previous database is destroyed after the event.");
            Assert.IsTrue(second != null);
        }

        [Test]
        public void SetActive_SameDatabase_IsNotDestroyed()
        {
            var database = BuildMain();
            TestDatabase.SetActive(database);
            TestDatabase.SetActive(database, _DestroyPrevious: true);

            Assert.IsTrue(database != null);
        }

        [Test]
        public void EditorEnsureActive_IsAskedWhenNoDatabaseIsActive()
        {
            var database = BuildMain();
            int calls = 0;
            TestDatabase.EditorEnsureActive = () => { calls++; TestDatabase.SetActive(database); };

            Assert.AreSame(database, TestDatabase.Active);
            Assert.AreSame(database, TestDatabase.Active);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void ReportUnresolved_IsSilentOutsidePlayMode()
        {
            bool built = false;
            TestDatabase.ReportUnresolved(Main, Id(1), () => { built = true; return "unresolved"; });

            Assert.IsFalse(built);
        }
        #endregion
    }
}
