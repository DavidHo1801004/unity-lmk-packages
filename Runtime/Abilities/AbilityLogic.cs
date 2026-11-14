using System;

using UnityEngine;

using LMK.Runtime.Attributes;
using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Why an activation was refused.
    /// </summary>
    [Flags]
    public enum AbilityFailureReason
    {
        None = 0,

        /// <summary> The unit has no avatar. </summary>
        NoAvatar = 1 << 0,

        /// <summary> An active ability or effect blocks one of its tags. </summary>
        Blocked = 1 << 1,

        /// <summary> The owner has one of its activation blocked tags. </summary>
        TagsBlocked = 1 << 2,

        /// <summary> The owner lacks one of its activation required tags. </summary>
        TagsMissing = 1 << 3,

        /// <summary> The triggering event's source or target fails its tag checks. </summary>
        SourceOrTargetTags = 1 << 4,

        /// <summary> A cooldown tag is on the owner. </summary>
        OnCooldown = 1 << 5,

        /// <summary> The cost would take an attribute below zero. </summary>
        CannotAffordCost = 1 << 6,

        /// <summary> The logic's own <see cref="AbilityLogic.CanActivate"/> refused. </summary>
        Logic = 1 << 7,
    }



    /// <summary>
    /// The reasons an activation was refused, and the tags the logic added to explain it.
    /// </summary>
    public sealed class ActivationFailure
    {
        public AbilityFailureReason Reasons { get; internal set; }

        public GameplayTagContainer Tags { get; } = new();

        public bool Has(AbilityFailureReason _Reason) => (Reasons & _Reason) != 0;

        public override string ToString() => Tags.IsEmpty ? Reasons.ToString() : $"{Reasons} ({Tags})";
    }



    /// <summary>
    /// What an ability does, inline-instanced on its <see cref="GameplayAbility"/>. The object on the
    /// asset is a template that never runs: it is cloned into each instance. Serialized fields are configuration;
    /// <c>[NonSerialized]</c> fields are the state of one instance and start at their defaults in every clone.
    /// </summary>
    [Serializable, CanReferenceInline]
    public abstract class AbilityLogic
    {
        /// <summary>
        /// Whether the ability may activate now, after the asset's own checks. Add tags to
        /// <paramref name="_FailureTags"/> to say why not. Read-only: it may run on the template.
        /// </summary>
        public virtual bool CanActivate(AbilityContext _Context, GameplayTagContainer _FailureTags) => true;

        /// <summary>
        /// The activation. Call <see cref="AbilityContext.Commit"/> at the point of no return, and
        /// <see cref="AbilityContext.End"/> when done; it may end before it returns, or much later.
        /// </summary>
        /// <param name="_Trigger"> The gameplay event that triggered it, if any. </param>
        public abstract void Activate(AbilityContext _Context, GameplayEventData? _Trigger);

        /// <summary>
        /// Once per activation, when it ends or is cancelled. Its tasks end right after.
        /// </summary>
        public virtual void OnEnd(AbilityContext _Context, bool _WasCancelled) { }

        /// <summary>
        /// Whether a cancel without force may end the activation.
        /// </summary>
        public virtual bool CanBeCancelled => true;

        public virtual void OnInputPressed(AbilityContext _Context) { }

        public virtual void OnInputReleased(AbilityContext _Context) { }

        /// <summary>
        /// When the instance is created at grant (per-actor instances only). UE's <c>OnGiveAbility</c>.
        /// </summary>
        public virtual void OnGranted(AbilityContext _Context) { }

        /// <summary>
        /// When the ability is removed (per-actor instances only). UE's <c>OnRemoveAbility</c>.
        /// </summary>
        public virtual void OnRemoved(AbilityContext _Context) { }

        /// <summary>
        /// When the unit's avatar changed (per-actor instances only).
        /// </summary>
        public virtual void OnAvatarSet(AbilityContext _Context) { }

        /// <summary>
        /// A clone for a new instance, or <see langword="null"/> to let the world's cloner make it. Override for hot
        /// paths; the clone must share nothing mutable with the template.
        /// </summary>
        protected internal virtual AbilityLogic Clone() => null;
    }



    /// <summary>
    /// Makes the logic of a new ability instance from the asset's template.
    /// </summary>
    public interface IAbilityLogicCloner
    {
        AbilityLogic Clone(GameplayAbility _Ability);
    }



    /// <summary>
    /// The default cloner: <see cref="UnityEngine.Object.Instantiate(UnityEngine.Object)"/> on the ability asset, which deep
    /// copies the <c>[SerializeReference]</c> graph, keeps references between its objects, shares references to Unity
    /// objects, and resets <c>[NonSerialized]</c> fields. The copied asset is destroyed; only its logic is kept.
    /// </summary>
    public sealed class InstantiateAbilityLogicCloner : IAbilityLogicCloner
    {
        public AbilityLogic Clone(GameplayAbility _Ability)
        {
            if (_Ability == null || _Ability.logic == null) return null;

            var own = _Ability.logic.Clone();
            if (own != null) return own;

            var copy = UnityEngine.Object.Instantiate(_Ability);
            var logic = copy.logic;

            if (Application.isPlaying) UnityEngine.Object.Destroy(copy);
            else UnityEngine.Object.DestroyImmediate(copy);

            return logic;
        }
    }
}
