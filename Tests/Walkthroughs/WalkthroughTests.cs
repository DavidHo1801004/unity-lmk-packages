using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using LMK.Runtime.AbilitySystem;
using LMK.Runtime.GameplayTags;

using static LMK.Editor.AbilitySystem.Tests.TestAttributes;
using static LMK.Runtime.AbilitySystem.ModifierOp;

using Unit = LMK.Runtime.AbilitySystem.GameplayAbilitySystem;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// Six walkthroughs, one mage from setup to death, checking every number.
    /// </summary>
    public sealed class WalkthroughTests
    {
        private TestWorld w;
        private CueLog cues;

        // The assets of walkthroughs 1 and 2.
        private AttributeSetDefinition characterSet;
        private GameplayEffect manaRegen, fireballCost, fireballCooldown, fireballDamage, burn, stun, dead, cleanse;
        private GameplayAbility fireball, death, stunReaction;

        // What the fireball's projectile carries, and whether the cast resumed.
        private readonly List<(GameplayEffectSpec Damage, GameplayEffectSpec Burn)> projectiles = new();
        private bool castResumed;

        private const float CAST_TIME = 0.4f;
        private const float BASE_DAMAGE = 20f;
        private const int FIREBALL_INPUT = 1;

        [SetUp]
        public void SetUp()
        {
            w = new TestWorld();
            cues = new CueLog();
            projectiles.Clear();
            castResumed = false;
            w.World.Cues = new GameplayCueQueue { Handler = cues };

            characterSet = w.Set((MaxHealth, 100f), (Health, null), (MaxMana, 50f), (Mana, null), (AttackPower, 10f), (Armor, 5f), (IncomingDamage, null));
            characterSet.Add(new TransferMetaAttribute { from = IncomingDamage, to = Health, mode = TransferMode.Subtract, eventTag = TestTags.DamageTaken });
            characterSet.Add(new SendEventOnThreshold { attribute = Health, comparison = ThresholdComparison.LessOrEqual, threshold = 0, eventTag = TestTags.Death, once = true });

            manaRegen = w.Effect(e => e.Infinite().Period(1).Mod(Mana, AddBase, 2));
            fireballCost = w.Effect(e => e.Instant().Mod(Mana, AddBase, -30));
            fireballCooldown = w.Effect(e => e.Duration(5).GrantTags(TestTags.CooldownFireball));
            fireballDamage = w.Effect(e => e.Instant().Execution(new DamageExecution()).AssetTags(TestTags.DamageFire).Cue(TestTags.CueHitFire));
            burn = w.Effect(e => e.Duration(6).Period(1).Mod(IncomingDamage, AddBase, 4)
                                  .Stack(StackingType.AggregateBySource, 3, StackExpirationPolicy.ClearEntireStack,
                                         StackDurationRefreshPolicy.RefreshOnSuccessfulApplication, StackPeriodResetPolicy.NeverReset)
                                  .GrantTags(TestTags.Burning, TestTags.Debuff).Cue(TestTags.CueStatusBurning));
            stun = w.Effect(e => e.Duration(2).GrantTags(TestTags.Stun, TestTags.Debuff));
            dead = w.Effect(e => e.Infinite().RemoveOther(GameplayEffectQuery.MatchingAnyGrantedTag(TestTags.Status))
                                  .Immunity(GameplayEffectQuery.MatchingAnyAssetTag(TestTags.Damage)));
            cleanse = w.Effect(e => e.Duration(3).RemoveOther(GameplayEffectQuery.MatchingAnyGrantedTag(TestTags.Debuff))
                                     .Immunity(GameplayEffectQuery.MatchingAnyGrantedTag(TestTags.Debuff)));

            fireball = w.Ability(a => a.Tags(TestTags.AbilityFireball, TestTags.AbilityInterruptible)
                                       .BlockedBy(TestTags.Stun, TestTags.Dead).OwnedTags(TestTags.Casting)
                                       .Cost(fireballCost).Cooldown(fireballCooldown).InstancedPerActor()
                                       .Logic(FireballLogic));
            death = w.Ability(a => a.Trigger(AbilityTriggerSource.GameplayEvent, TestTags.Death).Cancels(TestTags.Ability).OwnedTags(TestTags.Dead)
                                    .Logic(new TestLogic { onActivate = ctx => ctx.ApplyToOwner(ctx.MakeOutgoingSpec(dead)) }));
            stunReaction = w.Ability(a => a.Trigger(AbilityTriggerSource.OwnedTagPresent, TestTags.Stun).Cancels(TestTags.AbilityInterruptible)
                                           .Blocks(TestTags.Ability).Logic(TestLogic.StayActive()));
        }

        [TearDown]
        public void TearDown() => w.Dispose();

        /// <summary>
        /// The fireball cast of walkthrough 2, with the targeting step left out: wait for the cast, commit, make the specs, launch.
        /// </summary>
        private async void FireballLogic(AbilityContext _Context)
        {
            await _Context.Delay(CAST_TIME);
            castResumed = true;

            if (!_Context.Commit())
            {
                _Context.Cancel();
                return;
            }

            var damage = _Context.MakeOutgoingSpec(fireballDamage);
            damage.SetByCaller(TestTags.DataDamage, BASE_DAMAGE);
            projectiles.Add((damage, _Context.MakeOutgoingSpec(burn)));
            _Context.End();
        }

        /// <summary>
        /// Walkthrough 1: a mage with the character set, mana regeneration and three abilities.
        /// </summary>
        private Unit MakeMage()
        {
            var mage = w.MakeUnit(characterSet);
            mage.ApplyEffectToSelf(manaRegen);
            mage.GiveAbility(fireball, 1, FIREBALL_INPUT);
            mage.GiveAbility(death);
            mage.GiveAbility(stunReaction);
            return mage;
        }

        private Unit MakeTarget(float _Armor)
        {
            var target = MakeMage();
            target.SetAttributeBase(Armor, _Armor);
            return target;
        }

        /// <summary>
        /// Press the fireball's input: whether the fireball is casting afterwards.
        /// </summary>
        private bool Press(Unit _Mage)
        {
            _Mage.AbilityInputPressed(FIREBALL_INPUT);
            return _Mage.FindAbility(fireball).IsActive;
        }

        /// <summary>
        /// Cast a fireball and wait until the projectile is launched.
        /// </summary>
        private (GameplayEffectSpec Damage, GameplayEffectSpec Burn) CastFireball(Unit _Mage)
        {
            int launched = projectiles.Count;
            Assert.IsTrue(Press(_Mage));
            w.Advance(CAST_TIME);
            Assert.AreEqual(launched + 1, projectiles.Count, "the projectile is launched after the cast");
            return projectiles.Last();
        }



        [Test]
        public void Walkthrough1_SettingUpAUnit()
        {
            var mage = MakeMage();

            Assert.AreEqual(100f, mage.GetCurrent(MaxHealth));
            Assert.AreEqual(100f, mage.GetCurrent(Health), "registry default");
            Assert.AreEqual(50f, mage.GetCurrent(MaxMana));
            Assert.AreEqual(50f, mage.GetCurrent(Mana), "registry default");
            Assert.AreEqual(10f, mage.GetCurrent(AttackPower));
            Assert.AreEqual(5f, mage.GetCurrent(Armor));
            Assert.AreEqual(0f, mage.GetCurrent(IncomingDamage));
            Assert.AreEqual(3, mage.GetActivatableAbilities().Count);

            w.Advance(3);
            Assert.AreEqual(50f, mage.GetCurrent(Mana), "the regeneration is held at MaxMana by the registry clamp");
        }

        [Test]
        public void Walkthrough2_Fireball_CostCooldownDamageAndBurn()
        {
            var mage = MakeMage();
            var target = MakeTarget(25f);
            var damageTaken = new List<float>();
            target.RegisterGameplayEventListener(TestTags.DamageTaken, e => damageTaken.Add(e.Magnitude));

            // Activation, with the mage at 50 Mana.
            Assert.IsTrue(Press(mage));
            Assert.IsTrue(mage.HasTag(TestTags.Casting));
            Assert.AreEqual(50f, mage.GetCurrent(Mana), "nothing is paid before the commit");

            w.Advance(CAST_TIME);
            Assert.AreEqual(20f, mage.GetBase(Mana), "the cost: 50 → 20");
            Assert.IsTrue(mage.HasTag(TestTags.CooldownFireball));
            Assert.IsFalse(mage.HasTag(TestTags.Casting), "the ability ended");
            var (damage, burnSpec) = projectiles.Single();

            w.Advance(1);
            Assert.IsFalse(Press(mage));
            Assert.IsTrue(w.Events.Failed.Last().Failure.Has(AbilityFailureReason.OnCooldown));

            // Impact on a target with Armor 25 and Health 100.
            cues.Events.Clear();
            target.ApplySpecToSelf(damage);
            Assert.AreEqual(76f, target.GetCurrent(Health), "(20 + 10) × 100 / 125 = 24");
            Assert.AreEqual(0f, target.GetCurrent(IncomingDamage), "the meta attribute resets");
            CollectionAssert.AreEqual(new[] { 24f }, damageTaken);

            var burnHandle = target.ApplySpecToSelf(burnSpec);
            Assert.IsTrue(target.HasTag(TestTags.Burning) && target.HasTag(TestTags.Debuff));

            w.Advance(0.01);
            Assert.IsTrue(cues.Has(TestTags.CueHitFire, GameplayCueEvent.Executed));
            Assert.IsTrue(cues.Has(TestTags.CueStatusBurning, GameplayCueEvent.OnActive));

            w.Advance(6 - 0.01);
            Assert.AreEqual(52f, target.GetCurrent(Health), "six executions of +4: 76 → 52, the last one before expiry");
            Assert.IsNull(target.GetActiveEffect(burnHandle));
            Assert.IsFalse(target.HasTag(TestTags.Burning) || target.HasTag(TestTags.Debuff));
            Assert.IsTrue(cues.Has(TestTags.CueStatusBurning, GameplayCueEvent.Removed));
        }

        [Test]
        public void Walkthrough2_ASecondBurnFromTheSameMageStacks()
        {
            var mage = MakeMage();
            var target = MakeTarget(25f);
            var (_, burnSpec) = CastFireball(mage);

            var h = target.ApplySpecToSelf(burnSpec);
            w.Advance(2);
            Assert.AreEqual(92f, target.GetCurrent(Health), "two executions of 4");

            Assert.AreEqual(h, target.ApplySpecToSelf(burnSpec), "same source: the stack grows");
            var effect = target.GetActiveEffect(h);
            Assert.AreEqual(2, effect.StackCount);
            Assert.AreEqual(w.World.Time + 6, effect.EndTime, 1e-6, "the duration restarts: ends at t+8");

            w.Advance(1);
            Assert.AreEqual(84f, target.GetCurrent(Health), "the period schedule is kept, and each execution is now 4 × 2");

            w.Advance(5);
            Assert.AreEqual(44f, target.GetCurrent(Health), "five more executions of 8, the last at t+8 before expiry");
            Assert.IsNull(target.GetActiveEffect(h));
        }

        [Test]
        public void Walkthrough3_Death()
        {
            var mage = MakeMage();
            var target = MakeTarget(25f);
            var (_, burnSpec) = CastFireball(mage);

            target.ApplySpecToSelf(burnSpec);
            target.ApplyEffectToSelf(stun);
            Assert.IsTrue(target.HasTag(TestTags.Stun) && target.HasTag(TestTags.Burning));
            Assert.IsTrue(target.FindAbility(stunReaction).IsActive);

            // A killing blow: the damage execution with a large base damage.
            var lethal = mage.MakeOutgoingSpec(fireballDamage).SetByCaller(TestTags.DataDamage, 500f);
            target.ApplySpecToSelf(lethal);

            Assert.AreEqual(0f, target.GetCurrent(Health));
            Assert.IsTrue(target.HasTag(TestTags.Dead), "GA_Death activated from Event.Death");
            Assert.IsFalse(target.HasTag(TestTags.Burning) || target.HasTag(TestTags.Stun), "GE_Dead removed every Status effect");
            Assert.IsFalse(target.FindAbility(stunReaction).IsActive, "the stun went, so its reaction ended");

            int blocked = 0;
            target.OnImmunityBlocked += (_, _) => blocked++;
            target.ApplySpecToSelf(mage.MakeOutgoingSpec(fireballDamage).SetByCaller(TestTags.DataDamage, 10f));
            Assert.AreEqual(1, blocked, "later damage is blocked by GE_Dead's immunity");
            Assert.IsFalse(Press(target), "State.Dead blocks the fireball");
        }

        [Test]
        public void Walkthrough4_StunInterruptsACast()
        {
            var mage = MakeMage();
            Assert.IsTrue(Press(mage));
            w.Advance(CAST_TIME / 2);

            mage.ApplyEffectToSelf(stun);
            Assert.IsTrue(mage.FindAbility(stunReaction).IsActive, "OwnedTagPresent activated the reaction");
            Assert.IsFalse(mage.FindAbility(fireball).IsActive, "the reaction cancelled the interruptible cast");
            Assert.IsFalse(mage.HasTag(TestTags.Casting));

            w.Advance(CAST_TIME);
            Assert.IsFalse(castResumed, "the cast's pending await never resumes");
            Assert.AreEqual(50f, mage.GetCurrent(Mana), "Commit never ran: no mana spent");
            Assert.IsFalse(mage.HasTag(TestTags.CooldownFireball), "and no cooldown");
            Assert.IsFalse(Press(mage), "blocked while stunned");

            w.Advance(2);
            Assert.IsFalse(mage.HasTag(TestTags.Stun));
            Assert.IsFalse(mage.FindAbility(stunReaction).IsActive, "the reaction is cancelled when Status.Stun goes");
            Assert.IsTrue(Press(mage), "the block lifted");
        }

        [Test]
        public void Walkthrough5_EquipmentGrantsALiveBuffAndAPassive()
        {
            var thorns = w.Ability(a => a.Logic(TestLogic.StayActive()));
            var axe = w.Effect(e => e.Infinite().Mod(AttackPower, AddBase, Magnitudes.AttributeBased(CaptureSource.Target, MaxHealth, true, 0.1f))
                                     .GrantAbility(thorns).Ongoing(TestTags.Disarmed));
            var mage = MakeMage();

            var h = mage.ApplyEffectToSelf(axe);
            Assert.AreEqual(20f, mage.GetCurrent(AttackPower), "10 + 100 × 0.1");
            Assert.AreEqual(h, mage.FindAbility(thorns).GrantedByEffect);

            mage.ApplyEffectToSelf(w.Effect(e => e.Infinite().Mod(MaxHealth, AddBase, 50)));
            Assert.AreEqual(25f, mage.GetCurrent(AttackPower), "the live capture follows MaxHealth 150");

            mage.AddLooseTag(TestTags.Disarmed);
            Assert.AreEqual(10f, mage.GetCurrent(AttackPower), "inhibited");
            Assert.IsNull(mage.FindAbility(thorns), "the passive is removed");

            mage.RemoveLooseTag(TestTags.Disarmed);
            Assert.AreEqual(25f, mage.GetCurrent(AttackPower));
            Assert.IsNotNull(mage.FindAbility(thorns), "the modifier and the ability return");

            mage.RemoveActiveEffect(h);
            Assert.AreEqual(10f, mage.GetCurrent(AttackPower));
            Assert.IsNull(mage.FindAbility(thorns), "unequipped");
        }

        [Test]
        public void Walkthrough6_CleanseAndImmunity()
        {
            var mage = MakeMage();
            var target = MakeTarget(25f);
            var (damage, burnSpec) = CastFireball(mage);

            target.ApplySpecToSelf(burnSpec);
            target.ApplySpecToSelf(burnSpec);
            target.ApplyEffectToSelf(stun);
            w.Advance(0.01);
            cues.Events.Clear();

            GameplayEffectSpec blockedSpec = null;
            target.OnImmunityBlocked += (spec, _) => blockedSpec = spec;

            target.ApplyEffectToSelf(cleanse);
            Assert.IsFalse(target.HasTag(TestTags.Burning) || target.HasTag(TestTags.Stun) || target.HasTag(TestTags.Debuff),
                           "the burn (2 stacks) and the stun are removed");
            Assert.IsTrue(w.Events.Removed.Where(e => e.Effect.Def == burn || e.Effect.Def == stun).All(e => e.Info.Premature));
            w.Advance(0.01);
            Assert.IsTrue(cues.Has(TestTags.CueStatusBurning, GameplayCueEvent.Removed), "their cues go");

            Assert.AreEqual(ActiveEffectHandle.Invalid, target.ApplySpecToSelf(burnSpec));
            Assert.IsNotNull(blockedSpec, "OnImmunityBlocked");

            float health = target.GetCurrent(Health);
            target.ApplySpecToSelf(damage);
            Assert.Less(target.GetCurrent(Health), health, "the instant damage grants no Status.Debuff: it still applies");
        }



        #region Helpers
        /// <summary>
        /// Walkthrough 2's damage formula: (SetByCaller Data.Damage + source AttackPower) × 100 / (100 + target Armor).
        /// </summary>
        private sealed class DamageExecution : EffectExecutionCalculation
        {
            private static readonly AttributeCaptureDefinition SourceAttackPower = new(AttackPower, CaptureSource.Source, true);
            private static readonly AttributeCaptureDefinition TargetArmor = new(Armor, CaptureSource.Target, false);

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

                float final = UnityEngine.Mathf.Max(0f, (baseDamage + attackPower) * 100f / (100f + armor));
                _Output.AddOutputModifier(IncomingDamage, AddBase, final);
            }
        }

        private sealed class CueLog : IGameplayCueHandler
        {
            public readonly List<(GameplayTag Tag, GameplayCueEvent Type)> Events = new();

            public bool Has(GameplayTag _Tag, GameplayCueEvent _Type) => Events.Any(e => e.Tag.Equals(_Tag) && e.Type == _Type);

            public void HandleCue(Unit _Target, GameplayTag _CueTag, GameplayCueEvent _Event, GameplayCueParameters _Parameters) => Events.Add((_CueTag, _Event));

            public void ClearTarget(Unit _Target) { }
        }
        #endregion
    }
}
