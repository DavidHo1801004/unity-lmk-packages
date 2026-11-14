using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Attributes;
using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Tag requirements on the target: checked once on application, while active (the effect is
    /// inhibited while they fail), and for removal (the effect is removed when they match). Re-checked whenever the
    /// target's tags change.
    /// </summary>
    [Serializable]
    public sealed class TargetTagRequirementsComponent : GameplayEffectComponent
    {
        [Tooltip("Checked once: the application fails if they don't pass.")]
        public TagRequirements application = new();

        [Tooltip("While active: the effect is inhibited while they fail.")]
        public TagRequirements ongoing = new();

        [Tooltip("While active: the effect is removed when they pass. Empty: never.")]
        public TagRequirements removal = new();

        public override bool CanApply(GameplayAbilitySystem _Target, GameplayEffectSpec _Spec)
        {
            if (application != null && !application.RequirementsMet(_Target.Tags)) return false;
            return !IsRemovalMet(_Target);
        }

        public override bool OnActiveAdded(GameplayAbilitySystem _Target, ActiveGameplayEffect _Effect) => IsOngoingMet(_Target);

        internal bool IsOngoingMet(GameplayAbilitySystem _Target) => ongoing == null || ongoing.RequirementsMet(_Target.Tags);

        internal bool IsRemovalMet(GameplayAbilitySystem _Target) => removal != null && !removal.IsEmpty && removal.RequirementsMet(_Target.Tags);

        internal bool NeedsRechecks => (ongoing != null && !ongoing.IsEmpty) || (removal != null && !removal.IsEmpty);
    }



    /// <summary>
    /// While active and not inhibited, abilities with any of these tags (or their descendants) can't activate.
    /// </summary>
    [Serializable]
    public sealed class BlockAbilityTagsComponent : GameplayEffectComponent
    {
        public GameplayTagContainer tags = new();
    }



    /// <summary>
    /// What happens to an ability an effect granted when the effect ends or is inhibited.
    /// </summary>
    public enum GrantedAbilityRemovalPolicy
    {
        /// <summary> Cancel it if active, and remove it now. </summary>
        CancelAbilityImmediately,

        /// <summary> Let a running activation finish, then remove it. </summary>
        RemoveAbilityOnEnd,

        /// <summary> Keep it granted. </summary>
        DoNothing,
    }



    [Serializable]
    public sealed class GrantedAbilityConfig
    {
        public GameplayAbility ability;

        [Tooltip("Scaled by the effect's level.")]
        public ScalableFloat level = 1f;

        [Tooltip("-1 for no input.")]
        public int inputId = -1;

        public GrantedAbilityRemovalPolicy removalPolicy;
    }



    /// <summary>
    /// Grants abilities while the effect is active and not inhibited. Each ability's removal policy
    /// decides what happens when the effect ends or is inhibited.
    /// </summary>
    [Serializable]
    public sealed class AbilitiesComponent : GameplayEffectComponent
    {
        public List<GrantedAbilityConfig> abilities = new();
    }



    /// <summary>
    /// While active and not inhibited, incoming effects that match any query are blocked. Effects already active are
    /// untouched.
    /// </summary>
    [Serializable]
    public sealed class ImmunityComponent : GameplayEffectComponent
    {
        public List<GameplayEffectQuery> queries = new();

        internal bool Blocks(GameplayEffectSpec _Spec)
        {
            foreach (var query in queries)
                if (query != null && query.Matches(_Spec))
                    return true;

            return false;
        }
    }



    /// <summary>
    /// On application, removes the target's active effects that match any query, as premature removals.
    /// </summary>
    [Serializable]
    public sealed class RemoveOtherComponent : GameplayEffectComponent
    {
        public List<GameplayEffectQuery> queries = new();

        public override void OnApplied(GameplayAbilitySystem _Target, GameplayEffectSpec _Spec)
        {
            foreach (var query in queries)
                if (query != null)
                    _Target.RemoveActiveEffectsExceptCurrent(query);
        }
    }



    /// <summary>
    /// The application succeeds with a chance, rolled against the world's random source.
    /// </summary>
    [Serializable]
    public sealed class ChanceToApplyComponent : GameplayEffectComponent
    {
        [Tooltip("0 to 1, scaled by level.")]
        public ScalableFloat chance = 1f;

        public override bool CanApply(GameplayAbilitySystem _Target, GameplayEffectSpec _Spec)
        {
            float value = chance.At(_Spec.Level);
            if (value >= 1f) return true;
            if (value <= 0f) return false;

            return _Target.World.Random.NextDouble() < value;
        }
    }



    /// <summary>
    /// A custom application requirement (UE's <c>UGameplayEffectCustomApplicationRequirement</c>). Stateless.
    /// </summary>
    [Serializable, CanReferenceInline]
    public abstract class CustomApplicationRequirement
    {
        public abstract bool CanApply(GameplayAbilitySystem _Target, GameplayEffectSpec _Spec);
    }



    /// <summary>
    /// The application succeeds only if every custom requirement passes.
    /// </summary>
    [Serializable]
    public sealed class CustomCanApplyComponent : GameplayEffectComponent
    {
        [SerializeReference, InlineInstanced]
        public List<CustomApplicationRequirement> requirements = new();

        public override bool CanApply(GameplayAbilitySystem _Target, GameplayEffectSpec _Spec)
        {
            foreach (var requirement in requirements)
                if (requirement != null && !requirement.CanApply(_Target, _Spec))
                    return false;

            return true;
        }
    }
}
