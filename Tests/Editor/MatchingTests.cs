using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using LMK.Runtime.GameplayTags;

using static LMK.Editor.GameplayTags.Tests.TestIds;

namespace LMK.Editor.GameplayTags.Tests
{
    public sealed class MatchingTests : DatabaseTestBase
    {
        private static readonly GameplayTag Ability = Main(1, "Ability");
        private static readonly GameplayTag Fire = Main(2, "Ability.Fire");
        private static readonly GameplayTag Projectile = Main(3, "Ability.Fire.Projectile");
        private static readonly GameplayTag Status = Main(4, "Status");
        private static readonly GameplayTag Burning = Main(5, "Status.Burning");
        private static readonly GameplayTag Stun = Main(6, "Status.Stun");

        [SetUp]
        public void ActivateMain() => Activate(MainBuilder());



        #region GameplayTag
        [Test]
        public void MatchesTag_IsHierarchical()
        {
            Assert.IsTrue(Projectile.MatchesTag(Projectile));
            Assert.IsTrue(Projectile.MatchesTag(Fire));
            Assert.IsTrue(Projectile.MatchesTag(Ability));
            Assert.IsFalse(Fire.MatchesTag(Projectile));
            Assert.IsFalse(Projectile.MatchesTag(Status));
        }

        [Test]
        public void UnresolvedTags_NeverMatch_EvenEachOther()
        {
            var deletedA = Main(90, "Gone");
            var deletedB = Main(90, "Gone");
            var missingRegistry = Tag(PackageRegistry, 1, "Pkg");

            Assert.IsFalse(deletedA.MatchesTagExact(deletedB));
            Assert.IsFalse(deletedA.MatchesTag(deletedB));
            Assert.IsFalse(missingRegistry.MatchesTagExact(missingRegistry));
            Assert.IsFalse(GameplayTag.None.MatchesTagExact(GameplayTag.None));
            Assert.IsFalse(Fire.MatchesTag(GameplayTag.None));

            // Data equality still holds.
            Assert.IsTrue(deletedA.Equals(deletedB));
            Assert.AreEqual(deletedA.GetHashCode(), deletedB.GetHashCode());
        }

        [Test]
        public void Equality_IgnoresStoredName()
        {
            Assert.IsTrue(Main(2, "Old.Name").Equals(Fire));
            Assert.IsTrue(Main(2, "Old.Name").MatchesTagExact(Fire));
        }
        #endregion



        #region Container
        [Test]
        public void Container_HierarchicalAndExactQueries()
        {
            var container = new GameplayTagContainer(Projectile, Burning);

            Assert.IsTrue(container.HasTag(Projectile));
            Assert.IsTrue(container.HasTag(Fire));
            Assert.IsTrue(container.HasTag(Ability));
            Assert.IsTrue(container.HasTag(Status));
            Assert.IsFalse(container.HasTag(Stun));

            Assert.IsTrue(container.HasTagExact(Projectile));
            Assert.IsFalse(container.HasTagExact(Fire));

            Assert.IsTrue(container.HasAll(new GameplayTagContainer(Fire, Status)));
            Assert.IsFalse(container.HasAllExact(new GameplayTagContainer(Fire, Status)));
            Assert.IsTrue(container.HasAny(new GameplayTagContainer(Stun, Ability)));
            Assert.IsFalse(container.HasAnyExact(new GameplayTagContainer(Stun, Ability)));
            Assert.IsTrue(container.HasAll(new GameplayTagContainer()));
            Assert.IsFalse(container.HasAny(new GameplayTagContainer()));
        }

        [Test]
        public void Container_AddRemove()
        {
            var container = new GameplayTagContainer();

            Assert.IsTrue(container.AddTag(Fire));
            Assert.IsFalse(container.AddTag(Main(2, "Renamed")), "data equality deduplicates");
            Assert.IsFalse(container.AddTag(GameplayTag.None));
            Assert.IsTrue(container.AddTag(Projectile));
            Assert.AreEqual(2, container.Count);

            Assert.IsTrue(container.RemoveTag(Fire, _IncludeChildren: true));
            Assert.IsTrue(container.IsEmpty);
        }

        [Test]
        public void Container_UnresolvedEntriesAreKeptButIgnored()
        {
            var deleted = Main(90, "Gone");
            var container = new GameplayTagContainer(deleted, Fire);

            Assert.AreEqual(2, container.Count);
            Assert.IsFalse(container.HasTag(deleted));
            Assert.IsFalse(container.HasTagExact(deleted));
            Assert.IsTrue(container.HasTag(Ability));
        }

        [Test]
        public void Container_NoneEntriesAreNotCountedOrEnumerated()
        {
            var container = new GameplayTagContainer(Fire, Stun);
            GetSerializedList(container).Insert(1, GameplayTag.None);

            Assert.AreEqual(2, container.Count);

            var enumerated = new List<GameplayTag>();
            foreach (var tag in container)
                enumerated.Add(tag);

            CollectionAssert.AreEqual(new[] { Fire, Stun }, enumerated);
            Assert.AreEqual("Ability.Fire, Status.Stun", container.ToString());
        }

        [Test]
        public void Container_CacheFollowsActiveDatabase()
        {
            var container = new GameplayTagContainer(Projectile);
            Assert.IsTrue(container.HasTag(Fire));

            // A new database where "Fire" (2) no longer exists, so "Projectile" (3) is gone too.
            Activate(new GameplayTagDatabaseBuilder().AddRegistry(MainRegistry, "Main", TagRegistryKind.Main,
                new[] { Row(1, 0, "Ability"), Row(4, 0, "Status") }));

            Assert.IsFalse(container.HasTag(Fire));
            Assert.IsFalse(container.HasTag(Ability));
        }

        [Test]
        public void Container_NothingIsCachedBeforeADatabaseIsReady()
        {
            var database = GameplayTagDatabase.Active;
            var container = new GameplayTagContainer(Projectile);

            GameplayTagDatabase.SetActive(null);
            Assert.IsFalse(container.HasTag(Fire));

            GameplayTagDatabase.SetActive(database);
            Assert.IsTrue(container.HasTag(Fire));
        }

        [Test]
        public void Container_GetMatchingTags()
        {
            var container = new GameplayTagContainer(Projectile, Burning, Stun);
            var result = new GameplayTagContainer();

            container.GetMatchingTags(new GameplayTagContainer(Ability), result);

            Assert.AreEqual(1, result.Count);
            Assert.IsTrue(result.HasTagExact(Projectile));
        }

        private static List<GameplayTag> GetSerializedList(GameplayTagContainer _Container)
            => (List<GameplayTag>)typeof(GameplayTagContainer)
                .GetField("_tags", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_Container);
        #endregion
    }
}
