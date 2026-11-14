using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Attributes;
using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// A temporary modifier on a captured attribute's value, for one execution only (UE's scoped modifier).
    /// </summary>
    [Serializable]
    public sealed class ScopedModifier
    {
        public AttributeCaptureDefinition capture;

        public ModifierOp op;

        [SerializeReference, InlineInstanced]
        public Magnitude magnitude = new ScalableFloatMagnitude();
    }



    /// <summary>
    /// An effect an execution may apply to the target, when it asks for it and the source's tags pass.
    /// </summary>
    [Serializable]
    public sealed class ConditionalEffect
    {
        public GameplayEffect effect;

        [Tooltip("Every one of these must be among the source's captured tags.")]
        public GameplayTagContainer requiredSourceTags = new();
    }



    /// <summary>
    /// One execution of an effect: a calculation, its scoped modifiers, and its conditional effects.
    /// </summary>
    [Serializable]
    public sealed class ExecutionDefinition
    {
        [SerializeReference, InlineInstanced]
        public EffectExecutionCalculation calculation;

        public List<ScopedModifier> calculationModifiers = new();

        public List<ConditionalEffect> conditionalEffects = new();
    }



    /// <summary>
    /// What an execution calculation reads: the spec, the two units, and the captured attributes with this
    /// execution's scoped modifiers applied.
    /// </summary>
    public readonly struct ExecutionParams
    {
        private readonly List<ScopedModifier> scopedModifiers;

        public GameplayEffectSpec Spec { get; }

        /// <summary>
        /// The unit that made the spec, or <see langword="null"/> if it has shut down.
        /// </summary>
        public GameplayAbilitySystem Source => Spec.Context.Instigator;

        public GameplayAbilitySystem Target { get; }

        internal ExecutionParams(GameplayEffectSpec _Spec, GameplayAbilitySystem _Target, List<ScopedModifier> _ScopedModifiers)
        {
            Spec = _Spec;
            Target = _Target;
            scopedModifiers = _ScopedModifiers;
        }

        /// <summary>
        /// A captured attribute's current value, with the scoped modifiers that name it. False if the spec didn't
        /// capture it, or its unit lacks it.
        /// </summary>
        public bool TryGetCapturedMagnitude(AttributeCaptureDefinition _Capture, out float _Value)
        {
            if (!Spec.TryGetCapturedValue(_Capture, AttributeCalculationType.Magnitude, 0, out _Value)) return false;
            if (scopedModifiers == null || scopedModifiers.Count == 0) return true;

            var aggregator = new Aggregator();
            int id = 0;
            foreach (var scoped in scopedModifiers)
            {
                if (scoped == null || !scoped.capture.Equals(_Capture)) continue;

                aggregator.Add(new Aggregator.Modifier
                {
                    Handle = new ModifierHandle(++id),
                    Op = scoped.op,
                    Magnitude = scoped.magnitude?.Calculate(Spec) ?? 0f,
                });
            }

            _Value = aggregator.Evaluate(_Value, out _);
            return true;
        }
    }



    /// <summary>
    /// What an execution calculation produces: modifiers to execute on the target, and whether its conditional effects
    /// apply.
    /// </summary>
    public sealed class ExecutionOutput
    {
        internal readonly List<(GameplayAttribute Attribute, ModifierOp Op, float Magnitude)> modifiers = new();

        internal bool TriggersConditionalEffects { get; private set; }

        internal bool CuesHandledManually { get; private set; }

        /// <summary>
        /// Execute a modifier on the target's base value, after the calculation returns.
        /// </summary>
        public void AddOutputModifier(GameplayAttribute _Attribute, ModifierOp _Op, float _Magnitude) => modifiers.Add((_Attribute, _Op, _Magnitude));

        /// <summary>
        /// Apply the execution's conditional effects whose source tags pass.
        /// </summary>
        public void TriggerConditionalEffects() => TriggersConditionalEffects = true;

        /// <summary>
        /// The calculation triggers the effect's cues itself.
        /// </summary>
        public void MarkCuesHandledManually() => CuesHandledManually = true;
    }



    /// <summary>
    /// Game code that turns captured attributes into modifiers (UE's <c>UGameplayEffectExecutionCalculation</c>), e.g. a
    /// damage formula. Stateless: one instance serves every execution. Declare the attributes it reads in
    /// <see cref="relevantAttributes"/>, so they are captured.
    /// </summary>
    [Serializable, CanReferenceInline]
    public abstract class EffectExecutionCalculation
    {
        public List<AttributeCaptureDefinition> relevantAttributes = new();

        public abstract void Execute(in ExecutionParams _Params, ExecutionOutput _Output);
    }
}
