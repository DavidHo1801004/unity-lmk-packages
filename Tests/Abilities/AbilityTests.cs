using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using NUnit.Framework;

using UnityEngine;

using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Attributes;
using LMK.Runtime.GameplayTags;

using static LMK.Editor.AbilitySystem.Tests.TestAttributes;
using static LMK.Runtime.AbilitySystem.ModifierOp;

using Debug = UnityEngine.Debug;

namespace LMK.Editor.AbilitySystem.Tests
{
    [Serializable]
    public sealed class NestedConfig
    {
        public int value;
    }

    /// <summary>
    /// A logic with configuration, a nested inline-instanced object and per-instance state, cloned by the default
    /// cloner. In the test assembly's namespace so it serializes by reference.
    /// </summary>
    [Serializable]
    public sealed class CounterLogic : AbilityLogic
    {
        public int step;

        [SerializeReference, InlineInstanced]
        public NestedConfig nested;

        [NonSerialized]
        public int count;

        public override void Activate(AbilityContext _Context, GameplayEventData? _Trigger)
        {
            count += step;
            nested.value++;
            _Context.End();
        }
    }



    /// <summary>
    /// Abilities, and their edge cases.
    /// </summary>
    public sealed class AbilityTests
    {
        private TestWorld w;

        [SetUp]
        public void SetUp() => w = new TestWorld();

        [TearDown]
        public void TearDown() => w.Dispose();

        private static TestLogic StayActive => TestLogic.StayActive();

        private static TestLogic EndAtOnce => TestLogic.EndAtOnce();



        #region Edge cases
        [Test]
        public void End_IsIdempotent()
        {
            int ends = 0;
            var logic = new TestLogic
            {
                onActivate = ctx => { ctx.End(); ctx.End(); },
                onEnd = (ctx, _) => { ends++; ctx.End(); ctx.Cancel(); },
            };
            var unit = w.MakeUnit();
            var h = unit.GiveAbility(w.Ability(a => a.Logic(logic).OwnedTags(TestTags.Casting)));

            Assert.IsTrue(unit.TryActivateAbility(h));

            Assert.AreEqual(1, ends);
            Assert.AreEqual(1, w.Events.Ended.Count);
            Assert.AreEqual(0, unit.FindAbilitySpec(h).ActiveCount);
            Assert.AreEqual(0, unit.GetTagCount(TestTags.Casting));
        }

        [Test]
        public void StaleContinuation_NeverRuns()
        {
            int resumed = 0;
            var logic = new TestLogic
            {
                onActivate = async ctx =>
                {
                    await ctx.Delay(1);
                    resumed++;
                    ctx.End();
                },
            };
            var unit = w.MakeUnit();
            var h = unit.GiveAbility(w.Ability(a => a.Logic(logic).InstancedPerActor()));

            unit.TryActivateAbility(h);
            w.Advance(0.5);
            unit.CancelAbility(h);
            unit.TryActivateAbility(h);                                                     // same instance, new activation ID
            w.Advance(0.6);                                                                 // the first delay would have fired here

            Assert.AreEqual(0, resumed);
            Assert.AreEqual(1, unit.FindAbilitySpec(h).ActiveCount);

            w.Advance(0.5);
            Assert.AreEqual(1, resumed);
            Assert.AreEqual(0, unit.FindAbilitySpec(h).ActiveCount);
        }

        [Test]
        public void InlineLogic_IsClonedPerGrant()
        {
            var asset = w.Ability(a => a.Logic(new CounterLogic { step = 1, nested = new NestedConfig { value = 7 } }).InstancedPerActor());
            string before = JsonUtility.ToJson(asset);
            var u1 = w.MakeUnit();
            var u2 = w.MakeUnit();
            var h1 = u1.GiveAbility(asset);
            var h2 = u2.GiveAbility(asset);

            for (int i = 0; i < 3; i++)
                u1.TryActivateAbility(h1);

            var l1 = (CounterLogic)u1.FindAbilitySpec(h1).PerActorInstance.Logic;
            var l2 = (CounterLogic)u2.FindAbilitySpec(h2).PerActorInstance.Logic;
            var template = (CounterLogic)asset.logic;

            Assert.AreNotSame(l1, l2);
            Assert.AreNotSame(l1, template);
            Assert.AreNotSame(l1.nested, l2.nested);
            Assert.AreNotSame(l1.nested, template.nested);
            Assert.AreEqual(3, l1.count);
            Assert.AreEqual(10, l1.nested.value);
            Assert.AreEqual(0, l2.count);
            Assert.AreEqual(7, l2.nested.value);
            Assert.AreEqual(before, JsonUtility.ToJson(asset), "the template was never written");
            CollectionAssert.IsEmpty(w.World.FindWrittenAbilityAssets());
        }

        [Test]
        public void ClearAbility_WhileIterating_AndWhileActive()
        {
            var unit = w.MakeUnit();
            var hB = AbilitySpecHandle.Invalid;
            bool bRan = false;
            var hA = unit.GiveAbility(w.Ability(a => a.Trigger(AbilityTriggerSource.GameplayEvent, TestTags.Ping).Logic(_ => unit.ClearAbility(hB))));
            hB = unit.GiveAbility(w.Ability(a => a.Trigger(AbilityTriggerSource.GameplayEvent, TestTags.Ping).Logic(_ => bRan = true)));

            unit.HandleGameplayEvent(TestTags.Ping, new GameplayEventData(TestTags.Ping));  // A clears B while the spec list is iterated

            Assert.IsFalse(bRan);
            Assert.IsNull(unit.FindAbilitySpec(hB));

            unit.ClearAbility(hA);                                                          // A is active
            Assert.IsTrue(w.Events.Ended.Last().WasCancelled);
            Assert.IsNull(unit.FindAbilitySpec(hA));
        }

        [TestCase(GrantedAbilityRemovalPolicy.CancelAbilityImmediately, false, false)]
        [TestCase(GrantedAbilityRemovalPolicy.RemoveAbilityOnEnd, true, true)]
        [TestCase(GrantedAbilityRemovalPolicy.DoNothing, true, true)]
        public void GrantingEffectRemoved_FollowsPolicy(GrantedAbilityRemovalPolicy _Policy, bool _ActiveAfter, bool _GrantedAfter)
        {
            var unit = w.MakeUnit();
            var passive = w.Ability(a => a.Logic(StayActive));
            var h = unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().GrantAbility(passive, _Policy)));
            var spec = unit.FindAbility(passive);
            Assert.AreEqual(h, spec.GrantedByEffect);
            unit.TryActivateAbility(spec.Handle);

            unit.RemoveActiveEffect(h);

            Assert.AreEqual(_ActiveAfter, spec.ActiveCount > 0);
            Assert.AreEqual(_GrantedAfter, unit.FindAbility(passive) != null);

            if (_Policy == GrantedAbilityRemovalPolicy.RemoveAbilityOnEnd)
            {
                unit.CancelAbility(spec.Handle);
                Assert.IsNull(unit.FindAbility(passive));
            }
        }

        [Test]
        public void Cost_IsAllOrNothing()
        {
            var cost = w.Effect(e => e.Instant().Mod(Mana, AddBase, -30).Mod(Health, AddBase, -20));
            var cd = w.Effect(e => e.Duration(5).GrantTags(TestTags.CooldownTest));
            var unit = w.MakeUnit((Mana, 50f), (Health, 10f));
            var h = unit.GiveAbility(w.Ability(a => a.Cost(cost).Cooldown(cd).Logic(EndAtOnce)));

            Assert.IsFalse(unit.TryActivateAbility(h), "Mana is enough, Health is not");
            Assert.IsTrue(w.Events.Failed.Last().Failure.Has(AbilityFailureReason.CannotAffordCost));
            Assert.AreEqual(50f, unit.GetCurrent(Mana));
            Assert.AreEqual(10f, unit.GetCurrent(Health));

            unit.SetAttributeBase(Health, 30f);                                             // affordable at activation, not at commit
            bool committed = true;
            var h2 = unit.GiveAbility(w.Ability(a => a.Cost(cost).Cooldown(cd).Logic(ctx =>
            {
                unit.SetAttributeBase(Health, 10f);
                committed = ctx.Commit();
                ctx.End();
            })));

            Assert.IsTrue(unit.TryActivateAbility(h2));
            Assert.IsFalse(committed);
            Assert.AreEqual(50f, unit.GetCurrent(Mana), "nothing paid");
            Assert.IsFalse(unit.HasTag(TestTags.CooldownTest), "no cooldown");
        }

        [Test]
        public void BlockedTags_AreRefCounted()
        {
            var unit = w.MakeUnit();
            var h1 = unit.GiveAbility(w.Ability(a => a.Blocks(TestTags.AbilityAttack).Logic(StayActive)));
            var h2 = unit.GiveAbility(w.Ability(a => a.Blocks(TestTags.AbilityAttack).Logic(StayActive)));
            var hSlash = unit.GiveAbility(w.Ability(a => a.Tags(TestTags.AbilityAttackSlash).Logic(EndAtOnce)));

            unit.TryActivateAbility(h1);
            unit.TryActivateAbility(h2);
            unit.CancelAbility(h1);

            Assert.IsFalse(unit.TryActivateAbility(hSlash), "h2 still blocks; the parent tag covers the child");
            Assert.IsTrue(w.Events.Failed.Last().Failure.Has(AbilityFailureReason.Blocked));

            unit.CancelAbility(h2);
            Assert.IsTrue(unit.TryActivateAbility(hSlash));
        }

        [Test]
        public void CancelWithOwnTag_DoesNotCancelSelf()
        {
            var unit = w.MakeUnit();
            var hOther = unit.GiveAbility(w.Ability(a => a.Tags(TestTags.AbilityMelee).Logic(StayActive)));
            var h = unit.GiveAbility(w.Ability(a => a.Tags(TestTags.AbilityMelee).Cancels(TestTags.AbilityMelee).Logic(StayActive)));

            unit.TryActivateAbility(hOther);
            unit.TryActivateAbility(h);

            Assert.AreEqual(1, unit.FindAbilitySpec(h).ActiveCount);
            Assert.AreEqual(0, unit.FindAbilitySpec(hOther).ActiveCount);
        }

        [Test]
        public void TriggerLoop_StopsAtDepthLimit()
        {
            var unit = w.MakeUnit();
            int n = 0;
            w.Log.Expect(LogType.Error, "activation depth");
            var h = unit.GiveAbility(w.Ability(a => a.Trigger(AbilityTriggerSource.GameplayEvent, TestTags.Ping).InstancedPerExecution().Logic(ctx =>
            {
                n++;
                unit.HandleGameplayEvent(TestTags.Ping, new GameplayEventData(TestTags.Ping));
                ctx.End();
            })));

            unit.HandleGameplayEvent(TestTags.Ping, new GameplayEventData(TestTags.Ping));  // returns: no stack overflow

            Assert.AreEqual(w.Settings.MaxActivationDepth, n);
            Assert.AreEqual(0, unit.FindAbilitySpec(h).ActiveCount, "every nested activation ended");
        }

        [Test]
        public void TagCounts_AcrossSources()
        {
            var unit = w.MakeUnit();
            int added = 0, removed = 0;
            unit.RegisterTagEvent(TestTags.Stun, TagEventType.NewOrRemoved, (_, count) =>
            {
                if (count > 0) added++;
                else removed++;
            });

            var hE = unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().GrantTags(TestTags.StunFrozen)));     // a child tag counts for its parent
            unit.AddLooseTag(TestTags.Stun);
            var hA = unit.GiveAbility(w.Ability(a => a.OwnedTags(TestTags.Stun).Logic(StayActive)));
            unit.TryActivateAbility(hA);

            Assert.AreEqual(3, unit.GetTagCount(TestTags.Stun));
            Assert.AreEqual(1, added);
            Assert.AreEqual(0, removed);

            unit.RemoveActiveEffect(hE);
            unit.RemoveLooseTag(TestTags.Stun);
            Assert.IsTrue(unit.HasTag(TestTags.Stun));
            Assert.AreEqual(0, removed);

            unit.CancelAbility(hA);
            Assert.IsFalse(unit.HasTag(TestTags.Stun));
            Assert.AreEqual(1, removed);

            w.Log.Expect(LogType.Error, "tag count below zero");
            unit.RemoveLooseTag(TestTags.Stun);
            Assert.AreEqual(0, unit.GetTagCount(TestTags.Stun));
        }

        [Test]
        public void AvatarReplaced_CancelsActiveAbilities_AndKeepsTheRest()
        {
            var unit = w.MakeUnit((AttackPower, 10f));
            var running = unit.GiveAbility(w.Ability(a => a.Logic(StayActive).OwnedTags(TestTags.Casting)));
            var idle = unit.GiveAbility(w.Ability(a => a.Logic(EndAtOnce)));
            var buff = unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Mod(AttackPower, AddBase, 5)));
            unit.TryActivateAbility(running);

            unit.InitActorInfo(unit.ActorInfo.Owner, new object());

            Assert.AreEqual(0, unit.FindAbilitySpec(running).ActiveCount);
            Assert.IsTrue(w.Events.Ended.Last().WasCancelled);
            Assert.IsFalse(unit.HasTag(TestTags.Casting));
            Assert.IsNotNull(unit.FindAbilitySpec(idle), "grants persist");
            Assert.IsNotNull(unit.GetActiveEffect(buff), "effects persist");
            Assert.AreEqual(15f, unit.GetCurrent(AttackPower), "attributes persist");
        }

        [Test]
        public void InhibitedEffect_TakesItsGrantedAbility_AndGivesItBack()
        {
            var thorns = w.Ability(a => a.Logic(StayActive));
            var buff = w.Effect(e => e.Duration(10).Mod(AttackPower, AddBase, 5).GrantTags(TestTags.Empowered).GrantAbility(thorns).Ongoing(TestTags.Silenced));
            var unit = w.MakeUnit((AttackPower, 10f));
            unit.ApplyEffectToSelf(buff);
            Assert.IsNotNull(unit.FindAbility(thorns));

            unit.AddLooseTag(TestTags.Silenced);
            Assert.IsNull(unit.FindAbility(thorns));

            w.Advance(4);
            unit.RemoveLooseTag(TestTags.Silenced);
            Assert.IsNotNull(unit.FindAbility(thorns));
            Assert.AreEqual(15f, unit.GetCurrent(AttackPower));
        }

        [Test]
        public void AfterShutdown_AbilitiesCanNeitherBeGivenNorActivated()
        {
            var unit = w.MakeUnit((Health, 10f));
            var ability = w.Ability(a => a.Logic(StayActive));
            var h = unit.GiveAbility(ability);
            unit.TryActivateAbility(h);

            unit.Shutdown();

            Assert.AreEqual(AbilitySpecHandle.Invalid, unit.GiveAbility(ability));
            Assert.IsFalse(unit.TryActivateAbility(h));
        }
        #endregion



        #region Activation, cost and cooldown
        [Test]
        public void Commit_PaysTheCost_AndStartsTheCooldown()
        {
            var cost = w.Effect(e => e.Instant().Mod(Mana, AddBase, -30));
            var cd = w.Effect(e => e.Duration(5).GrantTags(TestTags.CooldownShared));
            var unit = w.MakeUnit((Mana, 50f));
            var fireball = w.Ability(a => a.Cost(cost).Cooldown(cd).Logic(ctx => { ctx.Commit(); ctx.End(); }));
            var frostbolt = w.Ability(a => a.Cooldown(cd).Logic(EndAtOnce));
            var h = unit.GiveAbility(fireball);
            var hFrost = unit.GiveAbility(frostbolt);

            Assert.IsTrue(unit.TryActivateAbility(h));
            Assert.AreEqual(20f, unit.GetCurrent(Mana));
            Assert.AreEqual(5, unit.GetCooldownRemaining(fireball), 1e-6);

            Assert.IsFalse(unit.TryActivateAbility(h));
            Assert.IsTrue(w.Events.Failed.Last().Failure.Has(AbilityFailureReason.OnCooldown));
            Assert.IsFalse(unit.TryActivateAbility(hFrost), "a shared cooldown tag shares the cooldown");

            w.Advance(2);
            Assert.AreEqual(3, unit.GetCooldownRemaining(frostbolt), 1e-6);
            w.Advance(3);
            Assert.AreEqual(0, unit.GetCooldownRemaining(fireball));
            Assert.IsFalse(unit.TryActivateAbility(h), "20 Mana can't pay 30");
            Assert.IsTrue(w.Events.Failed.Last().Failure.Has(AbilityFailureReason.CannotAffordCost));
        }

        [Test]
        public void RequiredAndBlockedTags_NoAvatar_AndTheLogicsOwnCheck()
        {
            var unit = w.MakeUnit();
            var needsEmpowered = unit.GiveAbility(w.Ability(a => a.Requires(TestTags.Empowered)));
            var silenceable = unit.GiveAbility(w.Ability(a => a.BlockedBy(TestTags.Silenced)));
            var picky = unit.GiveAbility(w.Ability(a => a.Logic(new TestLogic { canActivate = (_, tags) => { tags.AddTag(TestTags.Buff); return false; } })));

            Assert.IsFalse(unit.TryActivateAbility(needsEmpowered));
            Assert.IsTrue(w.Events.Failed.Last().Failure.Has(AbilityFailureReason.TagsMissing));

            unit.AddLooseTag(TestTags.Silenced);
            Assert.IsFalse(unit.TryActivateAbility(silenceable));
            Assert.IsTrue(w.Events.Failed.Last().Failure.Has(AbilityFailureReason.TagsBlocked));

            Assert.IsFalse(unit.TryActivateAbility(picky));
            Assert.IsTrue(w.Events.Failed.Last().Failure.Has(AbilityFailureReason.Logic));
            Assert.IsTrue(w.Events.Failed.Last().Failure.Tags.HasTagExact(TestTags.Buff));

            var bodiless = w.World.CreateSystem();
            var h = bodiless.GiveAbility(w.Ability(a => a.Logic(EndAtOnce)));
            Assert.IsFalse(bodiless.TryActivateAbility(h));
            Assert.IsTrue(bodiless.CanActivateAbility(h, out var failure) == false && failure.Has(AbilityFailureReason.NoAvatar));
        }

        [Test]
        public void Retrigger_EndsTheRunningActivation_AndPerExecutionRunsSeveral()
        {
            var unit = w.MakeUnit();
            var once = unit.GiveAbility(w.Ability(a => a.Logic(StayActive)));
            var again = unit.GiveAbility(w.Ability(a => a.Logic(StayActive).Retrigger()));
            var many = unit.GiveAbility(w.Ability(a => a.Logic(StayActive).InstancedPerExecution()));

            Assert.IsTrue(unit.TryActivateAbility(once));
            Assert.IsFalse(unit.TryActivateAbility(once), "one activation at a time");

            unit.TryActivateAbility(again);
            Assert.IsTrue(unit.TryActivateAbility(again));
            Assert.AreEqual(1, unit.FindAbilitySpec(again).ActiveCount);
            Assert.IsTrue(w.Events.Ended.Last().WasCancelled, "the running activation was ended");

            unit.TryActivateAbility(many);
            unit.TryActivateAbility(many);
            Assert.AreEqual(2, unit.FindAbilitySpec(many).ActiveCount);
            Assert.AreEqual(2, unit.FindAbilitySpec(many).ExecutionInstances.Count);

            unit.CancelAbility(many);
            Assert.AreEqual(0, unit.FindAbilitySpec(many).ExecutionInstances.Count, "per-execution instances are released");
        }

        [Test]
        public void GiveAndActivateOnce_AndRemoveOnEnd()
        {
            var unit = w.MakeUnit();
            var oneShot = w.Ability(a => a.Logic(EndAtOnce));
            var lasting = w.Ability(a => a.Logic(StayActive));

            unit.GiveAbilityAndActivateOnce(oneShot);
            Assert.IsNull(unit.FindAbility(oneShot), "removed when its activation ended");

            var h = unit.GiveAbility(lasting);
            unit.TryActivateAbility(h);
            unit.SetRemoveAbilityOnEnd(h);
            Assert.IsNotNull(unit.FindAbilitySpec(h));
            unit.CancelAbility(h);
            Assert.IsNull(unit.FindAbilitySpec(h));
        }

        [Test]
        public void SourceAndTargetTags_AreCheckedOnTheTriggeringEvent()
        {
            var unit = w.MakeUnit();
            var source = w.MakeUnit();
            var ability = w.Ability(a => a.Trigger(AbilityTriggerSource.GameplayEvent, TestTags.Ping).Logic(EndAtOnce));
            ability.sourceRequiredTags.AddTag(TestTags.Empowered);
            unit.GiveAbility(ability);

            var ping = new GameplayEventData(TestTags.Ping, source, unit);
            Assert.AreEqual(0, unit.HandleGameplayEvent(TestTags.Ping, ping));
            Assert.IsTrue(w.Events.Failed.Last().Failure.Has(AbilityFailureReason.SourceOrTargetTags));

            source.AddLooseTag(TestTags.Empowered);
            Assert.AreEqual(1, unit.HandleGameplayEvent(TestTags.Ping, ping));
        }
        #endregion



        #region Triggers, input and tasks
        [Test]
        public void OwnedTagTriggers_AddedActivates_PresentActivatesAndCancels()
        {
            var unit = w.MakeUnit();
            var added = unit.GiveAbility(w.Ability(a => a.Trigger(AbilityTriggerSource.OwnedTagAdded, TestTags.Stun).Logic(StayActive)));
            var present = unit.GiveAbility(w.Ability(a => a.Trigger(AbilityTriggerSource.OwnedTagPresent, TestTags.Stun).Logic(StayActive)));

            unit.AddLooseTag(TestTags.Stun);
            Assert.AreEqual(1, unit.FindAbilitySpec(added).ActiveCount);
            Assert.AreEqual(1, unit.FindAbilitySpec(present).ActiveCount);

            unit.RemoveLooseTag(TestTags.Stun);
            Assert.AreEqual(1, unit.FindAbilitySpec(added).ActiveCount, "OwnedTagAdded doesn't cancel");
            Assert.AreEqual(0, unit.FindAbilitySpec(present).ActiveCount);

            unit.AddLooseTag(TestTags.Silenced);
            var late = unit.GiveAbility(w.Ability(a => a.Trigger(AbilityTriggerSource.OwnedTagPresent, TestTags.Silenced).Logic(StayActive)));
            Assert.AreEqual(1, unit.FindAbilitySpec(late).ActiveCount, "a tag already present activates it on grant");
        }

        [Test]
        public void Input_ActivatesAnInactiveAbility_AndReachesAnActiveOne()
        {
            var unit = w.MakeUnit();
            int pressedWhileActive = 0, released = 0;
            var logic = new TestLogic
            {
                onActivate = async ctx =>
                {
                    await ctx.WaitInputRelease();
                    released++;
                    ctx.End();
                },
                onInputPressed = _ => pressedWhileActive++,
            };
            var h = unit.GiveAbility(w.Ability(a => a.Logic(logic)), 1, 7);

            unit.AbilityInputPressed(7);
            Assert.AreEqual(1, unit.FindAbilitySpec(h).ActiveCount);
            Assert.IsTrue(unit.FindAbilitySpec(h).InputPressed);

            unit.AbilityInputPressed(7);
            Assert.AreEqual(1, pressedWhileActive);

            unit.AbilityInputReleased(7);
            Assert.AreEqual(1, released);
            Assert.AreEqual(0, unit.FindAbilitySpec(h).ActiveCount);
        }

        [Test]
        public void Tasks_EventTagAttributeAndEffect()
        {
            var unit = w.MakeUnit((Health, 100f));
            var steps = new List<string>();
            var dot = unit.ApplyEffectToSelf(w.Effect(e => e.Duration(3)));
            var logic = new TestLogic
            {
                onActivate = async ctx =>
                {
                    var data = await ctx.WaitEvent(TestTags.Event);
                    steps.Add($"event {data.Magnitude}");
                    await ctx.WaitTagAdded(TestTags.Shielded);
                    steps.Add("tag");
                    float health = await ctx.WaitAttributeChange(Health, ThresholdComparison.LessOrEqual, 50f);
                    steps.Add($"health {health}");
                    await ctx.WaitEffectRemoved(dot);
                    steps.Add("effect");
                    ctx.End();
                },
            };
            var h = unit.GiveAbility(w.Ability(a => a.Logic(logic)));
            unit.TryActivateAbility(h);

            unit.HandleGameplayEvent(TestTags.Damaged, new GameplayEventData(TestTags.Damaged, null, unit, 4f));
            unit.AddLooseTag(TestTags.Shielded);
            unit.SetAttributeBase(Health, 80f);
            unit.SetAttributeBase(Health, 40f);
            w.Advance(3);

            CollectionAssert.AreEqual(new[] { "event 4", "tag", "health 40", "effect" }, steps);
            Assert.AreEqual(0, unit.FindAbilitySpec(h).ActiveCount);
        }

        /// <summary>
        /// Targets every other unit of the world at once, or cancels. Configuration only: it is cloned with the logic,
        /// so it can't hold a unit.
        /// </summary>
        [Serializable]
        private sealed class InstantTargeting : TargetingStrategy
        {
            public bool cancel;

            public override void Begin(AbilityContext _Context, Action<TargetData> _Confirm, Action _Cancel)
            {
                if (cancel)
                {
                    _Cancel();
                    return;
                }

                var data = new TargetData();
                data.Targets.AddRange(_Context.Owner.World.Systems.Where(e => e != _Context.Owner));
                _Confirm(data);
            }
        }

        [Test]
        public void SequenceLogic_CommitsWaitsAppliesAndSendsEvents()
        {
            var caster = w.MakeUnit((Mana, 50f));
            var enemy = w.MakeUnit((Health, 100f));
            var cost = w.Effect(e => e.Instant().Mod(Mana, AddBase, -10));
            var hit = w.Effect(e => e.Instant().Mod(Health, AddBase, -25));
            var pings = 0;
            caster.RegisterGameplayEventListener(TestTags.Ping, _ => pings++);

            var sequence = new SequenceLogic
            {
                actions =
                {
                    new CommitAction(),
                    new WaitAction { seconds = 1f },
                    new ApplyEffectToTargetsAction { targeting = new InstantTargeting(), effect = hit },
                    new SendEventAction { eventTag = TestTags.Ping },
                },
            };
            var h = caster.GiveAbility(w.Ability(a => a.Cost(cost).Logic(sequence)));

            caster.TryActivateAbility(h);
            Assert.AreEqual(40f, caster.GetCurrent(Mana));
            Assert.AreEqual(100f, enemy.GetCurrent(Health), "still waiting");

            w.Advance(1);
            Assert.AreEqual(75f, enemy.GetCurrent(Health));
            Assert.AreEqual(1, pings);
            Assert.AreEqual(0, caster.FindAbilitySpec(h).ActiveCount, "it ends after the last step");
            Assert.IsFalse(w.Events.Ended.Last().WasCancelled);

            var aimless = new SequenceLogic { actions = { new ApplyEffectToTargetsAction { targeting = new InstantTargeting { cancel = true }, effect = hit } } };
            caster.TryActivateAbility(caster.GiveAbility(w.Ability(a => a.Logic(aimless))));
            Assert.IsTrue(w.Events.Ended.Last().WasCancelled, "cancelled targeting cancels the sequence");
        }
        #endregion



        #region Assets
        [Test]
        public void WritingToAnAbilityAsset_IsReportedWhenTheWorldEnds()
        {
            var asset = w.Ability(a => a.Logic(new CounterLogic { step = 1, nested = new NestedConfig() }));
            var unit = w.MakeUnit();
            unit.GiveAbility(asset);

            ((CounterLogic)asset.logic).step = 5;

            CollectionAssert.AreEqual(new[] { asset }, w.World.FindWrittenAbilityAssets());
            w.Log.Expect(LogType.Error, "was written while the world ran");
            w.World.Dispose();
        }

        [Test]
        public void Validator_FlagsMissingLogic_BadCostAndCooldown_AndSelfTriggers()
        {
            var bad = w.Ability(a => a.Cost(w.Effect(e => e.Duration(1))).Cooldown(w.Effect(e => e.Duration(5)))
                                      .Trigger(AbilityTriggerSource.OwnedTagAdded, TestTags.Casting).OwnedTags(TestTags.Casting));
            bad.logic = null;

            var result = AbilityValidator.Validate(bad);

            Assert.IsTrue(result.HasError(AbilityIssueCode.NoLogic));
            Assert.IsTrue(result.HasError(AbilityIssueCode.CostNotInstant));
            Assert.IsTrue(result.HasError(AbilityIssueCode.InvalidCooldown), "a cooldown that grants no tag");
            Assert.IsTrue(result.HasWarning(AbilityIssueCode.TriggerOnOwnTag));

            var good = w.Ability(a => a.Cost(w.Effect(e => e.Instant().Mod(Mana, AddBase, -1))).Cooldown(w.Effect(e => e.Duration(1).GrantTags(TestTags.CooldownTest))));
            CollectionAssert.IsEmpty(AbilityValidator.Validate(good).Issues);
        }

        [Test]
        public void FiftyAbilitiesOnOneUnit_CloneQuickly()
        {
            var unit = w.MakeUnit();
            var assets = Enumerable.Range(0, 50)
                .Select(i => w.Ability(a => a.Logic(new CounterLogic { step = i, nested = new NestedConfig { value = i } })))
                .ToList();

            var clock = Stopwatch.StartNew();
            foreach (var asset in assets)
                unit.GiveAbility(asset);
            clock.Stop();

            Debug.Log($"Granting 50 abilities with Object.Instantiate cloning took {clock.Elapsed.TotalMilliseconds:F2} ms.");
            TestContext.WriteLine($"Granting 50 abilities took {clock.Elapsed.TotalMilliseconds:F2} ms.");
            Assert.Less(clock.Elapsed.TotalMilliseconds, 250, "Object.Instantiate is fast enough for 50 grants");
            Assert.AreEqual(50, unit.GetActivatableAbilities().Count);
        }
        #endregion
    }
}
