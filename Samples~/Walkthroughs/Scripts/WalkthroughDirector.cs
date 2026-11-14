using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using LMK.Runtime.AbilitySystem;
using LMK.Runtime.GameplayTags;

using static LMK.Samples.AbilitySystem.Walkthroughs.WalkthroughAttributes;
using static LMK.Samples.AbilitySystem.Walkthroughs.WalkthroughTags;

namespace LMK.Samples.AbilitySystem.Walkthroughs
{
    /// <summary>
    /// Runs the six walkthroughs in the scene, with a fresh mage and target dummy for each, and checks
    /// the numbers each one expects. Use the buttons in the Game view, or watch the units in
    /// Tools → LMK → Ability System → Debugger.
    /// </summary>
    public sealed class WalkthroughDirector : MonoBehaviour
    {
        [Header("Scene")]
        public AbilitySystemWorldRunner runner;
        public AbilitySystemComponent magePrefab;
        public AbilitySystemComponent dummyPrefab;
        public Vector3 magePosition = new(-3f, 0f, 0f);
        public Vector3 dummyPosition = new(3f, 0f, 0f);

        [Header("Assets")]
        public GameplayAbility fireball;
        public GameplayAbility stunReaction;
        public GameplayAbility thorns;
        public GameplayEffect fireballDamage;
        public GameplayEffect burn;
        public GameplayEffect stun;
        public GameplayEffect berserkerAxe;
        public GameplayEffect maxHealthBuff;
        public GameplayEffect cleanse;
        public int fireballInput = 1;

        [Tooltip("Run every walkthrough when the scene starts.")]
        public bool runOnStart;

        private readonly List<string> log = new();
        private AbilitySystemComponent mage, dummy;
        private Vector2 scroll;

        public static readonly string[] Titles =
        {
            "11.1 Setting up a unit",
            "11.2 Fireball: cost, cooldown, damage and burn",
            "11.3 Death",
            "11.4 Stun interrupts a cast",
            "11.5 Equipment: live buff and passive",
            "11.6 Cleanse and immunity",
        };

        public IReadOnlyList<string> Log => log;

        /// <summary>
        /// Checks that failed since the director started.
        /// </summary>
        public int Failures { get; private set; }

        public int Checks { get; private set; }

        public bool IsRunning { get; private set; }

        private AbilitySystemWorld World => runner.World;

        private GameplayAbilitySystem Mage => mage.System;

        private GameplayAbilitySystem Dummy => dummy.System;

        private void Start()
        {
            if (runOnStart) StartCoroutine(RunAll());
        }

        public IEnumerator RunAll()
        {
            for (int i = 0; i < Titles.Length; i++)
                yield return Run(i);

            Write($"Done: {Checks - Failures} of {Checks} checks passed.");
        }

        public IEnumerator Run(int _Index)
        {
            IsRunning = true;
            Write($"— {Titles[_Index]} —");
            yield return Spawn();

            yield return _Index switch
            {
                0 => Setup(),
                1 => Fireball(),
                2 => Death(),
                3 => StunInterruptsACast(),
                4 => Equipment(),
                _ => CleanseAndImmunity(),
            };

            IsRunning = false;
        }



        #region Walkthroughs
        private IEnumerator Setup()
        {
            Expect("MaxHealth", Mage.GetCurrent(MaxHealth), 100);
            Expect("Health (registry default)", Mage.GetCurrent(Health), 100);
            Expect("MaxMana", Mage.GetCurrent(MaxMana), 50);
            Expect("Mana (registry default)", Mage.GetCurrent(Mana), 50);
            Expect("AttackPower", Mage.GetCurrent(AttackPower), 10);
            Expect("Armor", Mage.GetCurrent(Armor), 5);
            Check("three abilities granted", Mage.GetActivatableAbilities().Count == 3);

            yield return WaitWorld(2.5);
            Expect("Mana after regeneration, held at MaxMana by the registry clamp", Mage.GetCurrent(Mana), 50);
        }

        private IEnumerator Fireball()
        {
            Press();
            Check("the fireball is casting", IsCasting(Mage) && Mage.HasTag(StateCasting));
            Expect("Mana before the commit", Mage.GetCurrent(Mana), 50);

            yield return WaitUntil(() => !IsCasting(Mage), 3);
            float mana = Mage.GetBase(Mana);
            Check($"the cost was paid: Mana 50 → 20 (now {mana:0.#}, with regeneration)", mana >= 20 && mana <= 22);
            Check("Cooldown.Fireball", Mage.HasTag(CooldownFireball));
            Check("State.Casting removed when the ability ended", !Mage.HasTag(StateCasting));

            yield return WaitUntil(() => Dummy.GetCurrent(Health) < 100, 5);
            Expect("target Health after the impact: (20 + 10) × 100 / 125 = 24 damage", Dummy.GetCurrent(Health), 76);
            Check("the target is burning", Dummy.HasTag(StatusBurning) && Dummy.HasTag(StatusDebuff));

            Press();
            Check("a second fireball fails: on cooldown", !IsCasting(Mage) && Mage.GetCooldownRemaining(fireball) > 0);

            yield return WaitUntil(() => !Dummy.HasTag(StatusBurning), 8);
            Expect("target Health after six burn ticks of 4", Dummy.GetCurrent(Health), 52);
        }

        private IEnumerator Death()
        {
            Dummy.ApplyEffectToSelf(stun);
            Press();
            yield return WaitUntil(() => Dummy.HasTag(StatusBurning), 5);
            Check("the target is burning and stunned", Dummy.HasTag(StatusBurning) && Dummy.HasTag(StatusStun));

            Write("A killing blow: the fireball's damage with 500 base damage.");
            Dummy.ApplySpecToSelf(Mage.MakeOutgoingSpec(fireballDamage).SetByCaller(DataDamage, 500));
            Expect("target Health", Dummy.GetCurrent(Health), 0);
            Check("Event.Death activated GA_Death: State.Dead", Dummy.HasTag(StateDead));
            Check("GE_Dead removed the burn and the stun", !Dummy.HasTag(StatusBurning) && !Dummy.HasTag(StatusStun));

            int blocked = 0;
            Dummy.OnImmunityBlocked += (_, _) => blocked++;
            Dummy.ApplySpecToSelf(Mage.MakeOutgoingSpec(fireballDamage).SetByCaller(DataDamage, 10));
            Check("later damage is blocked by GE_Dead's immunity", blocked == 1);
        }

        private IEnumerator StunInterruptsACast()
        {
            Press();
            yield return WaitWorld(0.2);

            Write("The mage is stunned mid-cast.");
            Mage.ApplyEffectToSelf(stun);
            Check("GA_StunReaction activated on Status.Stun", Mage.FindAbility(stunReaction).IsActive);
            Check("it cancelled the interruptible fireball", !IsCasting(Mage) && !Mage.HasTag(StateCasting));

            yield return WaitWorld(0.5);
            Expect("Commit never ran: Mana", Mage.GetCurrent(Mana), 50);
            Check("and no cooldown", !Mage.HasTag(CooldownFireball));
            Press();
            Check("abilities are blocked while stunned", !IsCasting(Mage));

            yield return WaitWorld(2);
            Check("the stun expired, and GA_StunReaction was cancelled", !Mage.HasTag(StatusStun) && !Mage.FindAbility(stunReaction).IsActive);
            Press();
            Check("the block lifted: the fireball casts", IsCasting(Mage));
            yield return WaitUntil(() => !IsCasting(Mage), 3);
        }

        private IEnumerator Equipment()
        {
            var h = Mage.ApplyEffectToSelf(berserkerAxe);
            Expect("AttackPower with the axe: 10 + MaxHealth 100 × 0.1", Mage.GetCurrent(AttackPower), 20);
            Check("GA_Thorns granted by the axe", Mage.FindAbility(thorns)?.GrantedByEffect == h);

            Mage.ApplyEffectToSelf(maxHealthBuff);
            Expect("AttackPower after a +50 MaxHealth buff (live capture)", Mage.GetCurrent(AttackPower), 25);

            Mage.AddLooseTag(StatusDisarmed);
            Expect("Disarmed: the axe is inhibited, AttackPower", Mage.GetCurrent(AttackPower), 10);
            Check("and GA_Thorns is removed", Mage.FindAbility(thorns) == null);

            Mage.RemoveLooseTag(StatusDisarmed);
            Expect("Disarmed removed: AttackPower", Mage.GetCurrent(AttackPower), 25);
            Check("and GA_Thorns returns", Mage.FindAbility(thorns) != null);

            Mage.RemoveActiveEffect(h);
            Expect("unequipped: AttackPower", Mage.GetCurrent(AttackPower), 10);
            Check("and GA_Thorns is gone", Mage.FindAbility(thorns) == null);
            yield break;
        }

        private IEnumerator CleanseAndImmunity()
        {
            var burnSpec = Mage.MakeOutgoingSpec(burn);
            Dummy.ApplySpecToSelf(burnSpec);
            Dummy.ApplySpecToSelf(burnSpec);
            Dummy.ApplyEffectToSelf(stun);
            Expect("burn stacks from one source", Dummy.GetActiveEffects().FirstOrDefault(e => e.Def == burn)?.StackCount ?? 0, 2);
            yield return WaitWorld(0.5);

            int blocked = 0;
            Dummy.OnImmunityBlocked += (_, _) => blocked++;

            Dummy.ApplyEffectToSelf(cleanse);
            Check("the cleanse removed the burn and the stun", !Dummy.HasTag(StatusBurning) && !Dummy.HasTag(StatusStun) && !Dummy.HasTag(StatusDebuff));

            Dummy.ApplySpecToSelf(burnSpec);
            Check("a new burn is blocked by immunity", blocked == 1 && !Dummy.HasTag(StatusBurning));

            float health = Dummy.GetCurrent(Health);
            Dummy.ApplySpecToSelf(Mage.MakeOutgoingSpec(fireballDamage).SetByCaller(DataDamage, 20));
            Check("instant damage grants no Status.Debuff: it still applies", Dummy.GetCurrent(Health) < health);
            yield return WaitWorld(3);
            Check("the cleanse expired", !Dummy.GetActiveEffects().Any(e => e.Def == cleanse));
        }
        #endregion



        #region Helpers
        private IEnumerator Spawn()
        {
            foreach (var unit in new[] { mage, dummy })
                if (unit != null)
                {
                    unit.System?.Shutdown();
                    Destroy(unit.gameObject);
                }

            mage = Instantiate(magePrefab, magePosition, Quaternion.identity);
            mage.name = "Mage";
            dummy = Instantiate(dummyPrefab, dummyPosition, Quaternion.identity);
            dummy.name = "Target Dummy";

            yield return null;          // Start: sets, startup effects and abilities
        }

        private void Press() => Mage.AbilityInputPressed(fireballInput);

        private bool IsCasting(GameplayAbilitySystem _Unit) => _Unit.FindAbility(fireball)?.IsActive ?? false;

        private IEnumerator WaitWorld(double _Seconds)
        {
            double end = World.Time + _Seconds;
            while (World.Time < end - 1e-6)
                yield return null;
        }

        private IEnumerator WaitUntil(Func<bool> _Condition, double _Timeout)
        {
            double end = World.Time + _Timeout;
            while (!_Condition())
            {
                if (World.Time >= end)
                {
                    Check("timed out", false);
                    yield break;
                }

                yield return null;
            }
        }

        private void Expect(string _Label, float _Actual, float _Expected)
            => Check($"{_Label}: {_Actual:0.##} (expected {_Expected:0.##})", Mathf.Abs(_Actual - _Expected) < 0.01f);

        private void Check(string _Label, bool _Passed)
        {
            Checks++;
            if (!_Passed) Failures++;

            Write($"{(_Passed ? "✓" : "✗")} {_Label}");
            if (!_Passed) Debug.LogWarning($"[Walkthroughs] Failed: {_Label}", this);
        }

        private void Write(string _Line)
        {
            string line = $"[{(runner != null ? World.Time : 0):0.00}s] {_Line}";
            log.Add(line);
            Debug.Log($"[Walkthroughs] {line}", this);
        }
        #endregion



        #region UI
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 330, Screen.height - 20), GUI.skin.box);
            GUI.enabled = !IsRunning;
            if (GUILayout.Button("Run all")) StartCoroutine(RunAll());
            for (int i = 0; i < Titles.Length; i++)
                if (GUILayout.Button(Titles[i]))
                    StartCoroutine(Run(i));
            GUI.enabled = true;

            GUILayout.Space(8);
            if (mage != null && mage.System != null) Readout(mage.System);
            if (dummy != null && dummy.System != null) Readout(dummy.System);
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(350, 10, Screen.width - 360, Screen.height - 20), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (string line in log)
                GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void Readout(GameplayAbilitySystem _Unit)
        {
            GUILayout.Label($"<b>{_Unit.Name}</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label($"Health {_Unit.GetCurrent(Health):0.#}/{_Unit.GetCurrent(MaxHealth):0.#}   Mana {_Unit.GetCurrent(Mana):0.#}/{_Unit.GetCurrent(MaxMana):0.#}");
            GUILayout.Label($"AttackPower {_Unit.GetCurrent(AttackPower):0.#}   Armor {_Unit.GetCurrent(Armor):0.#}");
            GUILayout.Label("Tags: " + string.Join(", ", _Unit.Tags.Snapshot().Select(e => e.Path)));
        }
        #endregion
    }
}
