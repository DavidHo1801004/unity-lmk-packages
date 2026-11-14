using System;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using LMK.Runtime.AbilitySystem;

namespace LMK.Editor.AbilitySystem.Tests
{
    public sealed class HarnessTests
    {
        private TestWorld w;

        [SetUp]
        public void SetUp() => w = new TestWorld();

        [TearDown]
        public void TearDown() => w.Dispose();



        #region Clock
        [Test]
        public void ManualClock_StartsAtZero_AndOnlyMovesWhenAdvanced()
        {
            var clock = new ManualClock();
            Assert.AreEqual(0, clock.Time);

            clock.Advance(1.5);
            clock.Advance(0);
            Assert.AreEqual(1.5, clock.Time);
        }

        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void ManualClock_RejectsBackwardOrNonFiniteSteps(double _Seconds)
        {
            var clock = new ManualClock(2);
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(_Seconds));
            Assert.AreEqual(2, clock.Time);
        }
        #endregion



        #region Advance
        [Test]
        public void Advance_SplitsTimeIntoEqualStepsOfAtMostMaxStep()
        {
            var steps = new List<double>();
            w.Stepped += steps.Add;

            w.Advance(0.35);

            Assert.AreEqual(4, steps.Count);
            foreach (double step in steps) Assert.LessOrEqual(step, w.MaxStep);
            Assert.AreEqual(0.35, w.Clock.Time, 1e-12);
        }

        [Test]
        public void Advance_ExactMultipleOfMaxStep_TakesNoExtraStep()
        {
            int steps = 0;
            w.Stepped += _ => steps++;

            w.Advance(0.3);    // 0.3 / 0.1 is 2.9999999999999996 in doubles

            Assert.AreEqual(3, steps);
        }

        [Test]
        public void Advance_Zero_TakesNoStep()
        {
            int steps = 0;
            w.Stepped += _ => steps++;

            w.Advance(0);

            Assert.AreEqual(0, steps);
            Assert.AreEqual(0, w.Clock.Time);
        }
        #endregion



        #region Log
        [Test]
        public void Log_ExpectedError_IsCountedAndDoesNotFailTheTest()
        {
            w.Log.Expect(LogType.Error, "attribute cycle");
            Debug.LogError("Attribute cycle detected between A and B");

            Assert.AreEqual(1, w.Log.ErrorCount);
            Assert.AreEqual(0, w.Log.WarningCount);
        }
        #endregion
    }
}
