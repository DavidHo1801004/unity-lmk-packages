using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Where an effect came from (UE's <c>FGameplayEffectContext</c>). Subclass it to carry game data; override
    /// <see cref="Duplicate"/> then.
    /// </summary>
    public class GameplayEffectContext
    {
        private GameplayAbilitySystem instigator;

        /// <summary>
        /// The instigator even after it shut down: stacks by source stay apart.
        /// </summary>
        internal GameplayAbilitySystem InstigatorEvenIfShutDown => instigator;

        /// <summary>
        /// The unit that caused the effect, or <see langword="null"/> if none, or if it has shut down since.
        /// </summary>
        public GameplayAbilitySystem Instigator
        {
            get => instigator != null && !instigator.IsShuttingDown ? instigator : null;
            set => instigator = value;
        }

        /// <summary>
        /// The physical causer, such as a projectile or a weapon.
        /// </summary>
        public UnityEngine.Object EffectCauser { get; set; }

        /// <summary>
        /// The object the effect came from, such as an item asset.
        /// </summary>
        public UnityEngine.Object SourceObject { get; set; }

        public int AbilityLevel { get; set; } = 1;

        public Vector3? Origin { get; set; }

        public GameplayEffectContext() { }

        public GameplayEffectContext(GameplayAbilitySystem _Instigator) => instigator = _Instigator;

        /// <summary>
        /// A copy, so an applied spec can't be changed through the caller's context.
        /// </summary>
        public virtual GameplayEffectContext Duplicate() => (GameplayEffectContext)MemberwiseClone();
    }



    /// <summary>
    /// One application of an effect (UE's <c>FGameplayEffectSpec</c>): the effect, its level and context, and the values
    /// computed for it. Applying a spec copies it, so the caller's spec can be applied to any number of targets.
    /// </summary>
    public sealed class GameplayEffectSpec
    {
        private Dictionary<GameplayTag, float> setByCaller = new();
        private float[] modifierMagnitudes = Array.Empty<float>();
        private Dictionary<AttributeCaptureDefinition, CapturedAttribute> captures = new();

        public GameplayEffect Def { get; }

        public float Level { get; set; }

        public GameplayEffectContext Context { get; private set; }

        /// <summary>
        /// Seconds, computed on application. Negative for an infinite effect.
        /// </summary>
        public double Duration { get; internal set; }

        /// <summary>
        /// Seconds between executions, computed on application. 0 when not periodic.
        /// </summary>
        public double Period { get; internal set; }

        public int StackCount { get; internal set; } = 1;

        /// <summary>
        /// The source's tags, captured when the spec was made.
        /// </summary>
        public GameplayTagContainer CapturedSourceTags { get; private set; } = new();

        /// <summary>
        /// The target's tags, captured when the spec was applied.
        /// </summary>
        public GameplayTagContainer CapturedTargetTags { get; internal set; } = new();

        /// <summary>
        /// Tags this application grants besides the effect's own.
        /// </summary>
        public GameplayTagContainer DynamicGrantedTags { get; private set; } = new();

        /// <summary>
        /// Tags this application has besides the effect's own.
        /// </summary>
        public GameplayTagContainer DynamicAssetTags { get; private set; } = new();

        /// <summary>
        /// Reserved for multiplayer prediction. Always 0.
        /// </summary>
        public int PredictionKey => 0;



        /// <param name="_Context"> <see langword="null"/> for an empty context. </param>
        public GameplayEffectSpec(GameplayEffect _Def, float _Level = 1f, GameplayEffectContext _Context = null)
        {
            Def = _Def != null ? _Def : throw new ArgumentNullException(nameof(_Def));
            Level = _Level;
            Context = _Context ?? new GameplayEffectContext();
        }

        /// <summary>
        /// A spec for an effect another one applies (additional, overflow, conditional): the parent's level, context and
        /// SetByCaller values.
        /// </summary>
        public static GameplayEffectSpec FromParent(GameplayEffect _Def, GameplayEffectSpec _Parent)
        {
            var spec = new GameplayEffectSpec(_Def, _Parent.Level, _Parent.Context.Duplicate())
            {
                CapturedSourceTags = new GameplayTagContainer(_Parent.CapturedSourceTags),
                setByCaller = new Dictionary<GameplayTag, float>(_Parent.setByCaller),
            };

            // The child's own source captures, from the parent's source.
            var source = _Parent.Context.InstigatorEvenIfShutDown;
            if (source != null && !source.IsShuttingDown)
                spec.CaptureSourceAttributes(source);

            return spec;
        }

        /// <summary>
        /// A deep copy: what an application keeps.
        /// </summary>
        public GameplayEffectSpec Copy() => new(Def, Level, Context.Duplicate())
        {
            Duration = Duration,
            Period = Period,
            StackCount = StackCount,
            CapturedSourceTags = new GameplayTagContainer(CapturedSourceTags),
            CapturedTargetTags = new GameplayTagContainer(CapturedTargetTags),
            DynamicGrantedTags = new GameplayTagContainer(DynamicGrantedTags),
            DynamicAssetTags = new GameplayTagContainer(DynamicAssetTags),
            setByCaller = new Dictionary<GameplayTag, float>(setByCaller),
            modifierMagnitudes = (float[])modifierMagnitudes.Clone(),
            captures = CopyCaptures(captures),
        };

        private static Dictionary<AttributeCaptureDefinition, CapturedAttribute> CopyCaptures(Dictionary<AttributeCaptureDefinition, CapturedAttribute> _Captures)
        {
            var copy = new Dictionary<AttributeCaptureDefinition, CapturedAttribute>(_Captures.Count);
            foreach (var (definition, captured) in _Captures)
                copy[definition] = captured.Copy();

            return copy;
        }

        internal void CaptureSourceTags(GameplayTagContainer _Tags) => CapturedSourceTags = new GameplayTagContainer(_Tags);



        #region Captures
        /// <summary>
        /// Capture the source unit's attributes the effect reads: snapshots now, live ones as links.
        /// </summary>
        internal void CaptureSourceAttributes(GameplayAbilitySystem _Source) => Capture(_Source, CaptureSource.Source);

        /// <summary>
        /// Capture the target unit's attributes the effect reads, on application.
        /// </summary>
        internal void CaptureTargetAttributes(GameplayAbilitySystem _Target) => Capture(_Target, CaptureSource.Target);

        private void Capture(GameplayAbilitySystem _Unit, CaptureSource _Side)
        {
            foreach (var (definition, channels) in Def.GetCaptureDefinitions())
            {
                if (definition.source != _Side) continue;

                var captured = new CapturedAttribute(definition, _Unit);
                if (definition.snapshot)
                    captured.Snapshot(channels);

                captures[definition] = captured;
            }
        }

        /// <summary>
        /// A captured attribute's value. False if the spec didn't capture it, or its unit lacks it.
        /// </summary>
        public bool TryGetCapturedValue(AttributeCaptureDefinition _Capture, AttributeCalculationType _Type, int _Channel, out float _Value)
        {
            _Value = 0f;
            return captures.TryGetValue(_Capture, out var captured) && captured.TryGet(_Type, _Channel, out _Value);
        }

        /// <summary>
        /// The live captures, with the units they follow.
        /// </summary>
        internal IEnumerable<CapturedAttribute> LiveCaptures
        {
            get
            {
                foreach (var captured in captures.Values)
                    if (!captured.Definition.snapshot && captured.Unit != null)
                        yield return captured;
            }
        }
        #endregion



        #region SetByCaller
        /// <summary>
        /// Set a value a <see cref="SetByCallerMagnitude"/> reads under <paramref name="_DataTag"/>.
        /// </summary>
        public GameplayEffectSpec SetByCaller(GameplayTag _DataTag, float _Value)
        {
            setByCaller[_DataTag] = _Value;
            return this;
        }

        public bool TryGetSetByCaller(GameplayTag _DataTag, out float _Value) => setByCaller.TryGetValue(_DataTag, out _Value);
        #endregion



        #region Magnitudes
        /// <summary>
        /// Compute every modifier's magnitude, once, on application.
        /// </summary>
        internal void CalculateModifierMagnitudes()
        {
            var modifiers = Def.Modifiers;
            modifierMagnitudes = new float[modifiers.Count];
            for (int i = 0; i < modifiers.Count; i++)
                modifierMagnitudes[i] = modifiers[i].magnitude != null ? modifiers[i].magnitude.Calculate(this) : 0f;
        }

        /// <summary>
        /// The magnitude of modifier <paramref name="_Index"/>, as computed on application.
        /// </summary>
        /// <param name="_WithStacks"> Fold it by <see cref="StackCount"/>, as an execution of a stacked effect does. </param>
        public float GetMagnitude(int _Index, bool _WithStacks)
        {
            if (_Index < 0 || _Index >= modifierMagnitudes.Length) return 0f;

            float magnitude = modifierMagnitudes[_Index];
            return _WithStacks ? Fold(Def.Modifiers[_Index].op, magnitude, StackCount) : magnitude;
        }

        /// <summary>
        /// A magnitude folded by a stack count: additive ops <c>m × count</c>, multiply and divide ops
        /// <c>1 + (m − 1) × count</c>, override unchanged.
        /// </summary>
        public static float Fold(ModifierOp _Op, float _Magnitude, int _Count)
        {
            float count = Math.Max(1, _Count);
            return _Op switch
            {
                ModifierOp.AddBase or ModifierOp.AddFinal => _Magnitude * count,
                ModifierOp.MultiplyAdditive or ModifierOp.DivideAdditive or ModifierOp.MultiplyCompound => 1f + (_Magnitude - 1f) * count,
                _ => _Magnitude,
            };
        }
        #endregion



        /// <summary>
        /// The effect's own tags and this application's.
        /// </summary>
        public GameplayTagContainer GetAllAssetTags()
        {
            var tags = Def.GetAssetTags();
            tags.AddTags(DynamicAssetTags);
            return tags;
        }

        /// <summary>
        /// The tags the effect grants and this application grants.
        /// </summary>
        public GameplayTagContainer GetAllGrantedTags()
        {
            var tags = Def.GetGrantedTags();
            tags.AddTags(DynamicGrantedTags);
            return tags;
        }

        public override string ToString() => $"{Def.name} (level {Level})";
    }
}
