using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using UnityEngine;

using LMK.Runtime.AbilitySystem;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

using static LMK.Editor.AbilitySystem.Tests.TestAttributes;

using Unit = LMK.Runtime.AbilitySystem.GameplayAbilitySystem;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// A unit's attributes: initialisation, the aggregator through the internal modifier API, clamping,
    /// meta attributes, behaviours and propagation.
    /// </summary>
    public sealed class AttributeRuntimeTests
    {
        private TestWorld w;

        [SetUp]
        public void SetUp() => w = new TestWorld();

        [TearDown]
        public void TearDown() => w.Dispose();

        private static readonly GameplayTag DamagedEvent = GameplayTag.FromId(RegistryGuid.Parse("e1000000000000000000000000000001"), new RegistryGuid(1, 1), "Event.Damaged");
        private static readonly GameplayTag DeathEvent = GameplayTag.FromId(RegistryGuid.Parse("e1000000000000000000000000000001"), new RegistryGuid(1, 2), "Event.Death");



        #region Initialisation
        [Test]
        public void InitOrder_BoundBeforeDependent()
        {
            var set = w.Set((Health, 250f), (MaxHealth, 300f));          // Health is listed first
            var unit = w.MakeUnit(set);

            Assert.AreEqual(250f, unit.GetCurrent(Health), "not clamped to MaxHealth's registry default of 100");
            Assert.AreEqual(300f, unit.GetCurrent(MaxHealth));
            Assert.AreEqual(0, w.Events.AttributeChanges);
            Assert.AreEqual(1, w.Events.Initializations);
        }

        [Test]
        public void Init_UsesRegistryDefaults_AndClampsToBounds()
        {
            var unit = w.MakeUnit(w.Set((MaxHealth, null), (Health, 500f), (Mana, null), (MaxMana, 20f), (Armor, -5f)));

            Assert.AreEqual(100f, unit.GetBase(MaxHealth), "the registry default");
            Assert.AreEqual(100f, unit.GetCurrent(Health), "clamped to MaxHealth");
            Assert.AreEqual(100f, unit.GetBase(Health), "the base is clamped too");
            Assert.AreEqual(20f, unit.GetCurrent(Mana), "the default 50 clamped to MaxMana");
            Assert.AreEqual(0f, unit.GetCurrent(Armor), "constant min 0");
            Assert.IsFalse(unit.HasAttribute(AttackPower));
            Assert.AreEqual(0f, unit.GetCurrent(AttackPower), "0 if absent");
        }

        [Test]
        public void TwoSets_ShareAnAttribute_FirstValueWins_AndItLeavesWithTheLastSet()
        {
            var first = w.Set((Health, 40f), (MaxHealth, null));
            var second = w.Set((Health, 80f), (Armor, 5f));
            var unit = w.MakeUnit(first, second);

            Assert.AreEqual(40f, unit.GetCurrent(Health));
            Assert.AreEqual(2, w.Events.Initializations, "one event per set");
            CollectionAssert.AreEqual(new[] { first, second }, unit.AttributeSets);

            Assert.IsTrue(unit.RemoveAttributeSet(first));
            Assert.IsTrue(unit.HasAttribute(Health), "the second set still lists it");
            Assert.IsFalse(unit.HasAttribute(MaxHealth));

            Assert.IsTrue(unit.RemoveAttributeSet(second));
            Assert.IsFalse(unit.HasAttribute(Health));
            Assert.IsFalse(unit.RemoveAttributeSet(second), "already removed");
        }

        [Test]
        public void AddingASetTwice_IsRefused()
        {
            var set = w.Set((Health, 40f));
            var unit = w.MakeUnit(set);

            w.Log.Expect(LogType.Warning, "already added");
            Assert.IsFalse(unit.AddAttributeSet(set));
        }

        [Test]
        public void UnresolvedAttributes_AreSkipped()
        {
            var ghost = GameplayAttribute.FromId(AttributeIds.MainRegistry, RegistryGuid.NewGuid(), "Vitals.Ghost");
            var unit = w.MakeUnit(w.Set((ghost, 5f), (Armor, 3f)));

            Assert.IsFalse(unit.HasAttribute(ghost));
            Assert.AreEqual(0f, unit.GetCurrent(ghost));
            Assert.AreEqual(3f, unit.GetCurrent(Armor));
        }

        [Test]
        public void InitialValues_ScaleWithTheLevel()
        {
            var set = w.Set();
            set.Add(new AttributeSetDefinition.Entry(AttackPower, true, new ScalableFloat(10f, AnimationCurve.Linear(1f, 1f, 10f, 10f))));
            var unit = w.World.CreateSystem();

            unit.AddAttributeSet(set, 3f);

            Assert.AreEqual(30f, unit.GetCurrent(AttackPower), 1e-4f);
        }
        #endregion



        #region Base values
        [Test]
        public void SetAttributeBase_ClampsAndFiresOneChange()
        {
            var unit = w.MakeUnit((MaxHealth, 100f), (Health, 50f));

            Assert.IsTrue(unit.SetAttributeBase(Health, 150f));

            Assert.AreEqual(100f, unit.GetBase(Health));
            Assert.AreEqual(100f, unit.GetCurrent(Health));
            Assert.AreEqual(1, w.Events.AttributeChanges);
            Assert.AreEqual(new AttributeChange(Health, 50f, 100f).ToString(), w.Events.Changes[0].ToString());
            Assert.IsFalse(unit.SetAttributeBase(AttackPower, 1f), "the unit lacks it");
        }

        [Test]
        public void IntegerAttributes_RoundHalvesAwayFromZero()
        {
            var unit = w.MakeUnit((Charges, 0f));
            var modifier = unit.AddModifier(Charges, ModifierOp.AddFinal, 2.5f);
            Assert.AreEqual(3f, unit.GetCurrent(Charges));

            unit.SetModifierMagnitude(modifier, 2.4f);
            Assert.AreEqual(2f, unit.GetCurrent(Charges));

            unit.SetModifierMagnitude(modifier, 1.5f);
            Assert.AreEqual(2f, unit.GetCurrent(Charges), "1.5 rounds to 2");
            Assert.AreEqual(0f, unit.GetBase(Charges), "the base is not rounded");
        }
        #endregion



        #region Aggregator
        private Unit TenAttackPower() => w.MakeUnit((AttackPower, 10f));

        [TestCase(ModifierOp.AddBase, 5f, 15f)]
        [TestCase(ModifierOp.MultiplyAdditive, 1.5f, 15f)]
        [TestCase(ModifierOp.DivideAdditive, 2f, 5f)]
        [TestCase(ModifierOp.MultiplyCompound, 3f, 30f)]
        [TestCase(ModifierOp.AddFinal, 4f, 14f)]
        [TestCase(ModifierOp.Override, 42f, 42f)]
        public void EachOp_OnItsOwn(ModifierOp _Op, float _Magnitude, float _Expected)
        {
            var unit = TenAttackPower();
            var modifier = unit.AddModifier(AttackPower, _Op, _Magnitude);

            Assert.AreEqual(_Expected, unit.GetCurrent(AttackPower), 1e-4f);
            Assert.AreEqual(10f, unit.GetBase(AttackPower), "modifiers never touch the base");

            Assert.IsTrue(unit.RemoveModifier(modifier));
            Assert.AreEqual(10f, unit.GetCurrent(AttackPower));
            Assert.IsFalse(unit.RemoveModifier(modifier), "a removed handle is stale");
        }

        [Test]
        public void AllOps_FollowTheUE54Formula()
        {
            var unit = TenAttackPower();
            unit.AddModifier(AttackPower, ModifierOp.AddBase, 5f);             // 15
            unit.AddModifier(AttackPower, ModifierOp.MultiplyAdditive, 1.5f);   // 1 + 0.5 + 1 = 2.5
            unit.AddModifier(AttackPower, ModifierOp.MultiplyAdditive, 2f);
            unit.AddModifier(AttackPower, ModifierOp.DivideAdditive, 2f);       // 1 + 1 + 0.5 = 2.5
            unit.AddModifier(AttackPower, ModifierOp.DivideAdditive, 1.5f);
            unit.AddModifier(AttackPower, ModifierOp.MultiplyCompound, 2f);     // × 2 × 1.5 = 3
            unit.AddModifier(AttackPower, ModifierOp.MultiplyCompound, 1.5f);
            unit.AddModifier(AttackPower, ModifierOp.AddFinal, 7f);

            Assert.AreEqual(15f * 2.5f / 2.5f * 3f + 7f, unit.GetCurrent(AttackPower), 1e-4f);
        }

        [Test]
        public void Override_FirstAppliedWins_AndTheNextTakesOverWhenItGoes()
        {
            var unit = TenAttackPower();
            var first = unit.AddModifier(AttackPower, ModifierOp.Override, 50f);
            unit.AddModifier(AttackPower, ModifierOp.Override, 80f);
            unit.AddModifier(AttackPower, ModifierOp.AddBase, 100f);

            Assert.AreEqual(50f, unit.GetCurrent(AttackPower), "first applied, and other ops of the channel don't count");

            unit.RemoveModifier(first);
            Assert.AreEqual(80f, unit.GetCurrent(AttackPower));
        }

        [Test]
        public void Channels_FeedEachOtherInOrder()
        {
            var unit = TenAttackPower();
            unit.AddModifier(AttackPower, ModifierOp.AddBase, 5f, _Channel: 1);
            unit.AddModifier(AttackPower, ModifierOp.MultiplyAdditive, 2f, _Channel: 0);

            Assert.AreEqual(25f, unit.GetCurrent(AttackPower), "(10 × 2) + 5, where one channel would give (10 + 5) × 2");

            unit.AddModifier(AttackPower, ModifierOp.Override, 1f, _Channel: 0);
            Assert.AreEqual(6f, unit.GetCurrent(AttackPower), "an override ends only its own channel");
        }

        [Test]
        public void Stacks_FoldTheMagnitude()
        {
            var unit = TenAttackPower();

            var add = unit.AddModifier(AttackPower, ModifierOp.AddBase, 5f, _StackCount: 3);
            Assert.AreEqual(25f, unit.GetCurrent(AttackPower), "additive: m × count");
            unit.RemoveModifier(add);

            var multiply = unit.AddModifier(AttackPower, ModifierOp.MultiplyAdditive, 1.5f, _StackCount: 2);
            Assert.AreEqual(20f, unit.GetCurrent(AttackPower), "multiply: 1 + (m − 1) × count");
            unit.SetModifierStackCount(multiply, 4);
            Assert.AreEqual(30f, unit.GetCurrent(AttackPower));
            unit.RemoveModifier(multiply);

            unit.AddModifier(AttackPower, ModifierOp.MultiplyCompound, 2f, _StackCount: 3);
            Assert.AreEqual(40f, unit.GetCurrent(AttackPower), "compound: 1 + (2 − 1) × 3 = 4");

            unit.AddModifier(AttackPower, ModifierOp.Override, 7f, _StackCount: 5);
            Assert.AreEqual(7f, unit.GetCurrent(AttackPower), "override ignores the count");
        }

        [Test]
        public void Qualification_CountsAModifierOnlyWhileItPasses()
        {
            var unit = TenAttackPower();
            bool qualifies = false;
            unit.AddModifier(AttackPower, ModifierOp.AddFinal, 5f, _Qualifies: () => qualifies);
            Assert.AreEqual(10f, unit.GetCurrent(AttackPower));

            qualifies = true;
            unit.ReevaluateAttributes();
            Assert.AreEqual(15f, unit.GetCurrent(AttackPower));
        }

        [Test]
        public void ZeroDivisor_CountsAsOne_WithAWarning()
        {
            var unit = TenAttackPower();

            w.Log.Expect(LogType.Warning, "zero divisor");
            unit.AddModifier(AttackPower, ModifierOp.DivideAdditive, 0f);     // 1 + (0 − 1) = 0

            Assert.AreEqual(10f, unit.GetCurrent(AttackPower));
        }

        [Test]
        public void NonFiniteValues_AreRejected()
        {
            var unit = TenAttackPower();

            w.Log.Expect(LogType.Error, "non-finite");
            Assert.IsFalse(unit.ExecuteModifier(AttackPower, ModifierOp.AddBase, float.NaN));
            Assert.AreEqual(10f, unit.GetBase(AttackPower));

            w.Log.Expect(LogType.Error, "non-finite");
            unit.AddModifier(AttackPower, ModifierOp.MultiplyCompound, float.PositiveInfinity);
            Assert.AreEqual(10f, unit.GetCurrent(AttackPower), "the previous value stays");

            w.Log.Expect(LogType.Error, "non-finite");
            Assert.IsFalse(unit.SetAttributeBase(AttackPower, float.NegativeInfinity));
        }

        [Test]
        public void Modifiers_WaitWhileTheUnitLacksTheAttribute()
        {
            var set = w.Set((Armor, 2f));
            var unit = w.MakeUnit(w.Set((Health, 10f)));

            unit.AddModifier(Armor, ModifierOp.AddFinal, 5f);
            Assert.AreEqual(0f, unit.GetCurrent(Armor));

            unit.AddAttributeSet(set);
            Assert.AreEqual(7f, unit.GetCurrent(Armor), "it applies once the attribute arrives");

            unit.RemoveAttributeSet(set);
            unit.AddAttributeSet(set);
            Assert.AreEqual(7f, unit.GetCurrent(Armor), "and again when it comes back");
        }
        #endregion



        #region Clamping and bound changes
        [Test]
        public void CurrentValue_IsClampedToItsBounds()
        {
            var unit = w.MakeUnit((MaxHealth, 100f), (Health, 100f));

            unit.AddModifier(Health, ModifierOp.AddFinal, 50f);
            Assert.AreEqual(100f, unit.GetCurrent(Health));

            unit.AddModifier(Health, ModifierOp.AddFinal, -500f);
            Assert.AreEqual(0f, unit.GetCurrent(Health), "constant min 0");
        }

        [Test]
        public void LoweredMax_ReclampsTheBase_AndRaisingItAgainDoesNotRestoreIt()
        {
            var unit = w.MakeUnit((MaxHealth, 100f), (Health, 100f));

            var debuff = unit.AddModifier(MaxHealth, ModifierOp.AddFinal, -20f);
            Assert.AreEqual(80f, unit.GetBase(Health), "the base is clamped, not only the current value");
            Assert.AreEqual(80f, unit.GetCurrent(Health));

            unit.RemoveModifier(debuff);
            Assert.AreEqual(80f, unit.GetCurrent(Health), "clamp keeps the value");
        }

        [Test]
        public void MaxBuffExpires_HiddenBaseAboveTheCapIsGone()
        {
            var unit = w.MakeUnit((MaxHealth, 100f), (Health, 100f));
            var buff = unit.AddModifier(MaxHealth, ModifierOp.AddBase, 50f);
            unit.ExecuteModifier(Health, ModifierOp.AddBase, 40f);
            Assert.AreEqual(140f, unit.GetBase(Health));

            unit.RemoveModifier(buff);

            Assert.AreEqual(100f, unit.GetBase(Health));
            unit.ExecuteModifier(Health, ModifierOp.AddBase, -30f);
            Assert.AreEqual(70f, unit.GetCurrent(Health), "no hidden health absorbed the hit");
        }

        [Test]
        public void KeepRatio_ScalesTheBaseWithItsMax()
        {
            var unit = w.MakeUnit((MaxMana, 50f), (Mana, 25f));

            var buff = unit.AddModifier(MaxMana, ModifierOp.MultiplyAdditive, 2f);
            Assert.AreEqual(100f, unit.GetCurrent(MaxMana));
            Assert.AreEqual(50f, unit.GetBase(Mana), "25 of 50 becomes 50 of 100");

            unit.RemoveModifier(buff);
            Assert.AreEqual(25f, unit.GetBase(Mana));
        }

        [Test]
        public void AUnitWithoutTheBoundingAttribute_HasNoSuchBound()
        {
            var unit = w.MakeUnit((Health, 500f));

            Assert.AreEqual(500f, unit.GetCurrent(Health));
        }
        #endregion



        #region Meta attributes and behaviours
        private Unit DamageableUnit(params AttributeBehaviour[] _Behaviours)
        {
            var set = w.Set((MaxHealth, 100f), (Health, 100f), (IncomingDamage, null));
            foreach (var behaviour in _Behaviours)
                set.Add(behaviour);

            return w.MakeUnit(set);
        }

        [Test]
        public void TransferMetaAttribute_MovesDamageToHealth_AndTheMetaValueResets()
        {
            var unit = DamageableUnit(new TransferMetaAttribute { from = IncomingDamage, to = Health, eventTag = DamagedEvent });
            w.Events.Clear();

            Assert.IsTrue(unit.ExecuteModifier(IncomingDamage, ModifierOp.AddBase, 30f));

            Assert.AreEqual(70f, unit.GetCurrent(Health));
            Assert.AreEqual(0f, unit.GetCurrent(IncomingDamage), "reset to its default after PostEffectExecute");
            Assert.AreEqual(0f, unit.GetBase(IncomingDamage));
            Assert.AreEqual(0, w.Events.ChangesOf(IncomingDamage), "a meta attribute fires no change event");
            Assert.AreEqual(1, w.Events.ChangesOf(Health));
            Assert.AreEqual(1, w.Events.GameplayEvents.Count);
            Assert.AreEqual(30f, w.Events.GameplayEvents[0].Magnitude);
        }

        [Test]
        public void MetaAttributes_IgnoreDurationModifiers()
        {
            var unit = DamageableUnit();

            unit.AddModifier(IncomingDamage, ModifierOp.AddFinal, 10f);

            Assert.AreEqual(0f, unit.GetCurrent(IncomingDamage));
        }

        [Test]
        public void SendEventOnThreshold_FiresWhenTheValueCrosses()
        {
            var death = new SendEventOnThreshold { attribute = Health, comparison = ThresholdComparison.LessOrEqual, threshold = 0f, eventTag = DeathEvent };
            var unit = DamageableUnit(death);

            unit.ExecuteModifier(Health, ModifierOp.AddBase, -60f);
            Assert.AreEqual(0, w.Events.GameplayEvents.Count, "40 is above the threshold");

            unit.ExecuteModifier(Health, ModifierOp.AddBase, -60f);
            unit.ExecuteModifier(Health, ModifierOp.AddBase, -10f);
            Assert.AreEqual(1, w.Events.GameplayEvents.Count, "once on crossing, not while it stays across");
            Assert.AreEqual(DeathEvent, w.Events.GameplayEvents[0].EventTag);

            unit.SetAttributeBase(Health, 50f);
            unit.ExecuteModifier(Health, ModifierOp.AddBase, -100f);
            Assert.AreEqual(2, w.Events.GameplayEvents.Count, "it fires again after recovering");
        }

        [Test]
        public void SendEventOnThreshold_Once_FiresOncePerUnit()
        {
            var death = new SendEventOnThreshold { attribute = Health, threshold = 0f, eventTag = DeathEvent, once = true };
            var first = DamageableUnit(death);
            var second = w.MakeUnit(first.AttributeSets.ToArray());

            first.ExecuteModifier(Health, ModifierOp.AddBase, -200f);
            first.SetAttributeBase(Health, 50f);
            first.ExecuteModifier(Health, ModifierOp.AddBase, -200f);
            second.ExecuteModifier(Health, ModifierOp.AddBase, -200f);

            Assert.AreEqual(2, w.Events.GameplayEvents.Count, "one per unit, though both share the behaviour");
        }

        private sealed class ArmorBehaviour : AttributeBehaviour
        {
            public readonly List<string> calls = new();

            public override bool PreEffectExecute(Unit _System, ref ModCallbackData _Data)
            {
                calls.Add("PreEffectExecute");
                if (_Data.Attribute != Health) return true;
                if (_Data.Magnitude < -100f) return false;                 // vetoes overkill

                _Data.Magnitude *= 0.5f;                                   // halves the rest
                return true;
            }

            public override void PreBaseChange(Unit _System, GameplayAttribute _Attribute, ref float _NewBase) => calls.Add("PreBaseChange");

            public override void PreAttributeChange(Unit _System, GameplayAttribute _Attribute, ref float _NewCurrent) => calls.Add("PreAttributeChange");

            public override void PostAttributeChange(Unit _System, GameplayAttribute _Attribute, float _OldValue, float _NewValue) => calls.Add("PostAttributeChange");

            public override void PostEffectExecute(Unit _System, in ModCallbackData _Data) => calls.Add("PostEffectExecute");
        }

        [Test]
        public void Behaviours_CanVetoOrChangeAnExecutedModifier_AndRunInOrder()
        {
            var armor = new ArmorBehaviour();
            var unit = DamageableUnit(armor);

            Assert.IsTrue(unit.ExecuteModifier(Health, ModifierOp.AddBase, -40f));
            Assert.AreEqual(80f, unit.GetCurrent(Health), "halved");
            CollectionAssert.AreEqual(new[] { "PreEffectExecute", "PreBaseChange", "PreAttributeChange", "PostAttributeChange", "PostEffectExecute" }, armor.calls);

            Assert.IsFalse(unit.ExecuteModifier(Health, ModifierOp.AddBase, -500f));
            Assert.AreEqual(80f, unit.GetCurrent(Health), "vetoed");
        }

        private sealed class RunawayBehaviour : AttributeBehaviour
        {
            public override void PostAttributeChange(Unit _System, GameplayAttribute _Attribute, float _OldValue, float _NewValue)
            {
                if (_Attribute == A) _System.SetAttributeBase(A, _NewValue + 1f);
            }
        }

        [Test]
        public void ChangesNestedThroughBehaviours_StopAtTheApplicationDepth()
        {
            var set = w.Set((A, 0f));
            set.Add(new RunawayBehaviour());
            var unit = w.MakeUnit(set);

            w.Log.Expect(LogType.Error, "attribute cycle");
            unit.SetAttributeBase(A, 1f);

            // Reaching this line proves there was no stack overflow.
            Assert.IsTrue(float.IsFinite(unit.GetCurrent(A)));
            Assert.AreEqual(w.Settings.MaxApplicationDepth, unit.GetCurrent(A), 1f);
        }
        #endregion



        #region Propagation
        [Test]
        public void PropagationCycle_StopsAtThePassLimit()
        {
            var unit = w.MakeUnit((A, 10f), (B, 10f));
            var onA = unit.AddModifier(A, ModifierOp.AddFinal, 0f);
            var onB = unit.AddModifier(B, ModifierOp.AddFinal, 0f);

            // A live link each way, as two non-snapshot attribute captures would make.
            unit.Attributes.Propagated += (attribute, _, value) =>
            {
                if (attribute == A) unit.SetModifierMagnitude(onB, value);
                if (attribute == B) unit.SetModifierMagnitude(onA, value);
            };

            w.Log.Expect(LogType.Error, "attribute cycle");
            unit.SetAttributeBase(A, 11f);

            // Reaching this line proves there was no hang.
            Assert.IsTrue(float.IsFinite(unit.GetCurrent(A)) && float.IsFinite(unit.GetCurrent(B)));
            Assert.AreEqual(1, w.Log.ErrorCount);
        }

        [Test]
        public void Propagation_RunsBeforeTheMutatorReturns()
        {
            var unit = w.MakeUnit((MaxHealth, 100f), (Health, 100f));
            var seen = new List<float>();
            unit.OnAttributeChanged += change =>
            {
                if (change.Attribute == MaxHealth) seen.Add(unit.GetCurrent(Health));
            };

            unit.SetAttributeBase(MaxHealth, 60f);

            Assert.AreEqual(100f, seen.Single(), "listeners run at once, before knock-on changes");
            Assert.AreEqual(60f, unit.GetCurrent(Health), "the knock-on change is done when the mutator returns");
        }
        #endregion
    }
}
