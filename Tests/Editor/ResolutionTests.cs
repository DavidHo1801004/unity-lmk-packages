using System.Collections.Generic;

using NUnit.Framework;

using LMK.Runtime.GameplayTags;

using static LMK.Editor.GameplayTags.Tests.TestIds;

namespace LMK.Editor.GameplayTags.Tests
{
    public sealed class ResolutionTests : DatabaseTestBase
    {
        [Test]
        public void LiveTag_ResolvesToItsPath()
        {
            var database = Activate(MainBuilder());
            var tag = Main(3, "Old.Name");

            var resolution = tag.Resolve();

            Assert.AreEqual(TagStatus.Live, resolution.Status);
            Assert.AreEqual("Ability.Fire.Projectile", database.GetPath(resolution.Handle));
            Assert.AreEqual("Ability.Fire.Projectile", tag.Path, "the path comes from the database, not the stored name");
        }

        [Test]
        public void UnknownId_InKnownRegistry_IsDeleted_WithNoNameFallback()
        {
            Activate(MainBuilder());

            // The stored name matches a live tag, but the ID doesn't: still deleted.
            var tag = Main(99, "Ability.Fire");

            Assert.AreEqual(TagStatus.Deleted, tag.Resolve().Status);
            Assert.AreEqual("Ability.Fire", tag.Path);
        }

        [Test]
        public void UnknownRegistry_IsMissingRegistry()
        {
            Activate(MainBuilder());

            var tag = Tag(PackageRegistry, 3, "Pkg.Tag");

            Assert.AreEqual(TagStatus.MissingRegistry, tag.Resolve().Status);
        }

        [Test]
        public void SameGuidInTwoRegistries_IsTwoTags()
        {
            var database = Activate(MainBuilder()
                .AddRegistry(PackageRegistry, "Package", TagRegistryKind.Package, new[] { Row(3, 0, "Vendor") }));

            var mainTag = Main(3, "");
            var packageTag = Tag(PackageRegistry, 3, "");

            Assert.AreEqual("Ability.Fire.Projectile", mainTag.Path);
            Assert.AreEqual("Vendor", packageTag.Path);
            Assert.AreNotEqual(mainTag.Handle, packageTag.Handle);
            Assert.IsFalse(mainTag.Equals(packageTag));
            Assert.IsFalse(mainTag.MatchesTagExact(packageTag));
            Assert.AreEqual(2, database.RegistryCount);
        }

        [Test]
        public void SamePathInTwoRegistries_NeverMatchesAcross()
        {
            Activate(MainBuilder()
                .AddRegistry(PackageRegistry, "Package", TagRegistryKind.Package, new[] { Row(10, 0, "Status"), Row(11, 10, "Stun") }));

            var mainStatus = Main(4, "Status");
            var packageStun = Tag(PackageRegistry, 11, "Status.Stun");
            var packageStatus = Tag(PackageRegistry, 10, "Status");

            Assert.IsFalse(packageStun.MatchesTag(mainStatus));
            Assert.IsTrue(packageStun.MatchesTag(packageStatus));
        }

        [Test]
        public void ZeroGuid_IsNone_NotMissing()
        {
            Activate(MainBuilder());

            var resolution = GameplayTag.None.Resolve();

            Assert.AreEqual(TagStatus.None, resolution.Status);
            Assert.IsFalse(resolution.IsMissing);
            Assert.IsTrue(GameplayTag.None.IsNone);
            Assert.AreEqual("None", GameplayTag.None.ToString());
        }

        [Test]
        public void NoActiveDatabase_AssignedTagIsMissingRegistry_AndGenerationIsZero()
        {
            GameplayTagDatabase.SetActive(null);

            Assert.AreEqual(0, GameplayTagDatabase.Generation);
            Assert.AreEqual(TagStatus.MissingRegistry, Main(3, "Ability.Fire.Projectile").Resolve().Status);
            Assert.AreEqual(TagStatus.None, GameplayTag.None.Resolve().Status);
        }

        [Test]
        public void SetActive_ChangesGeneration()
        {
            Activate(MainBuilder());
            int first = GameplayTagDatabase.Generation;

            Activate(MainBuilder());
            int second = GameplayTagDatabase.Generation;

            Assert.AreNotEqual(0, first);
            Assert.AreNotEqual(0, second);
            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void Parent_And_FromName()
        {
            Activate(MainBuilder());

            var projectile = GameplayTag.FromName("Ability.Fire.Projectile");

            Assert.IsTrue(projectile.IsLive);
            Assert.AreEqual(Id(3), projectile.Id);
            Assert.AreEqual(MainRegistry, projectile.RegistryId);
            Assert.AreEqual("Ability.Fire", projectile.Parent.Path);
            Assert.AreEqual(Id(1), projectile.Parent.Parent.Id);
            Assert.IsTrue(projectile.Parent.Parent.Parent.IsNone);
            Assert.IsTrue(GameplayTag.FromName("Ability.Missing").IsNone);
        }

        [Test]
        public void Database_HierarchyQueries()
        {
            var database = Activate(MainBuilder());
            var status = database.FindByPath(MainRegistry, "Status");

            var children = new List<TagHandle>();
            database.GetChildren(status, children);

            Assert.AreEqual(2, children.Count);
            Assert.AreEqual(0, database.GetDepth(status));
            Assert.IsTrue(database.IsImplicit(status));
            Assert.IsFalse(database.IsImplicit(children[0]));
            Assert.AreEqual(1, database.GetDepth(children[0]));
            Assert.AreEqual(MainRegistry, database.MainRegistryId);
        }
    }
}
