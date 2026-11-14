using System;
using System.Collections.Generic;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// An active effect, identified by a handle issued by its world. Handles are never reused: a stale handle finds
    /// nothing, and every call with it returns false or <see langword="null"/>.
    /// </summary>
    public readonly struct ActiveEffectHandle : IEquatable<ActiveEffectHandle>
    {
        public static readonly ActiveEffectHandle Invalid = default;

        public readonly int Id;

        public bool IsValid => Id > 0;

        internal ActiveEffectHandle(int _Id)
        {
            Id = _Id;
        }

        public bool Equals(ActiveEffectHandle _Other) => Id == _Other.Id;

        public override bool Equals(object _Obj) => _Obj is ActiveEffectHandle other && Equals(other);

        public override int GetHashCode() => Id;

        public static bool operator ==(ActiveEffectHandle _A, ActiveEffectHandle _B) => _A.Id == _B.Id;

        public static bool operator !=(ActiveEffectHandle _A, ActiveEffectHandle _B) => _A.Id != _B.Id;

        public override string ToString() => IsValid ? $"Effect {Id}" : "Invalid";
    }



    /// <summary>
    /// A duration or infinite effect on a unit (UE's <c>FActiveGameplayEffect</c>). Plain data: the spec, timing and
    /// stack count. Read it through <see cref="GameplayAbilitySystem.GetActiveEffect"/>; only the unit changes it.
    /// </summary>
    public sealed class ActiveGameplayEffect
    {
        // The aggregator modifier of each modifier, by its index in the effect.
        internal readonly List<(int Index, ModifierHandle Handle)> modifierHandles = new();
        internal GameplayTagContainer grantedTags = new();

        // Undo the live capture links.
        internal readonly List<Action> unlinks = new();

        // The abilities the effect granted, while it contributes.
        internal readonly List<(AbilitySpecHandle Handle, GrantedAbilityRemovalPolicy Policy)> grantedAbilities = new();

        public ActiveEffectHandle Handle { get; }

        /// <summary>
        /// The unit the effect is on.
        /// </summary>
        public GameplayAbilitySystem Owner { get; }

        public GameplayEffectSpec Spec { get; internal set; }

        public GameplayEffect Def => Spec.Def;

        public int StackCount => Spec.StackCount;

        /// <summary>
        /// World time when the effect started, or its duration last restarted.
        /// </summary>
        public double StartTime { get; internal set; }

        /// <summary>
        /// World time when the duration runs out. Infinite for an infinite effect.
        /// </summary>
        public double EndTime { get; internal set; }

        /// <summary>
        /// World time the period schedule counts from.
        /// </summary>
        public double PeriodStart { get; internal set; }

        /// <summary>
        /// Periods executed since <see cref="PeriodStart"/>.
        /// </summary>
        public int PeriodsDone { get; internal set; }

        /// <summary>
        /// Whether the effect contributes nothing for now: no modifiers, tags or periods. Its duration still runs.
        /// </summary>
        public bool IsInhibited { get; internal set; }

        /// <summary>
        /// Whether the effect was removed. Its handle no longer finds it.
        /// </summary>
        public bool IsRemoved { get; internal set; }

        public bool HasDuration => Def.DurationPolicy == DurationPolicy.HasDuration;

        public bool IsPeriodic => Spec.Period > 0;

        internal ActiveGameplayEffect(ActiveEffectHandle _Handle, GameplayAbilitySystem _Owner, GameplayEffectSpec _Spec, double _Now)
        {
            Handle = _Handle;
            Owner = _Owner;
            Spec = _Spec;
            StartTime = PeriodStart = _Now;
            EndTime = HasDuration ? _Now + _Spec.Duration : double.PositiveInfinity;
        }

        /// <summary>
        /// Seconds left at world time <paramref name="_Now"/>. Infinite for an infinite effect.
        /// </summary>
        public double TimeRemaining(double _Now) => HasDuration ? Math.Max(0, EndTime - _Now) : double.PositiveInfinity;

        /// <summary>
        /// Seconds left now. Infinite for an infinite effect.
        /// </summary>
        public double TimeRemaining() => TimeRemaining(Owner.World.Time);

        public override string ToString() => $"{Handle}: {Spec}";
    }
}
