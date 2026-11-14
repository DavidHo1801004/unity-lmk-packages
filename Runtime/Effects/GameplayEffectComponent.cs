using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Attributes;
using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Why and how an active effect ended.
    /// </summary>
    public readonly struct RemovalInfo
    {
        /// <summary>
        /// Removed before its duration ran out: by a call, a query or a requirement.
        /// </summary>
        public readonly bool Premature;

        /// <summary>
        /// Its stack count when it ended.
        /// </summary>
        public readonly int StackCount;

        public RemovalInfo(bool _Premature, int _StackCount)
        {
            Premature = _Premature;
            StackCount = _StackCount;
        }
    }



    /// <summary>
    /// A piece of an effect's behaviour (UE's <c>UGameplayEffectComponent</c>), inline-instanced on the effect and
    /// stateless: one instance serves every application.
    /// </summary>
    [Serializable, CanReferenceInline]
    public abstract class GameplayEffectComponent
    {
        /// <summary>
        /// Whether the spec may apply to the target. Any false refuses the application.
        /// </summary>
        public virtual bool CanApply(GameplayAbilitySystem _Target, GameplayEffectSpec _Spec) => true;

        /// <summary>
        /// After a successful application, whatever the duration policy.
        /// </summary>
        public virtual void OnApplied(GameplayAbilitySystem _Target, GameplayEffectSpec _Spec) { }

        /// <summary>
        /// After an execution: an instant application, or one period of a periodic effect.
        /// </summary>
        public virtual void OnExecuted(GameplayAbilitySystem _Target, GameplayEffectSpec _Spec) { }

        /// <summary>
        /// When a duration or infinite effect becomes active. False starts it inhibited.
        /// </summary>
        public virtual bool OnActiveAdded(GameplayAbilitySystem _Target, ActiveGameplayEffect _Effect) => true;

        /// <summary>
        /// When an active effect becomes inhibited or stops being so.
        /// </summary>
        public virtual void OnInhibitionChanged(GameplayAbilitySystem _Target, ActiveGameplayEffect _Effect, bool _Inhibited) { }

        /// <summary>
        /// When an active effect ends.
        /// </summary>
        public virtual void OnRemoved(GameplayAbilitySystem _Target, ActiveGameplayEffect _Effect, in RemovalInfo _Info) { }
    }



    /// <summary>
    /// Tags the effect itself has, used by queries, immunity and removal. Not granted to the target.
    /// </summary>
    [Serializable]
    public sealed class AssetTagsComponent : GameplayEffectComponent
    {
        public GameplayTagContainer tags = new();
    }



    /// <summary>
    /// Tags granted to the target while the effect is active and not inhibited.
    /// </summary>
    [Serializable]
    public sealed class TargetTagsComponent : GameplayEffectComponent
    {
        public GameplayTagContainer tags = new();
    }



    /// <summary>
    /// Effects applied to the target on application, and when the effect ends: always, on normal expiry, or on
    /// premature removal. They share the triggering spec's level and context.
    /// </summary>
    [Serializable]
    public sealed class AdditionalEffectsComponent : GameplayEffectComponent
    {
        public List<GameplayEffect> onApplication = new();

        [Tooltip("When the effect ends, however it ends.")]
        public List<GameplayEffect> onCompleteAlways = new();

        [Tooltip("When the effect's duration runs out.")]
        public List<GameplayEffect> onCompleteNormal = new();

        [Tooltip("When the effect is removed before its duration runs out.")]
        public List<GameplayEffect> onCompletePrematurely = new();

        public override void OnApplied(GameplayAbilitySystem _Target, GameplayEffectSpec _Spec) => Apply(_Target, onApplication, _Spec);

        public override void OnRemoved(GameplayAbilitySystem _Target, ActiveGameplayEffect _Effect, in RemovalInfo _Info)
        {
            Apply(_Target, onCompleteAlways, _Effect.Spec);
            Apply(_Target, _Info.Premature ? onCompletePrematurely : onCompleteNormal, _Effect.Spec);
        }

        private static void Apply(GameplayAbilitySystem _Target, List<GameplayEffect> _Effects, GameplayEffectSpec _Parent)
        {
            if (_Effects == null) return;

            foreach (var effect in _Effects)
                if (effect != null)
                    _Target.ApplySpecToSelf(GameplayEffectSpec.FromParent(effect, _Parent));
        }

        /// <summary>
        /// Every effect it may apply, for validation.
        /// </summary>
        public IEnumerable<GameplayEffect> AllEffects()
        {
            foreach (var list in new[] { onApplication, onCompleteAlways, onCompleteNormal, onCompletePrematurely })
                if (list != null)
                    foreach (var effect in list)
                        if (effect != null)
                            yield return effect;
        }
    }
}
