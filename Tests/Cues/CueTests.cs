using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using UnityEngine;

using LMK.Runtime.AbilitySystem;
using LMK.Runtime.GameplayTags;

using static LMK.Editor.AbilitySystem.Tests.TestAttributes;
using static LMK.Runtime.AbilitySystem.ModifierOp;

using Unit = LMK.Runtime.AbilitySystem.GameplayAbilitySystem;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// Gameplay cues: events raised by effects and abilities, queued and delivered at the end of a step,
    /// and routed to notifies by the cue manager.
    /// </summary>
    public sealed class CueTests
    {
        private TestWorld w;
        private RecordingHandler cues;

        [SetUp]
        public void SetUp()
        {
            w = new TestWorld();
            cues = new RecordingHandler();
            w.World.Cues = new GameplayCueQueue { Handler = cues };
        }

        [TearDown]
        public void TearDown() => w.Dispose();

        private void Step() => w.World.Step(0.01);



        #region Effects
        [Test]
        public void InstantEffect_RaisesExecuted_AtTheEndOfTheStep()
        {
            var unit = w.MakeUnit((Health, 100f));
            unit.ApplyEffectToSelf(w.Effect(e => e.Instant().Mod(Health, AddBase, -30).Cue(TestTags.CueHit, Health)));

            Assert.AreEqual(0, cues.Events.Count, "queued until the step ends");

            Step();
            Assert.AreEqual(1, cues.Events.Count);
            var (target, tag, type, parameters) = cues.Events[0];
            Assert.AreSame(unit, target);
            Assert.AreEqual(TestTags.CueHit, tag);
            Assert.AreEqual(GameplayCueEvent.Executed, type);
            Assert.AreEqual(-30f, parameters.RawMagnitude, "the executed magnitude of the cue's attribute");
            Assert.AreSame(unit, parameters.Instigator);
        }

        [Test]
        public void DurationEffect_RaisesOnActiveAndWhileActive_ThenRemovedOnExpiry()
        {
            var unit = w.MakeUnit((Armor, 10f));
            unit.ApplyEffectToSelf(w.Effect(e => e.Duration(1).Mod(Armor, AddBase, 5).Cue(TestTags.CueBurn, Armor)));
            Step();

            CollectionAssert.AreEqual(new[] { GameplayCueEvent.OnActive, GameplayCueEvent.WhileActive }, cues.Types);
            Assert.AreEqual(5f, cues.Events[0].Parameters.RawMagnitude);

            cues.Events.Clear();
            w.Advance(1);
            CollectionAssert.AreEqual(new[] { GameplayCueEvent.Removed }, cues.Types);
        }

        [Test]
        public void AddedAndRemovedInOneStep_CancelOut()
        {
            var unit = w.MakeUnit();
            var effect = w.Effect(e => e.Infinite().Cue(TestTags.CueBurn));
            var handle = unit.ApplyEffectToSelf(effect);
            unit.RemoveActiveEffect(handle);

            unit.ExecuteCue(TestTags.CueHit);
            Step();

            CollectionAssert.AreEqual(new[] { GameplayCueEvent.Executed }, cues.Types, "only the one-shot cue is shown");
        }

        [Test]
        public void PeriodicEffect_RaisesExecutedEachPeriod()
        {
            var unit = w.MakeUnit((Health, 100f));
            unit.ApplyEffectToSelf(w.Effect(e => e.Duration(3).Period(1).Mod(Health, AddBase, -10).Cue(TestTags.CueBurn)));
            w.Advance(3);

            Assert.AreEqual(1, cues.Count(GameplayCueEvent.OnActive));
            Assert.AreEqual(3, cues.Count(GameplayCueEvent.Executed));
            Assert.AreEqual(1, cues.Count(GameplayCueEvent.Removed));
        }

        [Test]
        public void Inhibition_RemovesTheCue_AndRelease_AddsItBack()
        {
            var unit = w.MakeUnit();
            unit.ApplyEffectToSelf(w.Effect(e => e.Infinite().Ongoing(TestTags.Silenced).Cue(TestTags.CueBurn)));
            Step();
            cues.Events.Clear();

            unit.AddLooseTag(TestTags.Silenced);
            Step();
            CollectionAssert.AreEqual(new[] { GameplayCueEvent.Removed }, cues.Types);

            cues.Events.Clear();
            unit.RemoveLooseTag(TestTags.Silenced);
            Step();
            CollectionAssert.AreEqual(new[] { GameplayCueEvent.OnActive, GameplayCueEvent.WhileActive }, cues.Types);
        }

        [Test]
        public void Execution_CanHandleCuesManually()
        {
            var unit = w.MakeUnit((Health, 100f));
            unit.ApplyEffectToSelf(w.Effect(e => e.Instant().Execution(new ManualCues()).Cue(TestTags.CueHit)));
            Step();

            Assert.AreEqual(1, cues.Events.Count);
            Assert.AreEqual(TestTags.CueHitFire, cues.Events[0].Tag, "only the cue the execution raised");
            Assert.AreEqual(90f, unit.GetCurrent(Health));
        }

        [Test]
        public void ExecutionOutput_IsTheCueMagnitude()
        {
            var unit = w.MakeUnit((Health, 100f), (Armor, 7f));
            unit.ApplyEffectToSelf(w.Effect(e => e.Instant().Execution(new ArmorDamage()).Cue(TestTags.CueHit, Health)));
            Step();

            Assert.AreEqual(-7f, cues.Events.Single().Parameters.RawMagnitude);
        }

        [Test]
        public void NormalizedMagnitude_IsTheLevelInTheCueRange()
        {
            var unit = w.MakeUnit();
            var effect = w.Effect(e => e.Instant().Cue(TestTags.CueHit, default, 1, 5));
            unit.ApplyEffectToSelf(effect, 3);
            unit.ApplyEffectToSelf(effect, 9);
            unit.ApplyEffectToSelf(w.Effect(e => e.Instant().Cue(TestTags.CueBurn)));
            Step();

            Assert.AreEqual(0.5f, cues.Events[0].Parameters.NormalizedMagnitude);
            Assert.AreEqual(1f, cues.Events[1].Parameters.NormalizedMagnitude, "clamped");
            Assert.AreEqual(1f, cues.Events[2].Parameters.NormalizedMagnitude, "an empty range is 1, as in UE");
            Assert.AreEqual(3f, cues.Events[0].Parameters.Level);
        }

        [Test]
        public void StackApplication_RaisesOnActiveAgain_UnlessSuppressed()
        {
            var unit = w.MakeUnit();
            var burn = w.Effect(e => e.Duration(5).Stack(StackingType.AggregateByTarget, 3).Cue(TestTags.CueBurn));
            unit.ApplyEffectToSelf(burn);
            unit.ApplyEffectToSelf(burn);
            Step();
            Assert.AreEqual(2, cues.Count(GameplayCueEvent.OnActive), "as in UE: each application to the stack");

            cues.Events.Clear();
            burn.SuppressStackingCues = true;
            unit.ApplyEffectToSelf(burn);
            Step();
            Assert.AreEqual(0, cues.Count(GameplayCueEvent.OnActive), "suppressed");
        }
        #endregion



        #region Units and abilities
        [Test]
        public void LooseCues_AreCounted()
        {
            var unit = w.MakeUnit();
            unit.AddCue(TestTags.CueBurn);
            unit.AddCue(TestTags.CueBurn);
            Step();
            Assert.AreEqual(1, cues.Count(GameplayCueEvent.OnActive));

            unit.RemoveCue(TestTags.CueBurn);
            Step();
            Assert.IsTrue(unit.HasCue(TestTags.CueBurn));
            Assert.AreEqual(0, cues.Count(GameplayCueEvent.Removed));

            unit.RemoveCue(TestTags.CueBurn);
            Step();
            Assert.IsFalse(unit.HasCue(TestTags.CueBurn));
            Assert.AreEqual(1, cues.Count(GameplayCueEvent.Removed));
        }

        [Test]
        public void AbilityCues_AreRemovedWhenTheActivationEnds()
        {
            var unit = w.MakeUnit();
            var target = w.MakeUnit();
            var h = unit.GiveAbility(w.Ability(a => a.Logic(ctx =>
            {
                ctx.AddCue(TestTags.CueBurn);
                ctx.ExecuteCue(TestTags.CueHit, null, target);
            })));

            unit.TryActivateAbility(h);
            Step();
            Assert.IsTrue(unit.HasCue(TestTags.CueBurn));
            Assert.AreSame(target, cues.Events.Single(e => e.Tag.Equals(TestTags.CueHit)).Target);

            unit.CancelAbility(h);
            Step();
            Assert.IsFalse(unit.HasCue(TestTags.CueBurn));
            Assert.AreEqual(1, cues.Count(GameplayCueEvent.Removed));
        }

        [Test]
        public void Shutdown_DropsQueuedEvents_AndClearsTheTarget()
        {
            var unit = w.MakeUnit();
            unit.AddCue(TestTags.CueBurn);
            unit.Shutdown();
            Step();

            Assert.AreEqual(0, cues.Events.Count);
            CollectionAssert.AreEqual(new[] { unit }, cues.Cleared);
        }

        [Test]
        public void ThrowingHandler_IsLogged_AndTheRestAreDelivered()
        {
            var unit = w.MakeUnit();
            cues.ThrowOn = TestTags.CueHit;
            unit.ExecuteCue(TestTags.CueHit);
            unit.ExecuteCue(TestTags.CueBurn);

            w.Log.Expect(LogType.Exception, "cue failed");
            Step();
            Assert.AreEqual(TestTags.CueBurn, cues.Events.Single().Tag);
        }
        #endregion



        #region Cue manager
        [Test]
        public void Manager_RoutesToTheMostSpecificTag_ThenStopsAtAnOverride()
        {
            var hit = Notify(TestTags.CueHit, true);
            var root = Notify(TestTags.Cue, true);
            var manager = Manager(new[] { hit, root });
            var unit = w.MakeUnit();

            manager.HandleCue(unit, TestTags.CueHitFire, GameplayCueEvent.Executed, new GameplayCueParameters());
            Assert.AreEqual(1, hit.Calls.Count, "Hit handles Hit.Fire, which has no notify of its own");
            Assert.AreEqual(TestTags.CueHit, hit.Calls[0].MatchedTag);
            Assert.AreEqual(TestTags.CueHitFire, hit.Calls[0].OriginalTag);
            Assert.AreEqual(0, root.Calls.Count, "Hit is an override");

            hit.isOverride = false;
            manager = Manager(new[] { hit, root });
            manager.HandleCue(unit, TestTags.CueHitFire, GameplayCueEvent.Executed, new GameplayCueParameters());
            Assert.AreEqual(1, root.Calls.Count, "routing goes on to the parent");
        }

        [Test]
        public void Manager_PoolsActorNotifies_OnePerTarget()
        {
            var prefabObject = new GameObject("Burn notify");
            try
            {
                var prefab = prefabObject.AddComponent<GameplayCueNotifyActor>();
                prefab.cueTag = TestTags.CueBurn;
                var set = ScriptableObject.CreateInstance<GameplayCueSet>();
                set.actorNotifies.Add(prefab);
                var manager = new GameplayCueManager(new[] { set });
                w.World.Cues = new GameplayCueQueue { Handler = manager };

                var a = w.MakeUnit();
                var b = w.MakeUnit();
                a.AddCue(TestTags.CueBurn);
                b.AddCue(TestTags.CueBurn);
                Step();

                var instanceA = manager.FindActive(a, prefab);
                Assert.IsNotNull(instanceA);
                Assert.AreNotSame(instanceA, manager.FindActive(b, prefab));
                Assert.AreSame(a, instanceA.Target);
                Assert.IsTrue(instanceA.gameObject.activeSelf);

                a.RemoveCue(TestTags.CueBurn);
                Step();
                Assert.IsNull(manager.FindActive(a, prefab));
                Assert.IsFalse(instanceA.gameObject.activeSelf, "back in the pool");

                a.AddCue(TestTags.CueBurn);
                Step();
                Assert.AreSame(instanceA, manager.FindActive(a, prefab), "reused from the pool");

                b.Shutdown();
                Assert.IsNull(manager.FindActive(b, prefab), "a unit that shuts down releases its instances");

                manager.Dispose();
                Object.DestroyImmediate(set);
            }
            finally
            {
                Object.DestroyImmediate(prefabObject);
            }
        }

        private readonly List<Object> notifies = new();

        private RecordingNotify Notify(GameplayTag _Tag, bool _Override)
        {
            var notify = ScriptableObject.CreateInstance<RecordingNotify>();
            notify.cueTag = _Tag;
            notify.isOverride = _Override;
            notifies.Add(notify);
            return notify;
        }

        private GameplayCueManager Manager(IEnumerable<GameplayCueNotifyStatic> _Notifies)
        {
            var set = ScriptableObject.CreateInstance<GameplayCueSet>();
            set.staticNotifies.AddRange(_Notifies);
            notifies.Add(set);
            return new GameplayCueManager(new[] { set });
        }

        [TearDown]
        public void DestroyNotifies()
        {
            foreach (var notify in notifies)
                Object.DestroyImmediate(notify);
            notifies.Clear();
        }
        #endregion



        #region Helpers
        private sealed class RecordingHandler : IGameplayCueHandler
        {
            public readonly List<(Unit Target, GameplayTag Tag, GameplayCueEvent Type, GameplayCueParameters Parameters)> Events = new();
            public readonly List<Unit> Cleared = new();
            public GameplayTag ThrowOn;

            public IEnumerable<GameplayCueEvent> Types => Events.Select(e => e.Type);

            public int Count(GameplayCueEvent _Type) => Events.Count(e => e.Type == _Type);

            public void HandleCue(Unit _Target, GameplayTag _CueTag, GameplayCueEvent _Event, GameplayCueParameters _Parameters)
            {
                if (_CueTag.Equals(ThrowOn)) throw new System.InvalidOperationException("cue failed");
                Events.Add((_Target, _CueTag, _Event, _Parameters));
            }

            public void ClearTarget(Unit _Target) => Cleared.Add(_Target);
        }

        private sealed class ManualCues : EffectExecutionCalculation
        {
            public override void Execute(in ExecutionParams _Params, ExecutionOutput _Output)
            {
                _Output.AddOutputModifier(Health, AddBase, -10);
                _Output.MarkCuesHandledManually();
                _Params.Target.ExecuteCue(TestTags.CueHitFire);
            }
        }

        private sealed class ArmorDamage : EffectExecutionCalculation
        {
            public static readonly AttributeCaptureDefinition TargetArmor = new(Armor, CaptureSource.Target, true);

            public ArmorDamage() => relevantAttributes.Add(TargetArmor);

            public override void Execute(in ExecutionParams _Params, ExecutionOutput _Output)
            {
                _Params.TryGetCapturedMagnitude(TargetArmor, out float armor);
                _Output.AddOutputModifier(Health, AddBase, -armor);
            }
        }
        #endregion
    }



    internal sealed class RecordingNotify : GameplayCueNotifyStatic
    {
        public readonly List<GameplayCueParameters> Calls = new();

        public override void OnExecute(Unit _Target, GameplayCueParameters _Parameters) => Calls.Add(_Parameters);
    }
}
