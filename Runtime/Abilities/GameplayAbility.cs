using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Attributes;
using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// When an ability's logic is cloned.
    /// </summary>
    public enum InstancingPolicy
    {
        /// <summary> Once, at grant. State persists across activations; one activation at a time unless retriggered. </summary>
        InstancedPerActor,

        /// <summary> At every activation. Concurrent activations are allowed. </summary>
        InstancedPerExecution,
    }



    /// <summary>
    /// What activates an ability by itself.
    /// </summary>
    public enum AbilityTriggerSource
    {
        /// <summary> A gameplay event whose tag matches. </summary>
        GameplayEvent,

        /// <summary> The owner's count for the tag going from 0 to 1. </summary>
        OwnedTagAdded,

        /// <summary> Activates on 0 to 1, and cancels on 1 to 0. </summary>
        OwnedTagPresent,
    }



    /// <summary>
    /// Reserved for multiplayer. Ignored.
    /// </summary>
    public enum NetExecutionPolicy
    {
        LocalPredicted,
        LocalOnly,
        ServerInitiated,
        ServerOnly,
    }



    [Serializable]
    public struct AbilityTrigger
    {
        public GameplayTag tag;

        public AbilityTriggerSource source;

        public AbilityTrigger(GameplayTag _Tag, AbilityTriggerSource _Source)
        {
            tag = _Tag;
            source = _Source;
        }
    }



    /// <summary>
    /// A gameplay ability (UE's <c>UGameplayAbility</c> and its class default object): its tags, cost, cooldown and
    /// policies, and one inline-instanced <see cref="AbilityLogic"/> where the custom behaviour lives. The
    /// asset is a template: its logic is cloned per grant or per activation, and never written at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "LMK/Ability System/Gameplay Ability", fileName = "GameplayAbility")]
    public sealed class GameplayAbility : ScriptableObject
    {
        [Header("Tags")]
        [Tooltip("What this ability is.")]
        public GameplayTagContainer abilityTags = new();

        [Tooltip("On activation, cancel the owner's active abilities that have any of these tags. Never this one.")]
        public GameplayTagContainer cancelAbilitiesWithTag = new();

        [Tooltip("While active, abilities with any of these tags can't activate.")]
        public GameplayTagContainer blockAbilitiesWithTag = new();

        [Tooltip("Added to the owner while active.")]
        public GameplayTagContainer activationOwnedTags = new();

        [Tooltip("The owner must have every one of these to activate.")]
        public GameplayTagContainer activationRequiredTags = new();

        [Tooltip("The owner must have none of these to activate.")]
        public GameplayTagContainer activationBlockedTags = new();

        [Tooltip("Checked on the source of a triggering event, when there is one.")]
        public GameplayTagContainer sourceRequiredTags = new();

        public GameplayTagContainer sourceBlockedTags = new();

        [Tooltip("Checked on the target of a triggering event, when there is one.")]
        public GameplayTagContainer targetRequiredTags = new();

        public GameplayTagContainer targetBlockedTags = new();

        [Header("Cost and cooldown")]
        [Tooltip("An instant effect applied on commit. Its AddBase modifiers must be affordable.")]
        public GameplayEffect costEffect;

        [Tooltip("A duration effect applied on commit. The tags it grants are the cooldown tags.")]
        public GameplayEffect cooldownEffect;

        [Header("Policies")]
        public InstancingPolicy instancingPolicy;

        [Tooltip("InstancedPerActor: activating it again while active ends the running activation and starts a new one.")]
        public bool retriggerInstancedAbility;

        public List<AbilityTrigger> triggers = new();

        [Tooltip("Reserved for multiplayer. Ignored.")]
        public NetExecutionPolicy netExecutionPolicy;

        [Header("Logic")]
        [SerializeReference, InlineInstanced]
        public AbilityLogic logic;

        /// <summary>
        /// The tags the cooldown effect grants.
        /// </summary>
        public GameplayTagContainer GetCooldownTags() => cooldownEffect != null ? cooldownEffect.GetGrantedTags() : new GameplayTagContainer();
    }
}
