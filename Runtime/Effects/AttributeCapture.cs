using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Attributes;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Whose attribute a capture reads.
    /// </summary>
    public enum CaptureSource
    {
        /// <summary> The unit that made the spec. </summary>
        Source,

        /// <summary> The unit the spec is applied to. </summary>
        Target,
    }



    /// <summary>
    /// Which value of a captured attribute counts.
    /// </summary>
    public enum AttributeCalculationType
    {
        /// <summary> The current value. </summary>
        Magnitude,

        /// <summary> The base value. </summary>
        Base,

        /// <summary> The current value minus the base value. </summary>
        Bonus,

        /// <summary> The base with the modifiers of the evaluation channels up to a given one. </summary>
        EvaluatedUpToChannel,
    }



    /// <summary>
    /// An attribute an effect reads: which, whose, and whether as a snapshot. Source snapshots are taken
    /// when the spec is made, target snapshots when it is applied. A live capture follows the attribute: when it
    /// changes, the magnitudes that read it are calculated again. If its unit shuts down, it keeps its last value.
    /// </summary>
    [Serializable]
    public struct AttributeCaptureDefinition : IEquatable<AttributeCaptureDefinition>
    {
        public GameplayAttribute attribute;

        public CaptureSource source;

        [Tooltip("Read the value once (source: when the spec is made; target: when it is applied), or follow it while the effect is active.")]
        public bool snapshot;

        public AttributeCaptureDefinition(GameplayAttribute _Attribute, CaptureSource _Source, bool _Snapshot)
        {
            attribute = _Attribute;
            source = _Source;
            snapshot = _Snapshot;
        }

        public bool Equals(AttributeCaptureDefinition _Other) => attribute == _Other.attribute && source == _Other.source && snapshot == _Other.snapshot;

        public override bool Equals(object _Obj) => _Obj is AttributeCaptureDefinition other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(attribute, source, snapshot);

        public override string ToString() => $"{source} {attribute}{(snapshot ? " (snapshot)" : " (live)")}";
    }



    /// <summary>
    /// One captured attribute of a spec: the unit it is read from, and the values last read.
    /// </summary>
    internal sealed class CapturedAttribute
    {
        public readonly AttributeCaptureDefinition Definition;
        public GameplayAbilitySystem Unit;
        public bool HasValue;

        private readonly Dictionary<(AttributeCalculationType, int), float> values = new();

        public CapturedAttribute(AttributeCaptureDefinition _Definition, GameplayAbilitySystem _Unit)
        {
            Definition = _Definition;
            Unit = _Unit;
        }

        public bool IsLive => !Definition.snapshot && Unit != null && !Unit.IsShuttingDown;

        /// <summary>
        /// Read every value type now, as a snapshot does.
        /// </summary>
        public void Snapshot(IEnumerable<int> _Channels)
        {
            if (Unit == null) return;

            HasValue = Unit.Attributes.Has(Definition.attribute);
            if (!HasValue) return;

            foreach (var type in new[] { AttributeCalculationType.Magnitude, AttributeCalculationType.Base, AttributeCalculationType.Bonus })
                Read(type, 0);
            foreach (int channel in _Channels)
                Read(AttributeCalculationType.EvaluatedUpToChannel, channel);
        }

        /// <summary>
        /// The value: read now for a live capture of a running unit, else the value kept from the last read.
        /// </summary>
        public bool TryGet(AttributeCalculationType _Type, int _Channel, out float _Value)
        {
            if (IsLive && Read(_Type, _Channel))
            {
                _Value = values[(_Type, Key(_Type, _Channel))];
                return true;
            }

            return values.TryGetValue((_Type, Key(_Type, _Channel)), out _Value);
        }

        private bool Read(AttributeCalculationType _Type, int _Channel)
        {
            if (!Unit.Attributes.TryEvaluate(Definition.attribute, _Type, _Channel, out float value)) return false;

            values[(_Type, Key(_Type, _Channel))] = value;
            HasValue = true;
            return true;
        }

        private static int Key(AttributeCalculationType _Type, int _Channel) => _Type == AttributeCalculationType.EvaluatedUpToChannel ? _Channel : 0;

        public CapturedAttribute Copy()
        {
            var copy = new CapturedAttribute(Definition, Unit) { HasValue = HasValue };
            foreach (var (key, value) in values)
                copy.values[key] = value;

            return copy;
        }
    }



    /// <summary>
    /// <c>coefficient × (preMultiplyAdd + captured) + postMultiplyAdd</c>, where <c>captured</c> is a value of a source
    /// or target attribute.
    /// </summary>
    [Serializable]
    public sealed class AttributeBasedMagnitude : Magnitude
    {
        public AttributeCaptureDefinition capture;

        public AttributeCalculationType calculationType;

        [Tooltip("EvaluatedUpToChannel only: the last channel included.")]
        public int finalChannel;

        public float coefficient = 1f;

        public float preMultiplyAdd;

        public float postMultiplyAdd;

        public AttributeBasedMagnitude() { }

        public AttributeBasedMagnitude(AttributeCaptureDefinition _Capture, float _Coefficient = 1f, float _PreMultiplyAdd = 0f, float _PostMultiplyAdd = 0f,
                                       AttributeCalculationType _Type = AttributeCalculationType.Magnitude)
        {
            capture = _Capture;
            coefficient = _Coefficient;
            preMultiplyAdd = _PreMultiplyAdd;
            postMultiplyAdd = _PostMultiplyAdd;
            calculationType = _Type;
        }

        public override float Calculate(GameplayEffectSpec _Spec)
        {
            float captured = _Spec != null && _Spec.TryGetCapturedValue(capture, calculationType, finalChannel, out float value) ? value : 0f;
            return coefficient * (preMultiplyAdd + captured) + postMultiplyAdd;
        }

        public override IEnumerable<AttributeCaptureDefinition> GetCaptures()
        {
            yield return capture;
        }

        public override IEnumerable<int> GetCaptureChannels(AttributeCaptureDefinition _Capture)
        {
            if (calculationType == AttributeCalculationType.EvaluatedUpToChannel && _Capture.Equals(capture))
                yield return finalChannel;
        }
    }



    /// <summary>
    /// A magnitude computed by game code (UE's <c>UGameplayModMagnitudeCalculation</c>). Stateless: one instance serves
    /// every application. Declare the attributes it reads in <see cref="relevantAttributes"/>, so they are captured.
    /// </summary>
    [Serializable, CanReferenceInline]
    public abstract class ModMagnitudeCalculation
    {
        public List<AttributeCaptureDefinition> relevantAttributes = new();

        public abstract float Calculate(GameplayEffectSpec _Spec);

        /// <summary>
        /// The current value of a captured attribute, if the spec captured it.
        /// </summary>
        protected static bool TryGetCapturedMagnitude(GameplayEffectSpec _Spec, AttributeCaptureDefinition _Capture, out float _Value)
            => _Spec.TryGetCapturedValue(_Capture, AttributeCalculationType.Magnitude, 0, out _Value);
    }



    /// <summary>
    /// <c>coefficient × (preMultiplyAdd + calculation) + postMultiplyAdd</c>, with a custom calculation.
    /// </summary>
    [Serializable]
    public sealed class CustomCalculationMagnitude : Magnitude
    {
        [SerializeReference, InlineInstanced]
        public ModMagnitudeCalculation calculation;

        public float coefficient = 1f;

        public float preMultiplyAdd;

        public float postMultiplyAdd;

        public override float Calculate(GameplayEffectSpec _Spec)
            => coefficient * (preMultiplyAdd + (calculation?.Calculate(_Spec) ?? 0f)) + postMultiplyAdd;

        public override IEnumerable<AttributeCaptureDefinition> GetCaptures()
            => calculation?.relevantAttributes ?? (IEnumerable<AttributeCaptureDefinition>)Array.Empty<AttributeCaptureDefinition>();
    }
}
