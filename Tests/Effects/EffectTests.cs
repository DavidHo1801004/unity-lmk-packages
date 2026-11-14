using System.Linq;

using NUnit.Framework;

using UnityEngine;

using LMK.Runtime.AbilitySystem;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

using static LMK.Editor.AbilitySystem.Tests.TestAttributes;
using static LMK.Runtime.AbilitySystem.ModifierOp;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// The effects core, and its edge cases.
    /// </summary>
    public sealed class EffectTests
    {
        private TestWorld w;

        [SetUp]
        public void SetUp() => w = new TestWorld();

        [TearDown]
        public void TearDown() => w.Dispose();



        #region Attribute edge cases
        [Test]
        public void ModifierOnMissingAttribute_IsSkipped()
        {
            var unit = w.MakeUnit((Health, 100f));                                         // no Armor
            unit.ApplyEffectToSelf(w.Effect(e => e.Instant().Mod(Armor, AddBase, 5).Mod(Health, AddBase, -10)));

            Assert.IsFalse(unit.HasAttribute(Armor));
            Assert.AreEqual(90f, unit.GetCurrent(Health), "the second modifier still applied");
        }

        [Test]
        public void UnresolvedAttribute_ResolvesToNone()
        {
            var ghost = GameplayAttribute.FromId(AttributeIds.MainRegistry, RegistryGuid.NewGuid(), "Vitals.Ghost");
            var ge = w.Effect(e => e.Instant().Mod(ghost, AddBase, 5).Mod(Health, AddBase, -10));
            var unit = w.MakeUnit((Health, 100f));

            unit.ApplyEffectToSelf(ge);

            Assert.AreEqual(AttributeStatus.Deleted, ghost.Resolve().Status);
            Assert.AreEqual(AttributeHandle.None, ghost.Handle);
            Assert.AreEqual(0f, unit.GetCurrent(ghost));
            Assert.AreEqual(90f, unit.GetCurrent(Health));
            Assert.IsTrue(EffectValidator.Validate(ge).HasError(EffectIssueCode.UnresolvedAttribute));
        }

        [Test]
        public void MaxBuffExpires_BaseIsReclamped()
        {
            var unit = w.MakeUnit((MaxHealth, 100f), (Health, 100f));
            unit.ApplyEffectToSelf(w.Effect(e => e.Duration(10).Mod(MaxHealth, AddBase, 50)));
            unit.ApplyEffectToSelf(w.Effect(e => e.Instant().Mod(Health, AddBase, 40)));
            Assert.AreEqual(140f, unit.GetBase(Health));

            w.Advance(10);                                                                  // the buff expires

            Assert.AreEqual(100f, unit.GetCurrent(MaxHealth));
            Assert.AreEqual(100f, unit.GetBase(Health));
            Assert.AreEqual(100f, unit.GetCurrent(Health));
            unit.ApplyEffectToSelf(w.Effect(e => e.Instant().Mod(Health, AddBase, -30)));
            Assert.AreEqual(70f, unit.GetCurrent(Health), "no hidden health absorbed the hit");
        }

        [Test]
        public void ZeroDivisorAndNonFinite_AreContained()
        {
            var unit = w.MakeUnit((AttackPower, 10f));

            w.Log.Expect(LogType.Warning, "zero divisor");
            unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Mod(AttackPower, DivideAdditive, 0)));     // 1 + (0 - 1) = 0
            Assert.AreEqual(10f, unit.GetCurrent(AttackPower));

            w.Log.Expect(LogType.Error, "non-finite");
            unit.ApplyEffectToSelf(w.Effect(e => e.Instant().Mod(AttackPower, AddBase, float.NaN)));
            Assert.AreEqual(10f, unit.GetBase(AttackPower));
            Assert.AreEqual(10f, unit.GetCurrent(AttackPower));
        }

        [Test]
        public void SameNameInTwoRegistries_AreTwoAttributes()
        {
            var unit = w.MakeUnit((Health, 100f), (OtherHealth, 100f));

            unit.ApplyEffectToSelf(w.Effect(e => e.Instant().Mod(Health, AddBase, -30)));
            unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Mod(OtherHealth, AddFinal, 5)));

            Assert.AreEqual(70f, unit.GetCurrent(Health));
            Assert.AreEqual(105f, unit.GetCurrent(OtherHealth));
            Assert.AreEqual(Health.LastKnownName, OtherHealth.LastKnownName);
        }

        [Test]
        public void SetRemovedAndAddedAgain_ModifiersRebind()
        {
            var armorSet = w.Set((Armor, 2f));
            var unit = w.MakeUnit(w.Set((Health, 100f)), armorSet);
            var handle = unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Mod(Armor, AddFinal, 5)));
            Assert.AreEqual(7f, unit.GetCurrent(Armor));

            unit.RemoveAttributeSet(armorSet);
            Assert.IsFalse(unit.HasAttribute(Armor));
            Assert.IsNotNull(unit.GetActiveEffect(handle), "the effect stays; its modifier waits");

            unit.AddAttributeSet(armorSet);
            Assert.AreEqual(7f, unit.GetCurrent(Armor), "the dormant modifier is rebound");

            unit.RemoveActiveEffect(handle);
            Assert.AreEqual(2f, unit.GetCurrent(Armor));
        }
        #endregion



        #region Effect edge cases
        [Test]
        public void RemoveAndApplyDuringTick_IsSafe()
        {
            var unit = w.MakeUnit((Health, 100f));
            var dot1 = unit.ApplyEffectToSelf(w.Effect(e => e.Duration(5).Period(1).Mod(Health, AddBase, -10)));
            var dot2 = unit.ApplyEffectToSelf(w.Effect(e => e.Duration(5).Period(1).Mod(Health, AddBase, -10)));
            var shield = w.Effect(e => e.Infinite().GrantTags(TestTags.Shielded));
            var shieldHandle = ActiveEffectHandle.Invalid;
            bool once = false;

            unit.OnAttributeChanged += _ =>                                                // runs inside dot1's execution, container locked
            {
                if (once) return;
                once = true;
                unit.RemoveActiveEffect(dot2);
                shieldHandle = unit.ApplyEffectToSelf(shield);
            };

            w.Advance(1);

            Assert.IsNotNull(unit.GetActiveEffect(dot1));
            Assert.IsNull(unit.GetActiveEffect(dot2), "dead at once");
            Assert.AreEqual(90f, unit.GetCurrent(Health), "dot2 never executed");
            Assert.IsNotNull(unit.GetActiveEffect(shieldHandle));
            Assert.IsTrue(unit.HasTag(TestTags.Shielded), "working at once");
        }

        [Test]
        public void MutuallyApplyingEffects_StopAtDepthLimit()
        {
            var a = w.Effect(e => e.Instant().Mod(Health, AddBase, -1));
            var b = w.Effect(e => e.Instant().Mod(Health, AddBase, -1));
            a.ComponentList.Add(AdditionalEffects.OnApplication(b));
            b.ComponentList.Add(AdditionalEffects.OnApplication(a));
            Assert.IsTrue(EffectValidator.Validate(a).HasError(EffectIssueCode.ApplicationCycle));

            var unit = w.MakeUnit((Health, 100f));
            w.Log.Expect(LogType.Error, "application depth");
            unit.ApplyEffectToSelf(a);                                                      // returns: no stack overflow

            Assert.AreEqual(100f - w.Settings.MaxApplicationDepth, unit.GetCurrent(Health));
            Assert.AreEqual(1, w.Log.ErrorCount);
        }

        [TestCase(false, 3)]
        [TestCase(true, 4)]
        public void PeriodOnExpiry_ExecutesExactly(bool _OnApplication, int _Expected)
        {
            var unit = w.MakeUnit((Health, 100f));
            unit.ApplyEffectToSelf(w.Effect(e => e.Duration(3).Period(1, _OnApplication).Mod(Health, AddBase, -1)));

            for (int i = 0; i < 210; i++)
                w.Advance(1.0 / 60);                                                        // 3.5 s in uneven float steps

            Assert.AreEqual(100f - _Expected, unit.GetCurrent(Health));
            Assert.AreEqual(0, unit.GetActiveEffects().Count);
        }

        [Test]
        public void Hitch_CatchesUpWithinDuration()
        {
            var unit = w.MakeUnit((Health, 100f));
            unit.ApplyEffectToSelf(w.Effect(e => e.Duration(3).Period(1).Mod(Health, AddBase, -1)));

            w.Advance(10);                                                                  // one call, far past expiry
            Assert.AreEqual(97f, unit.GetCurrent(Health), "3 executions, not 10");

            unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Period(1).Mod(Health, AddBase, -1)));
            w.Advance(3.5);
            Assert.AreEqual(94f, unit.GetCurrent(Health), "3 more");
        }

        [Test]
        public void ShutdownMidExecution_IsSafe()
        {
            var unit = w.MakeUnit((Health, 10f), (Mana, 50f));
            unit.OnAttributeChanged += c =>
            {
                if (c.Attribute == Health && c.NewValue <= 0) unit.Shutdown();
            };
            var hit = w.Effect(e => e.Instant().Mod(Health, AddBase, -50).Mod(Mana, AddBase, -10));

            Assert.DoesNotThrow(() => unit.ApplyEffectToSelf(hit));

            Assert.AreEqual(50f, unit.GetCurrent(Mana), "the second modifier was a no-op");
            Assert.AreEqual(ActiveEffectHandle.Invalid, unit.ApplyEffectToSelf(hit));
            Assert.IsFalse(unit.AddLooseTag(TestTags.Shielded));
            w.Advance(0.1);
            CollectionAssert.DoesNotContain(w.World.Systems, unit);
        }

        [Test]
        public void SelfRemoval_AndStaleHandle()
        {
            var unit = w.MakeUnit((Health, 100f));
            var h = unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Period(1).Mod(Health, AddBase, -10)));
            unit.OnAttributeChanged += _ => unit.RemoveActiveEffect(h);                    // from inside its own execution

            w.Advance(5);

            Assert.AreEqual(90f, unit.GetCurrent(Health), "exactly one execution");
            Assert.IsFalse(unit.RemoveActiveEffect(h));
            Assert.IsNull(unit.GetActiveEffect(h));
        }

        [Test]
        public void NonPositiveDuration_AndTinyPeriod()
        {
            var unit = w.MakeUnit((Health, 100f));
            var spec = unit.MakeOutgoingSpec(w.Effect(e => e.Duration(Magnitudes.SetByCaller(TestTags.DataDuration)).Mod(Health, AddBase, -5)));
            spec.SetByCaller(TestTags.DataDuration, 0);

            w.Log.Expect(LogType.Warning, "duration <= 0");
            Assert.AreEqual(ActiveEffectHandle.Invalid, unit.ApplySpecToSelf(spec));
            Assert.AreEqual(95f, unit.GetBase(Health), "executed as instant, on the base");

            var fast = w.Effect(e => e.Duration(1).Period(0.0001f).Mod(Health, AddBase, -1));
            Assert.IsTrue(EffectValidator.Validate(fast).HasError(EffectIssueCode.PeriodTooSmall));

            w.Log.Expect(LogType.Warning, "period clamped");
            unit.ApplyEffectToSelf(fast);
            w.Advance(1);

            Assert.AreEqual(95f - 1 / w.Settings.MinPeriod, unit.GetBase(Health), 1e-3, "50 executions, not 10,000");
        }
        #endregion



        #region Application and removal
        [Test]
        public void DurationEffect_AddsItsModifiers_UntilItEnds()
        {
            var unit = w.MakeUnit((AttackPower, 10f));
            var buff = w.Effect(e => e.Duration(5).Mod(AttackPower, AddBase, 5).GrantTags(TestTags.Empowered));

            var handle = unit.ApplyEffectToSelf(buff);
            var active = unit.GetActiveEffect(handle);

            Assert.AreEqual(15f, unit.GetCurrent(AttackPower));
            Assert.AreEqual(10f, unit.GetBase(AttackPower), "a duration effect never touches the base");
            Assert.IsTrue(unit.HasTag(TestTags.Empowered));
            Assert.IsTrue(unit.HasTag(TestTags.Status), "a tag counts for its parent too");
            Assert.AreEqual(5, active.TimeRemaining(), 1e-9);

            w.Advance(2);
            Assert.AreEqual(3, active.TimeRemaining(), 1e-9);

            w.Advance(3);
            Assert.IsNull(unit.GetActiveEffect(handle));
            Assert.IsTrue(active.IsRemoved);
            Assert.AreEqual(10f, unit.GetCurrent(AttackPower));
            Assert.IsFalse(unit.HasTag(TestTags.Empowered));
            Assert.IsFalse(w.Events.Removed.Single().Info.Premature, "it ran out");
        }

        [Test]
        public void Removal_ByHandle_IsPremature_AndHandlesAreUnique()
        {
            var unit = w.MakeUnit((AttackPower, 10f));
            var buff = w.Effect(e => e.Infinite().Mod(AttackPower, AddBase, 5));

            var first = unit.ApplyEffectToSelf(buff);
            var second = unit.ApplyEffectToSelf(buff);
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(20f, unit.GetCurrent(AttackPower), "no stacking yet: two active effects");

            Assert.IsTrue(unit.RemoveActiveEffect(first));
            Assert.AreEqual(15f, unit.GetCurrent(AttackPower));
            Assert.IsTrue(w.Events.Removed.Single().Info.Premature);
            Assert.IsFalse(unit.RemoveActiveEffect(ActiveEffectHandle.Invalid));
        }

        [Test]
        public void Applying_CopiesTheSpec_SoOneSpecServesSeveralTargets()
        {
            var source = w.MakeUnit((AttackPower, 0f));
            var first = w.MakeUnit((Health, 100f));
            var second = w.MakeUnit((Health, 100f));
            var hit = w.Effect(e => e.Instant().Mod(Health, AddBase, Magnitudes.SetByCaller(TestTags.DataDamage)));

            var spec = source.MakeOutgoingSpec(hit).SetByCaller(TestTags.DataDamage, -30);
            source.ApplySpecToTarget(spec, first);
            spec.SetByCaller(TestTags.DataDamage, -10);
            source.ApplySpecToTarget(spec, second);

            Assert.AreEqual(70f, first.GetCurrent(Health));
            Assert.AreEqual(90f, second.GetCurrent(Health));
            Assert.AreSame(source, spec.Context.Instigator);
        }

        [Test]
        public void Events_AppliedToSelfAndTarget_AndRemoved()
        {
            var source = w.MakeUnit((AttackPower, 0f));
            var target = w.MakeUnit((Health, 100f));
            int appliedToTarget = 0;
            source.OnEffectAppliedToTarget += (unit, _, _) =>
            {
                Assert.AreSame(target, unit);
                appliedToTarget++;
            };

            var handle = source.ApplySpecToTarget(source.MakeOutgoingSpec(w.Effect(e => e.Duration(1).Mod(Health, AddFinal, 1))), target);
            w.Advance(1);

            Assert.AreEqual(1, appliedToTarget);
            Assert.AreEqual(handle, w.Events.Applied.Single(e => e.Handle.IsValid).Handle);
            Assert.AreEqual(handle, w.Events.Removed.Single().Effect.Handle);
        }

        [Test]
        public void AdditionalEffects_RunOnApplication_AndOnTheRightCompletion()
        {
            var unit = w.MakeUnit((Health, 100f), (Armor, 0f));
            var normal = w.Effect(e => e.Instant().Mod(Armor, AddBase, 1));
            var premature = w.Effect(e => e.Instant().Mod(Armor, AddBase, 10));
            var bonus = w.Effect(e => e.Instant().Mod(Health, AddBase, -5));

            var effect = w.Effect(e => e.Duration(2).Add(AdditionalEffects.OnApplication(bonus)).Add(AdditionalEffects.OnComplete(normal, premature)));
            unit.ApplyEffectToSelf(effect);
            Assert.AreEqual(95f, unit.GetCurrent(Health));

            w.Advance(2);
            Assert.AreEqual(1f, unit.GetCurrent(Armor), "ran out: the normal completion");

            var handle = unit.ApplyEffectToSelf(effect);
            unit.RemoveActiveEffect(handle);
            Assert.AreEqual(11f, unit.GetCurrent(Armor), "removed: the premature completion");
        }

        [Test]
        public void ModifierQualification_FollowsTheTargetsTags()
        {
            var unit = w.MakeUnit((AttackPower, 10f));
            var requirement = new TagRequirements(new GameplayTagContainer(TestTags.Empowered));
            unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Mod(AttackPower, AddFinal, 5, _TargetTags: requirement)));
            Assert.AreEqual(10f, unit.GetCurrent(AttackPower), "the target lacks Empowered");

            unit.AddLooseTag(TestTags.Empowered);
            Assert.AreEqual(15f, unit.GetCurrent(AttackPower), "re-checked when the tags changed");

            unit.RemoveLooseTag(TestTags.Empowered);
            Assert.AreEqual(10f, unit.GetCurrent(AttackPower));
        }

        [Test]
        public void SourceRequirements_UseTheTagsCapturedWhenTheSpecWasMade()
        {
            var source = w.MakeUnit((AttackPower, 0f));
            var target = w.MakeUnit((Health, 100f));
            var requirement = new TagRequirements(new GameplayTagContainer(TestTags.Empowered));
            var hit = w.Effect(e => e.Instant().Mod(Health, AddBase, -10, _SourceTags: requirement));

            var plain = source.MakeOutgoingSpec(hit);
            source.AddLooseTag(TestTags.Empowered);
            var empowered = source.MakeOutgoingSpec(hit);
            source.RemoveLooseTag(TestTags.Empowered);

            source.ApplySpecToTarget(plain, target);
            Assert.AreEqual(100f, target.GetCurrent(Health));
            source.ApplySpecToTarget(empowered, target);
            Assert.AreEqual(90f, target.GetCurrent(Health));
        }

        [Test]
        public void SetByCaller_Missing_IsZeroWithAWarning()
        {
            var unit = w.MakeUnit((Health, 100f));

            w.Log.Expect(LogType.Warning, "no SetByCaller value");
            unit.ApplyEffectToSelf(w.Effect(e => e.Instant().Mod(Health, AddBase, Magnitudes.SetByCaller(TestTags.DataDamage))));

            Assert.AreEqual(100f, unit.GetCurrent(Health));
        }
        #endregion



        #region Tags and events
        [Test]
        public void TagCounts_AreReferenceCounted_AndEventsFireAfterTheUpdate()
        {
            var unit = w.MakeUnit((Health, 100f));
            int newOrRemoved = 0, anyChange = 0;
            unit.RegisterTagEvent(TestTags.Status, TagEventType.NewOrRemoved, (_, _) => newOrRemoved++);
            unit.RegisterTagEvent(TestTags.Status, TagEventType.AnyCountChange, (tag, count) =>
            {
                anyChange++;
                Assert.AreEqual(count, unit.GetTagCount(tag), "listeners see a consistent container");
            });

            var shield = unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().GrantTags(TestTags.Shielded)));
            unit.AddLooseTag(TestTags.Shielded);
            unit.AddLooseTag(TestTags.Silenced);

            Assert.AreEqual(3, unit.GetTagCount(TestTags.Status));
            Assert.AreEqual(2, unit.GetTagCount(TestTags.Shielded));
            Assert.IsTrue(unit.HasTagExact(TestTags.Shielded));
            Assert.IsFalse(unit.HasTagExact(TestTags.Status), "only an ancestor");
            Assert.AreEqual(1, newOrRemoved, "0 to 1 only");
            Assert.AreEqual(3, anyChange);

            unit.RemoveActiveEffect(shield);
            Assert.IsTrue(unit.HasTag(TestTags.Shielded), "the loose tag still holds it");
            unit.RemoveLooseTag(TestTags.Shielded);
            unit.RemoveLooseTag(TestTags.Silenced);
            Assert.IsFalse(unit.HasTag(TestTags.Status));
            Assert.AreEqual(2, newOrRemoved, "and 1 to 0");
        }

        [Test]
        public void TagCounts_NeverGoBelowZero_AndUnresolvedTagsNeverCount()
        {
            var unit = w.MakeUnit((Health, 100f));
            var ghost = GameplayTag.FromId(TestTags.Registry, RegistryGuid.NewGuid(), "Status.Ghost");

            unit.AddLooseTag(ghost);
            Assert.IsFalse(unit.HasTag(ghost));
            Assert.IsFalse(unit.HasTag(TestTags.Status), "an unresolved tag counts for no ancestor");

            w.Log.Expect(LogType.Error, "only added 0");
            unit.RemoveLooseTag(TestTags.Shielded);
            Assert.AreEqual(0, unit.GetTagCount(TestTags.Shielded));
        }

        [Test]
        public void GameplayEvents_ReachListenersOfTheTagOrAnAncestor()
        {
            var unit = w.MakeUnit((Health, 100f));
            int events = 0, deaths = 0;
            unit.RegisterGameplayEventListener(TestTags.Event, _ => events++);
            var registration = unit.RegisterGameplayEventListener(TestTags.Death, _ => deaths++);

            unit.HandleGameplayEvent(TestTags.Damaged, new GameplayEventData(TestTags.Damaged));
            unit.HandleGameplayEvent(TestTags.Death, new GameplayEventData(TestTags.Death));
            registration.Dispose();
            unit.HandleGameplayEvent(TestTags.Death, new GameplayEventData(TestTags.Death));

            Assert.AreEqual(3, events);
            Assert.AreEqual(1, deaths);
        }
        #endregion



        #region Validation
        [Test]
        public void Validator_FlagsInstantEffectsWithDurationFeatures()
        {
            var effect = w.Effect(e => e.Instant().Period(1).GrantTags(TestTags.Shielded).Mod(Health, AddBase, 1));

            var result = EffectValidator.Validate(effect);

            Assert.IsTrue(result.HasError(EffectIssueCode.InstantWithDurationFeatures));
            StringAssert.Contains("granted tags", result.ToString());
            StringAssert.Contains("a period", result.ToString());
        }

        [Test]
        public void Validator_FlagsSetByCallerWithoutTag_AndNonPositiveDurations()
        {
            var effect = w.Effect(e => e.Duration(0).Mod(Health, AddBase, Magnitudes.SetByCaller(GameplayTag.None)));

            var result = EffectValidator.Validate(effect);

            Assert.IsTrue(result.HasError(EffectIssueCode.SetByCallerWithoutTag));
            Assert.IsTrue(result.HasWarning(EffectIssueCode.NonPositiveDuration));
        }

        [Test]
        public void Validator_PassesACleanEffect()
        {
            var effect = w.Effect(e => e.Duration(5).Period(1).Mod(Health, AddBase, -1).GrantTags(TestTags.Shielded));

            CollectionAssert.IsEmpty(EffectValidator.Validate(effect).Issues);
        }
        #endregion
    }
}
