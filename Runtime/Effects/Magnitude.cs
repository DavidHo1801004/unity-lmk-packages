using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Attributes;
using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// How an effect computes a number, such as a modifier's magnitude or a duration. Inline-instanced on
    /// the effect, and stateless: one instance serves every application.
    /// </summary>
    [Serializable, CanReferenceInline]
    public abstract class Magnitude
    {
        /// <summary>
        /// The value for <paramref name="_Spec"/>, at its level.
        /// </summary>
        public abstract float Calculate(GameplayEffectSpec _Spec);

        /// <summary>
        /// The attributes the magnitude reads, so a spec captures them.
        /// </summary>
        public virtual IEnumerable<AttributeCaptureDefinition> GetCaptures() => Array.Empty<AttributeCaptureDefinition>();

        /// <summary>
        /// The evaluation channels a snapshot of <paramref name="_Capture"/> must keep, for values read up to a channel.
        /// </summary>
        public virtual IEnumerable<int> GetCaptureChannels(AttributeCaptureDefinition _Capture) => Array.Empty<int>();
    }



    /// <summary>
    /// <c>value × curve(level)</c>, or a constant without a curve.
    /// </summary>
    [Serializable]
    public sealed class ScalableFloatMagnitude : Magnitude
    {
        public ScalableFloat value;

        public ScalableFloatMagnitude() { }

        public ScalableFloatMagnitude(ScalableFloat _Value) => value = _Value;

        /// <summary>
        /// Whether the value is the same at every level.
        /// </summary>
        public bool IsConstant => value.Curve == null || value.Curve.length == 0;

        public override float Calculate(GameplayEffectSpec _Spec) => value.At(_Spec?.Level ?? 1f);
    }



    /// <summary>
    /// The value the caller set on the spec under <see cref="dataTag"/> (<see cref="GameplayEffectSpec.SetByCaller"/>).
    /// Missing: 0, with a warning.
    /// </summary>
    [Serializable]
    public sealed class SetByCallerMagnitude : Magnitude
    {
        [Tooltip("The tag the caller sets the value under, e.g. Data.Damage.")]
        public GameplayTag dataTag;

        public SetByCallerMagnitude() { }

        public SetByCallerMagnitude(GameplayTag _DataTag) => dataTag = _DataTag;

        public override float Calculate(GameplayEffectSpec _Spec)
        {
            if (_Spec != null && _Spec.TryGetSetByCaller(dataTag, out float value)) return value;

            Debug.LogWarning($"Effect \"{(_Spec?.Def != null ? _Spec.Def.name : "?")}\" has no SetByCaller value for \"{dataTag}\". The magnitude is 0.");
            return 0f;
        }
    }
}
