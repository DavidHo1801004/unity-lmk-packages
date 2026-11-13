using System;
using System.Collections.Generic;

using NUnit.Framework;
using UnityEngine;

using LMK.Runtime.GameplayTags;

using static LMK.Editor.GameplayTags.Tests.TestIds;
using Expr = LMK.Runtime.GameplayTags.GameplayTagQueryExpression;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// Tag queries against the main test registry: Ability > Fire > Projectile, Status > Burning, Status > Stun.
    /// </summary>
    public sealed class QueryTests : DatabaseTestBase
    {
        private static GameplayTag Ability => Main(1, "Ability");
        private static GameplayTag Fire => Main(2, "Ability.Fire");
        private static GameplayTag Projectile => Main(3, "Ability.Fire.Projectile");
        private static GameplayTag Status => Main(4, "Status");
        private static GameplayTag Burning => Main(5, "Status.Burning");
        private static GameplayTag Stun => Main(6, "Status.Stun");

        [Serializable]
        private sealed class Holder
        {
            public GameplayTagQuery query = new();
        }

        [SetUp]
        public void ActivateMain() => Activate(MainBuilder());

        private static bool Matches(Expr _Expression, params GameplayTag[] _Tags)
            => GameplayTagQuery.Build(_Expression).Matches(new GameplayTagContainer(_Tags));



        [Test]
        public void TagOperators_AreHierarchical_UnlessExact()
        {
            Assert.IsTrue(Matches(Expr.AnyTags(Fire, Stun), Projectile), "a set holding Ability.Fire.Projectile has Ability.Fire");
            Assert.IsFalse(Matches(Expr.AnyTagsExact(Fire, Stun), Projectile));
            Assert.IsTrue(Matches(Expr.AnyTagsExact(Fire, Stun), Stun));

            Assert.IsTrue(Matches(Expr.AllTags(Ability, Status), Projectile, Burning));
            Assert.IsFalse(Matches(Expr.AllTagsExact(Ability, Status), Projectile, Burning));
            Assert.IsFalse(Matches(Expr.AllTags(Ability, Stun), Projectile, Burning));

            Assert.IsTrue(Matches(Expr.NoTags(Stun), Burning));
            Assert.IsFalse(Matches(Expr.NoTags(Status), Burning), "Status.Burning is a Status");
            Assert.IsTrue(Matches(Expr.NoTagsExact(Status), Burning));
        }

        [Test]
        public void Expressions_Nest()
        {
            // Burning or stunned, and not using an ability.
            var query = GameplayTagQuery.Build(Expr.All(
                Expr.AnyTags(Burning, Stun),
                Expr.None(Expr.AnyTags(Ability))));

            Assert.IsTrue(query.Matches(new GameplayTagContainer(Burning)));
            Assert.IsTrue(query.Matches(new GameplayTagContainer(Stun, Burning)));
            Assert.IsFalse(query.Matches(new GameplayTagContainer(Stun, Fire)));
            Assert.IsFalse(query.Matches(new GameplayTagContainer()));

            Assert.IsTrue(Matches(Expr.Any(Expr.AllTags(Fire, Stun), Expr.AnyTags(Burning)), Burning));
            Assert.IsFalse(Matches(Expr.Any(Expr.AllTags(Fire, Stun), Expr.AnyTags(Burning)), Fire));
        }

        [Test]
        public void EmptyParts_FollowAnyAllNone_AndAnEmptyQueryMatchesNothing()
        {
            var set = new GameplayTagContainer(Burning);

            Assert.IsFalse(Matches(Expr.AnyTags(), Burning));
            Assert.IsTrue(Matches(Expr.AllTags(), Burning));
            Assert.IsTrue(Matches(Expr.NoTags(), Burning));
            Assert.IsFalse(Matches(Expr.Any(), Burning));
            Assert.IsTrue(Matches(Expr.All(), Burning));
            Assert.IsTrue(Matches(Expr.None(), Burning));

            var empty = new GameplayTagQuery();
            Assert.IsTrue(empty.IsEmpty);
            Assert.IsFalse(empty.Matches(set));
            Assert.IsNull(empty.ToExpression());
        }

        [Test]
        public void UnresolvedTags_MatchNothing()
        {
            var deleted = Main(99, "Status.Gone");

            Assert.IsFalse(Matches(Expr.AnyTags(deleted), deleted));
            Assert.IsFalse(Matches(Expr.AllTags(Burning, deleted), Burning, deleted));
            Assert.IsTrue(Matches(Expr.NoTags(deleted), Burning));
        }

        [Test]
        public void ContainerShortcuts_AndDescription()
        {
            var tags = new GameplayTagContainer(Burning, Stun);

            Assert.IsTrue(GameplayTagQuery.MatchAnyTags(tags).Matches(new GameplayTagContainer(Stun)));
            Assert.IsFalse(GameplayTagQuery.MatchAllTags(tags).Matches(new GameplayTagContainer(Stun)));
            Assert.IsTrue(GameplayTagQuery.MatchNoTags(tags).Matches(new GameplayTagContainer(Fire)));

            var query = GameplayTagQuery.Build(Expr.All(Expr.AnyTags(Burning, Stun), Expr.NoTagsExact(Fire)), "Disabled, not casting");
            Assert.AreEqual("ALL(ANY(Status.Burning, Status.Stun), NONE EXACT(Ability.Fire))", query.ToString());
            Assert.AreEqual("Disabled, not casting", query.Description);
        }

        [Test]
        public void Serialization_RoundTripsTheExpressionTree()
        {
            var holder = new Holder
            {
                query = GameplayTagQuery.Build(Expr.All(Expr.AnyTags(Burning, Stun), Expr.None(Expr.AllTagsExact(Fire, Projectile))), "Test"),
            };

            var loaded = JsonUtility.FromJson<Holder>(JsonUtility.ToJson(holder));

            Assert.AreEqual(holder.query.ToString(), loaded.query.ToString());
            Assert.AreEqual("Test", loaded.query.Description);
            Assert.IsTrue(loaded.query.Matches(new GameplayTagContainer(Stun, Fire)));
            Assert.IsFalse(loaded.query.Matches(new GameplayTagContainer(Stun, Fire, Projectile)));

            var root = loaded.query.ToExpression();
            Assert.AreEqual(GameplayTagQueryOperator.AllExprMatch, root.Operator);
            Assert.AreEqual(2, root.Expressions.Count);
            CollectionAssert.AreEqual(new List<GameplayTag> { Burning, Stun }, root.Expressions[0].Tags);
        }

        [Test]
        public void SerializedCycles_AreIgnored()
        {
            // A hand-edited file whose root lists itself: the bad index is skipped instead of looping forever.
            var holder = new Holder { query = GameplayTagQuery.Build(Expr.Any(Expr.AnyTags(Burning))) };
            string json = JsonUtility.ToJson(holder).Replace("\"children\":[1]", "\"children\":[0,1]");

            var loaded = JsonUtility.FromJson<Holder>(json);

            Assert.IsTrue(loaded.query.Matches(new GameplayTagContainer(Burning)));
            Assert.AreEqual("ANY(ANY(Status.Burning))", loaded.query.ToString());
        }

        [Test]
        public void OperatorsCheckWhatTheyCombine()
        {
            Assert.Throws<ArgumentException>(() => Expr.OfTags(GameplayTagQueryOperator.AllExprMatch, Burning));
            Assert.Throws<ArgumentException>(() => Expr.OfExpressions(GameplayTagQueryOperator.AnyTagsMatch));
        }
    }
}
