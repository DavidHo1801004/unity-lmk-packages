using System;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using LMK.Runtime.AbilitySystem;

using static LMK.Editor.AbilitySystem.Tests.TestAttributes;

using Unit = LMK.Runtime.AbilitySystem.GameplayAbilitySystem;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// The world: stepping, units, handles, settings, time and randomness, and the mutator guards.
    /// </summary>
    public sealed class WorldTests
    {
        private TestWorld w;

        [SetUp]
        public void SetUp() => w = new TestWorld();

        [TearDown]
        public void TearDown() => w.Dispose();



        #region Stepping
        [Test]
        public void Advance_StepsInEqualSlicesOfAtMostMaxStep_AndMovesTheClock()
        {
            var steps = new List<double>();
            w.Stepped += steps.Add;

            w.Advance(0.25);

            Assert.AreEqual(3, steps.Count);
            Assert.IsTrue(steps.All(e => e <= w.MaxStep));
            Assert.AreEqual(0.25, w.World.Time, 1e-12);
            Assert.AreEqual(w.Clock.Time, w.World.Time);
        }

        [Test]
        public void Step_RefusesMoreThanMaxStep_AndBadTimes()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => w.World.Step(0.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => w.World.Step(-0.01));
            Assert.Throws<ArgumentOutOfRangeException>(() => w.World.Advance(double.NaN));
            Assert.AreEqual(0, w.World.Time);
        }

        [Test]
        public void UnitsCreatedOrShutDownDuringAStep_JoinOrLeaveAfterIt()
        {
            var first = w.MakeUnit((A, 0f));
            var modifier = first.AddModifier(A, ModifierOp.AddFinal, 0f);
            Unit created = null;
            bool ran = false;

            first.Attributes.Propagated += (_, _, _) =>
            {
                if (ran) return;
                ran = true;

                created = w.World.CreateSystem("Created");
                first.Shutdown();
                CollectionAssert.Contains(w.World.Systems, first, "its slot waits for the end of the step");
                CollectionAssert.DoesNotContain(w.World.Systems, created, "it joins after the step");
            };

            // A change left queued, as a tick leaves it: the world's flush propagates it inside the step.
            first.Attributes.SetModifierMagnitude(modifier, 1f);
            w.World.Step(0.05);

            Assert.IsTrue(ran);
            CollectionAssert.AreEqual(new[] { created }, w.World.Systems);
        }

        [Test]
        public void Handles_AreNeverReused_AcrossUnits()
        {
            var first = w.MakeUnit((A, 0f));
            var second = w.MakeUnit((A, 0f));

            var a = first.AddModifier(A, ModifierOp.AddFinal, 1f);
            var b = second.AddModifier(A, ModifierOp.AddFinal, 1f);
            first.RemoveModifier(a);
            var c = first.AddModifier(A, ModifierOp.AddFinal, 1f);

            Assert.AreEqual(3, new[] { a, b, c }.Distinct().Count());
            Assert.IsFalse(second.RemoveModifier(a), "another unit's handle does nothing");
        }

        [Test]
        public void TwoWorlds_AreIndependent()
        {
            using var other = new AbilitySystemWorld(null, new ManualClock());
            var unit = other.CreateSystem();

            w.Advance(1);

            Assert.AreEqual(0, other.Time);
            CollectionAssert.DoesNotContain(w.World.Systems, unit);
        }
        #endregion



        #region Guards
        [Test]
        public void Shutdown_TurnsEveryMutatorIntoANoOp()
        {
            var unit = w.MakeUnit((MaxHealth, 100f), (Health, 80f));
            unit.Shutdown();

            Assert.IsTrue(unit.IsShuttingDown);
            Assert.IsFalse(unit.SetAttributeBase(Health, 10f));
            Assert.IsFalse(unit.AddAttributeSet(w.Set((Armor, 1f))));
            Assert.IsFalse(unit.ExecuteModifier(Health, ModifierOp.AddBase, -10f));
            Assert.IsFalse(unit.AddModifier(Health, ModifierOp.AddFinal, 1f).IsValid);
            Assert.AreEqual(0, unit.HandleGameplayEvent(default, default));
            Assert.AreEqual(80f, unit.GetCurrent(Health), "queries still answer");
            CollectionAssert.DoesNotContain(w.World.Systems, unit);
        }

        private sealed class NoAuthority : IAbilitySystemNetDriver
        {
            public bool HasAuthority(Unit _System) => false;

            public bool CanApplyEffects(Unit _System) => false;
        }

        [Test]
        public void WithoutAuthority_MutatorsDoNothing()
        {
            using var world = new AbilitySystemWorld(w.Settings, new ManualClock(), null, new NoAuthority());
            var unit = world.CreateSystem();

            Assert.IsFalse(unit.AddAttributeSet(w.Set((Health, 1f))));
            Assert.IsFalse(unit.HasAttribute(Health));
        }
        #endregion



        #region Settings, time and randomness
        [Test]
        public void Settings_HaveTheDesignDefaults_AndRefuseBadValues()
        {
            var settings = AbilitySystemSettings.CreateDefault();
            try
            {
                Assert.AreEqual(0.1, settings.MaxStep);
                Assert.AreEqual(0.02, settings.MinPeriod);
                Assert.AreEqual(8, settings.MaxPropagationPasses);
                Assert.AreEqual(32, settings.MaxApplicationDepth);
                Assert.AreEqual(8, settings.MaxActivationDepth);

                Assert.Throws<ArgumentOutOfRangeException>(() => settings.MaxStep = 0);
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.MinPeriod = double.PositiveInfinity);
                Assert.Throws<ArgumentOutOfRangeException>(() => settings.MaxPropagationPasses = 0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void AWorldWithoutSettings_UsesDefaults_AndReleasesThemWhenDisposed()
        {
            var world = new AbilitySystemWorld();
            var settings = world.Settings;

            Assert.AreEqual(AbilitySystemSettings.DEFAULT_MAX_STEP, settings.MaxStep);
            Assert.IsInstanceOf<ScaledClock>(world.Clock);
            Assert.IsInstanceOf<SinglePlayerNetDriver>(world.NetDriver);

            world.Dispose();
            Assert.IsTrue(settings == null, "the default settings are destroyed with the world");
            Assert.IsTrue(world.IsDisposed);
        }

        [Test]
        public void SeededRandom_RepeatsItsSequence_AndStaysInRange()
        {
            var a = new SeededRandom(42);
            var b = new SeededRandom(42);
            var c = new SeededRandom(43);

            var first = Enumerable.Range(0, 100).Select(_ => a.NextDouble()).ToList();
            CollectionAssert.AreEqual(first, Enumerable.Range(0, 100).Select(_ => b.NextDouble()));
            CollectionAssert.AreNotEqual(first, Enumerable.Range(0, 100).Select(_ => c.NextDouble()));
            Assert.IsTrue(first.All(e => e >= 0 && e < 1));

            for (int i = 0; i < 1000; i++)
            {
                int value = a.Range(-3, 4);
                Assert.That(value, Is.InRange(-3, 3));
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => a.Range(2, 2));
        }

        [Test]
        public void ScaledClock_OnlyMovesForward()
        {
            var clock = new ScaledClock();
            clock.Advance(0.5);

            Assert.AreEqual(0.5, clock.Time);
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-1));
        }
        #endregion
    }
}
