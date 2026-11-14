using System;
using System.Linq;

using UnityEngine;

using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Attributes;
using LMK.Runtime.GameplayTags;

using static LMK.Samples.AbilitySystem.Walkthroughs.WalkthroughAttributes;

namespace LMK.Samples.AbilitySystem.Walkthroughs
{
    /// <summary>
    /// GA_Fireball's logic: aim, cast, commit, then launch a projectile that carries the specs.
    /// </summary>
    [Serializable]
    public sealed class FireballLogic : AbilityLogic
    {
        public float castTime = 0.4f;

        [Tooltip("The animator state of the cast, played on the avatar if it has an animator.")]
        public string castAnimation = "Cast";

        public ScalableFloat baseDamage = new(20f);

        public GameplayEffect damageEffect;

        public GameplayEffect burnEffect;

        public Projectile projectilePrefab;

        [SerializeReference, InlineInstanced]
        public TargetingStrategy targeting = new NearestEnemyTargeting();

        public override async void Activate(AbilityContext _Context, GameplayEventData? _Trigger)
        {
            var aim = await _Context.WaitTargetData(targeting);       // never resumes if the ability ends first
            if (aim.Cancelled || aim.Targets.Count == 0)
            {
                _Context.Cancel();
                return;
            }

            // The animation, or the cast time when the avatar has no animator.
            if (!await _Context.PlayAnimation(castAnimation, castTime))
                await _Context.Delay(castTime);

            if (!_Context.Commit())                                     // mana or cooldown changed during the cast
            {
                _Context.Cancel();
                return;
            }

            var damage = _Context.MakeOutgoingSpec(damageEffect);       // snapshots the caster's AttackPower now
            damage.SetByCaller(WalkthroughTags.DataDamage, baseDamage.At(_Context.Level));
            var burn = _Context.MakeOutgoingSpec(burnEffect);

            Projectile.Launch(projectilePrefab, _Context.ActorInfo.AvatarGameObject, aim.Targets[0], damage, burn);
            _Context.End();                                             // the projectile outlives the ability
        }
    }



    /// <summary>
    /// Picks the nearest other unit with an avatar in range, at once.
    /// </summary>
    [Serializable]
    public sealed class NearestEnemyTargeting : TargetingStrategy
    {
        public float range = 25f;

        public override void Begin(AbilityContext _Context, Action<TargetData> _Confirm, Action _Cancel)
        {
            var self = _Context.ActorInfo.AvatarGameObject;
            if (self == null)
            {
                _Cancel();
                return;
            }

            var target = _Context.Owner.World.Systems
                .Where(e => e != _Context.Owner && !e.IsShuttingDown && e.ActorInfo.AvatarGameObject != null)
                .Select(e => (Unit: e, Distance: Vector3.Distance(self.transform.position, e.ActorInfo.AvatarGameObject.transform.position)))
                .Where(e => e.Distance <= range)
                .OrderBy(e => e.Distance)
                .Select(e => e.Unit)
                .FirstOrDefault();

            if (target == null)
            {
                _Cancel();
                return;
            }

            var data = new TargetData { Direction = (target.ActorInfo.AvatarGameObject.transform.position - self.transform.position).normalized };
            data.Targets.Add(target);
            _Confirm(data);
        }
    }



    /// <summary>
    /// GA_Death's logic: apply GE_Dead, and stay dead. The sample keeps the body so the walkthrough can
    /// show its state; a game would play a death animation and destroy the avatar.
    /// </summary>
    [Serializable]
    public sealed class DeathLogic : AbilityLogic
    {
        public GameplayEffect deadEffect;

        public override void Activate(AbilityContext _Context, GameplayEventData? _Trigger)
        {
            _Context.ApplyToOwner(_Context.MakeOutgoingSpec(deadEffect));

            var avatar = _Context.ActorInfo.AvatarGameObject;
            if (avatar != null) avatar.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        }

        public override bool CanBeCancelled => false;
    }



    /// <summary>
    /// Stays active until cancelled: GA_StunReaction and the GA_Thorns passive.
    /// </summary>
    [Serializable]
    public sealed class HoldLogic : AbilityLogic
    {
        public override void Activate(AbilityContext _Context, GameplayEventData? _Trigger) { }
    }



    /// <summary>
    /// GE_Fireball_Damage's execution: (SetByCaller Data.Damage + source AttackPower) × 100 / (100 + target Armor),
    /// as incoming damage.
    /// </summary>
    [Serializable]
    public sealed class DamageExecution : EffectExecutionCalculation
    {
        private static readonly AttributeCaptureDefinition SourceAttackPower = new(AttackPower, CaptureSource.Source, true);
        private static readonly AttributeCaptureDefinition TargetArmor = new(Armor, CaptureSource.Target, false);

        public DamageExecution()
        {
            relevantAttributes.Clear();
            relevantAttributes.Add(SourceAttackPower);
            relevantAttributes.Add(TargetArmor);
        }

        public override void Execute(in ExecutionParams _Params, ExecutionOutput _Output)
        {
            _Params.TryGetCapturedMagnitude(SourceAttackPower, out float attackPower);
            _Params.TryGetCapturedMagnitude(TargetArmor, out float armor);
            _Params.Spec.TryGetSetByCaller(WalkthroughTags.DataDamage, out float baseDamage);

            float final = Mathf.Max(0f, (baseDamage + attackPower) * 100f / (100f + armor));
            _Output.AddOutputModifier(IncomingDamage, ModifierOp.AddBase, final);
        }
    }
}
