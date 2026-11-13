using System;
using System.Collections.Generic;

using NUnit.Framework;
using UnityEngine;

using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

using Row = LMK.Runtime.GameplayTags.GameplayTagDatabaseBuilder.Row;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// Base for tests that swap the active database. Restores the previous database, and the editor hook
    /// that loads one, after every test.
    /// </summary>
    public abstract class DatabaseTestBase
    {
        private GameplayTagDatabase previousActive;
        private Action previousEnsureActive;
        private readonly List<GameplayTagDatabase> created = new();

        [SetUp]
        public void SaveActiveDatabase()
        {
            previousEnsureActive = GameplayTagDatabase.EditorEnsureActive;
            previousActive = GameplayTagDatabase.Active;

            // Tests control the active database themselves.
            GameplayTagDatabase.EditorEnsureActive = null;
        }

        [TearDown]
        public void RestoreActiveDatabase()
        {
            GameplayTagDatabase.EditorEnsureActive = previousEnsureActive;
            GameplayTagDatabase.SetActive(previousActive);

            foreach (var database in created)
                if (database != null)
                    UnityEngine.Object.DestroyImmediate(database);

            created.Clear();
        }

        /// <summary>
        /// Build a database, make it active, and destroy it after the test.
        /// </summary>
        protected GameplayTagDatabase Activate(GameplayTagDatabaseBuilder _Builder)
        {
            var database = _Builder.Build();
            created.Add(database);
            GameplayTagDatabase.SetActive(database);

            return database;
        }

        protected GameplayTagDatabase Track(GameplayTagDatabase _Database)
        {
            created.Add(_Database);
            return _Database;
        }
    }



    /// <summary>
    /// Fixed IDs and small registries for tests.
    /// </summary>
    public static class TestIds
    {
        public static readonly RegistryGuid MainRegistry = RegistryGuid.Parse("6b1f0c2a9d7e4b3f8a5c1d0e2f3a4b5c");
        public static readonly RegistryGuid PackageRegistry = RegistryGuid.Parse("a0000000000000000000000000000001");

        /// <summary>
        /// A deterministic, non-zero tag ID.
        /// </summary>
        public static RegistryGuid Id(int _N) => new(0x1000_0000_0000_0000UL, (ulong)_N);

        public static Row Row(int _Id, int _Parent, string _Segment, bool _Implicit = false)
            => new(Id(_Id), _Parent == 0 ? RegistryGuid.Zero : Id(_Parent), _Segment, _Implicit);

        /// <summary>
        /// Main registry: Ability(1) > Fire(2) > Projectile(3), Status(4) > Burning(5), Status > Stun(6).
        /// </summary>
        public static IEnumerable<Row> MainRows() => new[]
        {
            Row(1, 0, "Ability", _Implicit: true),
            Row(2, 1, "Fire"),
            Row(3, 2, "Projectile"),
            Row(4, 0, "Status", _Implicit: true),
            Row(5, 4, "Burning"),
            Row(6, 4, "Stun"),
        };

        public static GameplayTagDatabaseBuilder MainBuilder()
            => new GameplayTagDatabaseBuilder().AddRegistry(MainRegistry, "Main", TagRegistryKind.Main, MainRows());

        public static GameplayTag Tag(RegistryGuid _Registry, int _Id, string _Name)
            => GameplayTag.FromId(_Registry, Id(_Id), _Name);

        public static GameplayTag Main(int _Id, string _Name) => Tag(MainRegistry, _Id, _Name);
    }
}
