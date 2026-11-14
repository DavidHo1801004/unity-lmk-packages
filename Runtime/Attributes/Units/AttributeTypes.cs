using System;

using UnityEngine;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// How a modifier changes an attribute: the six ops of UE 5.4. On a duration or infinite effect they feed the
    /// aggregator; on an execution they change the base value.
    /// </summary>
    public enum ModifierOp : byte
    {
        /// <summary> Added to the base before multiplying. </summary>
        AddBase,

        /// <summary> Multiplies, summed with the other multipliers: 1 + Σ(m − 1). </summary>
        MultiplyAdditive,

        /// <summary> Divides, summed with the other divisors: 1 + Σ(m − 1). A sum of zero counts as 1. </summary>
        DivideAdditive,

        /// <summary> Multiplies the result of the other ops, one after the other. </summary>
        MultiplyCompound,

        /// <summary> Added last, after every multiplier. </summary>
        AddFinal,

        /// <summary> Replaces the channel's result. The first applied wins. </summary>
        Override,
    }



    /// <summary>
    /// An attribute's current value changed.
    /// </summary>
    public readonly struct AttributeChange
    {
        public readonly GameplayAttribute Attribute;
        public readonly float OldValue;
        public readonly float NewValue;

        /// <summary>
        /// The effect application that caused the change, or <see langword="null"/> (a direct base write, a bound).
        /// </summary>
        public readonly GameplayEffectSpec Cause;

        public AttributeChange(GameplayAttribute _Attribute, float _OldValue, float _NewValue, GameplayEffectSpec _Cause = null)
        {
            Attribute = _Attribute;
            OldValue = _OldValue;
            NewValue = _NewValue;
            Cause = _Cause;
        }

        public override string ToString() => $"{Attribute}: {OldValue} → {NewValue}";
    }



    /// <summary>
    /// One modifier being executed on a base value, as attribute behaviours see it.
    /// </summary>
    public struct ModCallbackData
    {
        public GameplayAttribute Attribute;
        public ModifierOp Op;

        /// <summary>
        /// The magnitude about to be applied. <see cref="AttributeBehaviour.PreEffectExecute"/> may change it.
        /// </summary>
        public float Magnitude;

        /// <summary>
        /// The effect application being executed, or <see langword="null"/>.
        /// </summary>
        public GameplayEffectSpec Spec;

        public ModCallbackData(GameplayAttribute _Attribute, ModifierOp _Op, float _Magnitude, GameplayEffectSpec _Spec = null)
        {
            Attribute = _Attribute;
            Op = _Op;
            Magnitude = _Magnitude;
            Spec = _Spec;
        }
    }



    /// <summary>
    /// A value scaled by level: <c>value × curve(level)</c>, or just <c>value</c> without a curve.
    /// </summary>
    [Serializable]
    public struct ScalableFloat
    {
        [SerializeField] private float value;

        [SerializeField, Tooltip("Multiplies the value by the curve at the level. Leave it empty for a constant value.")]
        private AnimationCurve curve;

        public float Value => value;

        public AnimationCurve Curve => curve;

        public ScalableFloat(float _Value, AnimationCurve _Curve = null)
        {
            value = _Value;
            curve = _Curve;
        }

        /// <summary>
        /// The value at <paramref name="_Level"/>.
        /// </summary>
        public float At(float _Level)
            => curve != null && curve.length > 0 ? value * curve.Evaluate(_Level) : value;

        public static implicit operator ScalableFloat(float _Value) => new(_Value);
    }
}
