using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using UnityEngine;

using LMK.Runtime.AbilitySystem;
using LMK.Runtime.GameplayTags;

using static LMK.Editor.AbilitySystem.Tests.TestAttributes;
using static LMK.Runtime.AbilitySystem.ModifierOp;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// The full effect feature set, and its edge cases.
    /// </summary>
    public sealed class EffectsCompleteTests
    {
        private TestWorld w;

        [SetUp]
        public void SetUp() => w = new TestWorld();

        [TearDown]
        public void TearDown() => w.Dispose();

        private static GameplayTag X => TestTags.Shielded;



        #region Edge cases
        [Test]
        public void LiveCaptureCycle_StopsAtPassLimit()
        {
            var unit = w.MakeUnit((A, 10f), (B, 10f));
            unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Mod(A, AddBase, Magnitudes.AttributeBased(CaptureSource.Target, B, true))));

            w.Log.Expect(LogType.Error, "attribute cycle");
            unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Mod(B, AddBase, Magnitudes.AttributeBased(CaptureSource.Target, A, true))));

            // Reaching this line proves there was no hang.
            Assert.IsTrue(float.IsFinite(unit.GetCurrent(A)) && float.IsFinite(unit.GetCurrent(B)));
            Assert.AreEqual(1, w.Log.ErrorCount);
        }

        [Test]
        public void DurationModifierOnAMetaAttribute_IsAnErrorAndIgnored()
        {
            var effect = w.Effect(e => e.Duration(5).Mod(IncomingDamage, AddBase, 10));
            Assert.IsTrue(EffectValidator.Validate(effect).HasError(EffectIssueCode.MetaAttributeOnDurationEffect));

            var unit = w.MakeUnit((IncomingDamage, 0f));
            unit.ApplyEffectToSelf(effect);
            Assert.AreEqual(0f, unit.GetCurrent(IncomingDamage));

            var periodic = w.Effect(e => e.Duration(5).Period(1).Mod(IncomingDamage, AddBase, 10));
            Assert.IsFalse(EffectValidator.Validate(periodic).HasError(EffectIssueCode.MetaAttributeOnDurationEffect), "periods execute: allowed");
        }

        [Test]
        public void StackLimit_Overflow_Expiry_Sources()
        {
            var overflow = w.Effect(e => e.Instant().Mod(Health, AddBase, -5));
            var curse = w.Effect(e => e.Duration(6).Mod(Armor, AddBase, -2)
                                       .Stack(StackingType.AggregateBySource, 3, StackExpirationPolicy.RemoveSingleStackAndRefreshDuration).Overflow(overflow));
            var src = w.MakeUnit();
            var unit = w.MakeUnit((Health, 100f), (Armor, 10f));

            var h = ActiveEffectHandle.Invalid;
            for (int i = 0; i < 4; i++)
                h = src.ApplySpecToTarget(src.MakeOutgoingSpec(curse), unit);

            Assert.AreEqual(1, unit.GetActiveEffects().Count);
            Assert.AreEqual(3, unit.GetActiveEffect(h).StackCount);
            Assert.AreEqual(4f, unit.GetCurrent(Armor), "10 - 2 x 3");
            Assert.AreEqual(95f, unit.GetCurrent(Health), "the overflow effect ran once");

            w.Advance(6);
            Assert.AreEqual(2, unit.GetActiveEffect(h).StackCount);
            Assert.AreEqual(6f, unit.GetCurrent(Armor));

            w.Advance(12);
            Assert.AreEqual(0, unit.GetActiveEffects().Count);
            Assert.AreEqual(10f, unit.GetCurrent(Armor));

            var other = w.MakeUnit();
            src.ApplySpecToTarget(src.MakeOutgoingSpec(curse), unit);
            other.ApplySpecToTarget(other.MakeOutgoingSpec(curse), unit);
            Assert.AreEqual(2, unit.GetActiveEffects().Count, "one stack per source");
        }

        [Test]
        public void ASecondApplication_ReplacesTheStoredSpec()
        {
            var src = w.MakeUnit((AttackPower, 10f));
            var unit = w.MakeUnit((Armor, 0f));
            var aura = w.Effect(e => e.Infinite().Mod(Armor, AddFinal, Magnitudes.AttributeBased(CaptureSource.Source, AttackPower, false))
                                      .Stack(StackingType.AggregateByTarget));

            var h = src.ApplySpecToTarget(src.MakeOutgoingSpec(aura, 1f), unit);
            Assert.AreEqual(10f, unit.GetCurrent(Armor));

            src.SetAttributeBase(AttackPower, 20f);
            src.ApplySpecToTarget(src.MakeOutgoingSpec(aura, 2f), unit);

            var active = unit.GetActiveEffect(h);
            Assert.AreEqual(2, active.StackCount);
            Assert.AreEqual(2f, active.Spec.Level, "the latest level");
            Assert.AreEqual(40f, unit.GetCurrent(Armor), "every stack uses the latest snapshot: 20 x 2");
        }

        [Test]
        public void SelfInhibition_Terminates()
        {
            var ge = w.Effect(e => e.Infinite().GrantTags(X).Ongoing(X));
            Assert.IsTrue(EffectValidator.Validate(ge).HasError(EffectIssueCode.SelfInhibition));

            var unit = w.MakeUnit();
            w.Log.Expect(LogType.Error, "propagation pass limit");
            var h = unit.ApplyEffectToSelf(ge);                                             // returns: no hang

            Assert.IsTrue(unit.GetActiveEffect(h).IsInhibited);
            Assert.IsFalse(unit.HasTag(X));
        }

        [Test]
        public void InhibitAndRelease_RoundTrips()
        {
            var buff = w.Effect(e => e.Duration(10).Mod(AttackPower, AddBase, 5).GrantTags(TestTags.Empowered).Ongoing(TestTags.Silenced));
            var unit = w.MakeUnit((AttackPower, 10f));
            var h = unit.ApplyEffectToSelf(buff);
            var changes = new List<bool>();
            unit.OnInhibitionChanged += (_, inhibited) => changes.Add(inhibited);

            unit.AddLooseTag(TestTags.Silenced);
            Assert.AreEqual(10f, unit.GetCurrent(AttackPower));
            Assert.IsFalse(unit.HasTag(TestTags.Empowered));

            w.Advance(4);
            unit.RemoveLooseTag(TestTags.Silenced);

            Assert.AreEqual(15f, unit.GetCurrent(AttackPower));
            Assert.IsTrue(unit.HasTag(TestTags.Empowered));
            Assert.AreEqual(6, unit.GetActiveEffect(h).TimeRemaining(), 1e-6, "the duration ran while inhibited");
            CollectionAssert.AreEqual(new[] { true, false }, changes);
        }

        [Test]
        public void SourceDestroyed_EffectSurvives()
        {
            var src = w.MakeUnit((AttackPower, 20f));
            var unit = w.MakeUnit((Health, 100f));
            var dot = w.Effect(e => e.Duration(4).Period(1).Mod(Health, AddBase, Magnitudes.AttributeBased(CaptureSource.Source, AttackPower, true, -0.5f)));

            src.ApplySpecToTarget(src.MakeOutgoingSpec(dot), unit);
            w.Advance(1);
            Assert.AreEqual(90f, unit.GetCurrent(Health));

            src.Shutdown();
            w.Advance(3);

            Assert.AreEqual(60f, unit.GetCurrent(Health), "the capture froze at 20: still 10 per period");
            Assert.IsNull(w.Events.Removed.Last().Effect.Spec.Context.Instigator);
        }

        [Test]
        public void MissingSetByCaller_IsZeroWithAWarning()
        {
            var unit = w.MakeUnit((Health, 100f));

            w.Log.Expect(LogType.Warning, "no SetByCaller value");
            unit.ApplyEffectToSelf(w.Effect(e => e.Instant().Mod(Health, AddBase, Magnitudes.SetByCaller(TestTags.DataDamage))));

            Assert.AreEqual(100f, unit.GetCurrent(Health));
        }

        [Test]
        public void TwoOverrides_FirstAppliedWins_ThenTheSecond()
        {
            var unit = w.MakeUnit((AttackPower, 10f));
            var first = unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Mod(AttackPower, Override, 50)));
            unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Mod(AttackPower, Override, 80)));
            Assert.AreEqual(50f, unit.GetCurrent(AttackPower));

            unit.RemoveActiveEffect(first);
            Assert.AreEqual(80f, unit.GetCurrent(AttackPower));
        }

        [Test]
        public void Immunity_BlocksOnlyNewApplications()
        {
            var unit = w.MakeUnit((AttackPower, 10f));
            var buff = w.Effect(e => e.Infinite().AssetTags(TestTags.Buff).Mod(AttackPower, AddBase, 5));
            var existing = unit.ApplyEffectToSelf(buff);
            ActiveGameplayEffect blocker = null;
            unit.OnImmunityBlocked += (_, by) => blocker = by;

            var ward = unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Immunity(GameplayEffectQuery.MatchingAnyAssetTag(TestTags.Buff))));

            Assert.IsNotNull(unit.GetActiveEffect(existing), "the existing effect stays");
            Assert.AreEqual(15f, unit.GetCurrent(AttackPower));
            Assert.AreEqual(ActiveEffectHandle.Invalid, unit.ApplyEffectToSelf(buff), "new applications are blocked");
            Assert.AreEqual(ward, blocker.Handle);
            Assert.AreEqual(15f, unit.GetCurrent(AttackPower));
        }
        #endregion



        #region Stacking
        [Test]
        public void StackedPeriodicEffect_ExecutesFoldedByTheCount_AndKeepsItsSchedule()
        {
            var unit = w.MakeUnit((Health, 100f));
            var burn = w.Effect(e => e.Duration(6).Period(1).Mod(Health, AddBase, -4).Stack(StackingType.AggregateByTarget, 3));

            unit.ApplyEffectToSelf(burn);
            w.Advance(2);                                                                   // -4 at 1 and 2
            unit.ApplyEffectToSelf(burn);                                                   // 2 stacks, ends at 8
            w.Advance(10);

            Assert.AreEqual(100f - 8f - 6 * 8f, unit.GetCurrent(Health), "six more periods of 4 x 2");
        }

        [Test]
        public void DenyOverflow_FailsTheApplication_AndClearOnOverflowRemovesTheStack()
        {
            var unit = w.MakeUnit((Armor, 10f));
            var deny = w.Effect(e => e.Infinite().Mod(Armor, AddBase, -1).Stack(StackingType.AggregateByTarget, 2, _DenyOverflow: true));
            var clear = w.Effect(e => e.Infinite().Mod(Armor, AddBase, -1).Stack(StackingType.AggregateByTarget, 2, _DenyOverflow: true, _ClearOnOverflow: true));

            unit.ApplyEffectToSelf(deny);
            unit.ApplyEffectToSelf(deny);
            Assert.AreEqual(ActiveEffectHandle.Invalid, unit.ApplyEffectToSelf(deny));
            Assert.AreEqual(8f, unit.GetCurrent(Armor));

            unit.ApplyEffectToSelf(clear);
            unit.ApplyEffectToSelf(clear);
            Assert.AreEqual(6f, unit.GetCurrent(Armor));
            Assert.AreEqual(ActiveEffectHandle.Invalid, unit.ApplyEffectToSelf(clear));
            Assert.AreEqual(8f, unit.GetCurrent(Armor), "the whole stack went");
        }

        [Test]
        public void NeverRefresh_KeepsTheEndTime_AndRefreshDurationExpiryKeepsTheEffect()
        {
            var unit = w.MakeUnit((Armor, 10f));
            var plain = w.Effect(e => e.Duration(4).Mod(Armor, AddBase, 1).Stack(StackingType.AggregateByTarget, 0, _Refresh: StackDurationRefreshPolicy.NeverRefresh));
            var h = unit.ApplyEffectToSelf(plain);
            w.Advance(2);
            unit.ApplyEffectToSelf(plain);
            Assert.AreEqual(2, unit.GetActiveEffect(h).TimeRemaining(), 1e-6);

            var lasting = w.Effect(e => e.Duration(1).Mod(Armor, AddBase, 1).Stack(StackingType.AggregateByTarget, 0, StackExpirationPolicy.RefreshDuration));
            var expired = new List<ActiveEffectHandle>();
            unit.OnStackExpired += expired.Add;
            var kept = unit.ApplyEffectToSelf(lasting);
            w.Advance(1);

            Assert.IsNotNull(unit.GetActiveEffect(kept), "the effect stays; game code decides");
            CollectionAssert.AreEqual(new[] { kept }, expired);
            Assert.AreEqual(1, unit.GetActiveEffect(kept).TimeRemaining(), 1e-6);
        }

        [Test]
        public void RemovingSomeStacks_LowersTheCount()
        {
            var unit = w.MakeUnit((Armor, 10f));
            var curse = w.Effect(e => e.Infinite().Mod(Armor, AddBase, -2).Stack(StackingType.AggregateByTarget));
            var changes = new List<(int, int)>();
            unit.OnStackChanged += (_, old, current) => changes.Add((old, current));

            var h = unit.ApplyEffectToSelf(curse);
            unit.ApplyEffectToSelf(curse);
            unit.ApplyEffectToSelf(curse);
            Assert.AreEqual(4f, unit.GetCurrent(Armor));

            Assert.IsTrue(unit.RemoveActiveEffect(h, 2));
            Assert.AreEqual(8f, unit.GetCurrent(Armor));
            CollectionAssert.AreEqual(new[] { (1, 2), (2, 3), (3, 1) }, changes);
        }
        #endregion



        #region Requirements, inhibition and periods
        [Test]
        public void ApplicationAndRemovalRequirements()
        {
            var unit = w.MakeUnit((Armor, 10f));
            var needsShield = w.Effect(e => e.Infinite().Mod(Armor, AddBase, 5).Requires(TestTags.Shielded));
            Assert.AreEqual(ActiveEffectHandle.Invalid, unit.ApplyEffectToSelf(needsShield));

            var fragile = w.Effect(e => e.Infinite().Mod(Armor, AddBase, 5).RemovedBy(TestTags.Silenced));
            var h = unit.ApplyEffectToSelf(fragile);
            Assert.AreEqual(15f, unit.GetCurrent(Armor));

            unit.AddLooseTag(TestTags.Silenced);
            Assert.IsNull(unit.GetActiveEffect(h));
            Assert.IsTrue(w.Events.Removed.Single().Info.Premature);
            Assert.AreEqual(ActiveEffectHandle.Invalid, unit.ApplyEffectToSelf(fragile), "its removal requirements already match");
        }

        [TestCase(PeriodicInhibitionPolicy.NeverReset, 6)]
        [TestCase(PeriodicInhibitionPolicy.ResetPeriod, 5)]
        [TestCase(PeriodicInhibitionPolicy.ExecuteAndResetPeriod, 6)]
        public void PeriodicInhibitionPolicies(PeriodicInhibitionPolicy _Policy, int _Executions)
        {
            var unit = w.MakeUnit((Health, 100f));
            unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Period(1).Mod(Health, AddBase, -1).Ongoing(TestTags.Silenced).InhibitionPolicy(_Policy)));

            w.Advance(2);                                                                   // 2 executions
            unit.AddLooseTag(TestTags.Silenced);
            w.Advance(2.5);                                                                 // none while inhibited
            unit.RemoveLooseTag(TestTags.Silenced);                                         // released at 4.5
            w.Advance(3.5);                                                                 // to 8

            // NeverReset: periods at 5, 6, 7, 8. ResetPeriod: 5.5, 6.5, 7.5. ExecuteAndReset: now, then 5.5, 6.5, 7.5.
            Assert.AreEqual(100f - _Executions, unit.GetCurrent(Health));
        }
        #endregion



        #region Other components
        [Test]
        public void CleanseAndImmunity_ByGrantedTags()
        {
            var unit = w.MakeUnit((Armor, 10f));
            var debuff = w.Effect(e => e.Duration(10).GrantTags(TestTags.Silenced).Mod(Armor, AddBase, -5));
            var cleanse = w.Effect(e => e.Duration(3)
                                         .RemoveOther(GameplayEffectQuery.MatchingAnyGrantedTag(TestTags.Silenced))
                                         .Immunity(GameplayEffectQuery.MatchingAnyGrantedTag(TestTags.Silenced)));

            unit.ApplyEffectToSelf(debuff);
            unit.ApplyEffectToSelf(debuff);
            Assert.AreEqual(0f, unit.GetCurrent(Armor));

            unit.ApplyEffectToSelf(cleanse);
            Assert.AreEqual(10f, unit.GetCurrent(Armor), "both debuffs removed");
            Assert.IsTrue(w.Events.Removed.All(e => e.Info.Premature));
            Assert.AreEqual(ActiveEffectHandle.Invalid, unit.ApplyEffectToSelf(debuff), "immune for 3 s");

            w.Advance(3);
            Assert.IsTrue(unit.ApplyEffectToSelf(debuff).IsValid);
        }

        [Test]
        public void ChanceToApply_RollsTheWorldsRandomSource()
        {
            var unit = w.MakeUnit((Armor, 0f));
            GameplayEffect Chance(float _Chance) => w.Effect(e => e.Instant().Mod(Armor, AddBase, 1).Add(new ChanceToApplyComponent { chance = _Chance }));

            unit.ApplyEffectToSelf(Chance(0f));
            Assert.AreEqual(0f, unit.GetCurrent(Armor));

            var half = Chance(0.5f);
            for (int i = 0; i < 200; i++)
                unit.ApplyEffectToSelf(half);

            Assert.That(unit.GetCurrent(Armor), Is.InRange(70f, 130f));
        }

        private sealed class OnlyWithArmor : CustomApplicationRequirement
        {
            public override bool CanApply(LMK.Runtime.AbilitySystem.GameplayAbilitySystem _Target, GameplayEffectSpec _Spec) => _Target.HasAttribute(Armor);
        }

        [Test]
        public void CustomCanApply_RunsGameCode()
        {
            var effect = w.Effect(e => e.Instant().Mod(Health, AddBase, -1).Add(new CustomCanApplyComponent { requirements = { new OnlyWithArmor() } }));
            var plain = w.MakeUnit((Health, 100f));
            var armored = w.MakeUnit((Health, 100f), (Armor, 1f));

            plain.ApplyEffectToSelf(effect);
            armored.ApplyEffectToSelf(effect);

            Assert.AreEqual(100f, plain.GetCurrent(Health));
            Assert.AreEqual(99f, armored.GetCurrent(Health));
        }
        #endregion



        #region Captures and calculations
        [Test]
        public void SourceSnapshot_IsTakenWhenTheSpecIsMade()
        {
            var src = w.MakeUnit((AttackPower, 10f));
            var unit = w.MakeUnit((Health, 100f));
            var hit = w.Effect(e => e.Instant().Mod(Health, AddBase, Magnitudes.AttributeBased(CaptureSource.Source, AttackPower, false, -1f)));

            var spec = src.MakeOutgoingSpec(hit);
            src.SetAttributeBase(AttackPower, 50f);
            src.ApplySpecToTarget(spec, unit);

            Assert.AreEqual(90f, unit.GetCurrent(Health), "10, as when the spec was made");
        }

        [Test]
        public void LiveTargetCapture_FollowsTheAttribute()
        {
            var unit = w.MakeUnit((MaxHealth, 100f), (Health, 100f), (AttackPower, 10f));
            var axe = unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Mod(AttackPower, AddBase, Magnitudes.AttributeBased(CaptureSource.Target, MaxHealth, true, 0.1f))));
            Assert.AreEqual(20f, unit.GetCurrent(AttackPower), "MaxHealth 100 x 0.1");

            var buff = unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Mod(MaxHealth, AddBase, 50)));
            Assert.AreEqual(25f, unit.GetCurrent(AttackPower), "the live capture recalculated");

            unit.RemoveActiveEffect(buff);
            Assert.AreEqual(20f, unit.GetCurrent(AttackPower));
            unit.RemoveActiveEffect(axe);
            Assert.AreEqual(10f, unit.GetCurrent(AttackPower));
        }

        [Test]
        public void TargetSnapshot_IsTakenOnApplication()
        {
            var unit = w.MakeUnit((MaxHealth, 100f), (AttackPower, 10f));
            unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Mod(AttackPower, AddBase, Magnitudes.AttributeBased(CaptureSource.Target, MaxHealth, false, 0.1f))));

            unit.SetAttributeBase(MaxHealth, 300f);

            Assert.AreEqual(20f, unit.GetCurrent(AttackPower), "the snapshot doesn't follow");
        }

        private sealed class HalfArmor : ModMagnitudeCalculation
        {
            public static readonly AttributeCaptureDefinition Armor = new(TestAttributes.Armor, CaptureSource.Target, true);

            public HalfArmor() => relevantAttributes.Add(Armor);

            public override float Calculate(GameplayEffectSpec _Spec) => TryGetCapturedMagnitude(_Spec, Armor, out float armor) ? armor * 0.5f : 0f;
        }

        [Test]
        public void CustomCalculation_WrappedInTheCoefficients()
        {
            var unit = w.MakeUnit((Armor, 20f), (AttackPower, 0f));

            unit.ApplyEffectToSelf(w.Effect(e => e.Instant().Mod(AttackPower, AddBase,
                new CustomCalculationMagnitude { calculation = new HalfArmor(), coefficient = 2f, preMultiplyAdd = 1f, postMultiplyAdd = 3f })));

            Assert.AreEqual(2f * (1f + 10f) + 3f, unit.GetCurrent(AttackPower));
        }

        private sealed class DamageExecution : EffectExecutionCalculation
        {
            public static readonly AttributeCaptureDefinition SourceAttackPower = new(AttackPower, CaptureSource.Source, true);
            public static readonly AttributeCaptureDefinition TargetArmor = new(Armor, CaptureSource.Target, false);

            public DamageExecution()
            {
                relevantAttributes.Add(SourceAttackPower);
                relevantAttributes.Add(TargetArmor);
            }

            public override void Execute(in ExecutionParams _Params, ExecutionOutput _Output)
            {
                _Params.TryGetCapturedMagnitude(SourceAttackPower, out float attackPower);
                _Params.TryGetCapturedMagnitude(TargetArmor, out float armor);
                _Params.Spec.TryGetSetByCaller(TestTags.DataDamage, out float baseDamage);

                float raw = baseDamage + attackPower;
                _Output.AddOutputModifier(IncomingDamage, AddBase, Mathf.Max(0f, raw * 100f / (100f + armor)));
                _Output.TriggerConditionalEffects();
            }
        }

        [Test]
        public void Execution_OutputsModifiers_AndAppliesConditionalEffects()
        {
            var mage = w.MakeUnit((AttackPower, 10f));
            var targetSet = w.Set((MaxHealth, 100f), (Health, 100f), (Armor, 25f), (IncomingDamage, null));
            targetSet.Add(new TransferMetaAttribute { from = IncomingDamage, to = Health });
            var target = w.MakeUnit(targetSet);

            var mark = w.Effect(e => e.Duration(5).GrantTags(TestTags.Silenced));
            var damage = w.Effect(e => e.Instant().Execution(new DamageExecution(),
                new ConditionalEffect { effect = mark },
                new ConditionalEffect { effect = mark, requiredSourceTags = new GameplayTagContainer(TestTags.Empowered) }));

            var spec = mage.MakeOutgoingSpec(damage).SetByCaller(TestTags.DataDamage, 20f);
            mage.ApplySpecToTarget(spec, target);

            Assert.AreEqual(76f, target.GetCurrent(Health), "(20 + 10) x 100 / 125 = 24");
            Assert.AreEqual(0f, target.GetCurrent(IncomingDamage));
            Assert.AreEqual(1, target.GetActiveEffects().Count, "the conditional effect whose source tags pass");
            Assert.IsTrue(target.HasTag(TestTags.Silenced));
        }

        private sealed class ArmorOnly : EffectExecutionCalculation
        {
            public static readonly AttributeCaptureDefinition TargetArmor = new(Armor, CaptureSource.Target, true);

            public ArmorOnly() => relevantAttributes.Add(TargetArmor);

            public override void Execute(in ExecutionParams _Params, ExecutionOutput _Output)
            {
                _Params.TryGetCapturedMagnitude(TargetArmor, out float armor);
                _Output.AddOutputModifier(Health, AddBase, -armor);
            }
        }

        [Test]
        public void ScopedModifiers_ChangeTheCapturedValueForOneExecution()
        {
            var unit = w.MakeUnit((Health, 100f), (Armor, 10f));
            var effect = w.Effect(e => e.Instant().Execution(new ArmorOnly()));
            effect.Executions[0].calculationModifiers.Add(new ScopedModifier { capture = ArmorOnly.TargetArmor, op = MultiplyAdditive, magnitude = new ScalableFloatMagnitude(2f) });

            unit.ApplyEffectToSelf(effect);

            Assert.AreEqual(80f, unit.GetCurrent(Health), "armor doubled for the execution only");
            Assert.AreEqual(10f, unit.GetCurrent(Armor));
        }
        #endregion



        #region Queries and validation
        [Test]
        public void RemoveActiveEffects_ByQuery()
        {
            var src = w.MakeUnit();
            var unit = w.MakeUnit((Armor, 0f));
            var buff = w.Effect(e => e.Infinite().AssetTags(TestTags.Buff).Mod(Armor, AddBase, 1));
            var other = w.Effect(e => e.Infinite().Mod(Armor, AddBase, 10));

            unit.ApplyEffectToSelf(buff);
            src.ApplySpecToTarget(src.MakeOutgoingSpec(buff), unit);
            unit.ApplyEffectToSelf(other);

            Assert.AreEqual(2, unit.GetActiveEffects(GameplayEffectQuery.MatchingAnyAssetTag(TestTags.Buff)).Count);
            Assert.AreEqual(1, unit.RemoveActiveEffects(new GameplayEffectQuery { source = src }));
            Assert.AreEqual(1, unit.RemoveActiveEffects(new GameplayEffectQuery { effectDefinition = buff }));
            Assert.AreEqual(0, unit.RemoveActiveEffects(new GameplayEffectQuery()), "an empty query matches nothing");
            Assert.AreEqual(10f, unit.GetCurrent(Armor));
        }

        [Test]
        public void Validator_SelfRemoval_ConditionalCycles_InstantRequirements_UnresolvedCaptures()
        {
            Assert.IsTrue(EffectValidator.Validate(w.Effect(e => e.Infinite().GrantTags(X).RemovedBy(X))).HasError(EffectIssueCode.SelfRemoval));

            var looping = w.Effect(e => e.Instant());
            looping.ExecutionList.Add(new ExecutionDefinition { calculation = new ArmorOnly(), conditionalEffects = { new ConditionalEffect { effect = looping } } });
            Assert.IsTrue(EffectValidator.Validate(looping).HasError(EffectIssueCode.ApplicationCycle));

            Assert.IsTrue(EffectValidator.Validate(w.Effect(e => e.Instant().Ongoing(X))).HasError(EffectIssueCode.InstantWithDurationFeatures));

            var ghost = GameplayAttribute.FromId(AttributeIds.MainRegistry, LMK.Runtime.Registry.RegistryGuid.NewGuid(), "Vitals.Ghost");
            var capturing = w.Effect(e => e.Instant().Mod(Health, AddBase, Magnitudes.AttributeBased(CaptureSource.Source, ghost, false)));
            Assert.IsTrue(EffectValidator.Validate(capturing).HasError(EffectIssueCode.UnresolvedAttribute));
        }
        #endregion
    }
}
