using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Attributes;
using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// How long an effect lasts.
    /// </summary>
    public enum DurationPolicy
    {
        /// <summary> Executes once on base values, and is gone. </summary>
        Instant,

        /// <summary> Active for its duration. </summary>
        HasDuration,

        /// <summary> Active until removed. </summary>
        Infinite,
    }



    /// <summary>
    /// What a periodic effect's schedule does when its inhibition ends.
    /// </summary>
    public enum PeriodicInhibitionPolicy
    {
        /// <summary> Keep the original schedule. </summary>
        NeverReset,

        /// <summary> Start a new period from now. </summary>
        ResetPeriod,

        /// <summary> Execute now, then start a new period. </summary>
        ExecuteAndResetPeriod,
    }



    /// <summary>
    /// One modifier of an effect.
    /// </summary>
    [Serializable]
    public sealed class ModifierInfo
    {
        public GameplayAttribute attribute;

        public ModifierOp op;

        [SerializeReference, InlineInstanced]
        public Magnitude magnitude = new ScalableFloatMagnitude();

        [Tooltip("Channels evaluate in order; each channel's result is the next channel's base.")]
        public int evaluationChannel;

        [Tooltip("The modifier counts only while the source's tags, captured when the spec was made, pass.")]
        public TagRequirements sourceTags = new();

        [Tooltip("The modifier counts only while the target's tags pass. Re-checked whenever they change.")]
        public TagRequirements targetTags = new();

        public ModifierInfo() { }

        public ModifierInfo(GameplayAttribute _Attribute, ModifierOp _Op, Magnitude _Magnitude)
        {
            attribute = _Attribute;
            op = _Op;
            magnitude = _Magnitude;
        }
    }



    public enum StackingType
    {
        /// <summary> Every application is its own active effect. </summary>
        None,

        /// <summary> One stack per source unit and effect. </summary>
        AggregateBySource,

        /// <summary> One stack per effect on the target. </summary>
        AggregateByTarget,
    }

    public enum StackDurationRefreshPolicy { RefreshOnSuccessfulApplication, NeverRefresh }

    public enum StackPeriodResetPolicy { ResetOnSuccessfulApplication, NeverReset }

    public enum StackExpirationPolicy { ClearEntireStack, RemoveSingleStackAndRefreshDuration, RefreshDuration }



    /// <summary>
    /// How applications of an effect stack.
    /// </summary>
    [Serializable]
    public sealed class StackingConfig
    {
        public StackingType type;

        [Tooltip("Maximum stacks. 0 = no limit.")]
        public int limitCount;

        public StackDurationRefreshPolicy durationRefreshPolicy;

        public StackPeriodResetPolicy periodResetPolicy;

        public StackExpirationPolicy expirationPolicy;

        [Tooltip("Applied when an application arrives at the limit.")]
        public List<GameplayEffect> overflowEffects = new();

        [Tooltip("At the limit, the application fails: no refresh, no reset.")]
        public bool denyOverflowApplication;

        [Tooltip("With deny: an overflow removes the whole stack.")]
        public bool clearStackOnOverflow;
    }



    /// <summary>
    /// A cue an effect triggers.
    /// </summary>
    [Serializable]
    public sealed class GameplayEffectCue
    {
        public GameplayTagContainer cueTags = new();

        [Tooltip("Optional: the attribute whose value is the cue's magnitude.")]
        public GameplayAttribute magnitudeAttribute;

        public float minLevel;

        public float maxLevel;
    }



    /// <summary>
    /// A gameplay effect (UE's <c>UGameplayEffect</c>): an immutable template. Applying one makes a spec, which either
    /// executes once on base values or becomes an active effect on the target.
    /// </summary>
    [CreateAssetMenu(menuName = "LMK/Ability System/Gameplay Effect", fileName = "GameplayEffect")]
    public sealed class GameplayEffect : ScriptableObject
    {
        [SerializeField]
        private DurationPolicy durationPolicy;

        [SerializeReference, InlineInstanced, Tooltip("HasDuration only: the duration in seconds.")]
        private Magnitude durationMagnitude = new ScalableFloatMagnitude();

        [SerializeField, Tooltip("Seconds between executions. 0 = not periodic.")]
        private ScalableFloat period;

        [SerializeField, Tooltip("Execute once on application too, not only at the end of each period.")]
        private bool executePeriodicEffectOnApplication = true;

        [SerializeField]
        private PeriodicInhibitionPolicy periodicInhibitionPolicy;

        [SerializeField]
        private List<ModifierInfo> modifiers = new();

        [SerializeField]
        private List<ExecutionDefinition> executions = new();

        [SerializeReference, InlineInstanced]
        private List<GameplayEffectComponent> components = new();

        [SerializeField]
        private StackingConfig stacking = new();

        [SerializeField]
        private List<GameplayEffectCue> cues = new();

        [SerializeField, Tooltip("Raise the cues' OnActive and WhileActive only for the first application of a stack, not for each new one.")]
        private bool suppressStackingCues;

        public DurationPolicy DurationPolicy
        {
            get => durationPolicy;
            internal set => durationPolicy = value;
        }

        public Magnitude DurationMagnitude
        {
            get => durationMagnitude;
            internal set => durationMagnitude = value;
        }

        public ScalableFloat Period
        {
            get => period;
            internal set => period = value;
        }

        public bool ExecutePeriodicEffectOnApplication
        {
            get => executePeriodicEffectOnApplication;
            internal set => executePeriodicEffectOnApplication = value;
        }

        public PeriodicInhibitionPolicy PeriodicInhibitionPolicy
        {
            get => periodicInhibitionPolicy;
            internal set => periodicInhibitionPolicy = value;
        }

        public IReadOnlyList<ModifierInfo> Modifiers => modifiers;

        public IReadOnlyList<ExecutionDefinition> Executions => executions;

        internal List<ExecutionDefinition> ExecutionList => executions;

        public IReadOnlyList<GameplayEffectComponent> Components => components;

        public StackingConfig Stacking => stacking;

        public IReadOnlyList<GameplayEffectCue> Cues => cues;

        /// <summary>
        /// Whether a new application to an existing stack raises the cues' <c>OnActive</c> and <c>WhileActive</c> again
        /// (false, as in UE) or not.
        /// </summary>
        public bool SuppressStackingCues
        {
            get => suppressStackingCues;
            internal set => suppressStackingCues = value;
        }

        internal List<ModifierInfo> ModifierList => modifiers;

        internal List<GameplayEffectComponent> ComponentList => components;

        internal List<GameplayEffectCue> CueList => cues;



        /// <summary>
        /// The effect's own tags (<see cref="AssetTagsComponent"/>), used by queries, immunity and removal.
        /// </summary>
        public GameplayTagContainer GetAssetTags()
        {
            var tags = new GameplayTagContainer();
            foreach (var component in components)
                if (component is AssetTagsComponent assetTags && assetTags.tags != null)
                    tags.AddTags(assetTags.tags);

            return tags;
        }

        /// <summary>
        /// The tags the effect grants its target while active (<see cref="TargetTagsComponent"/>).
        /// </summary>
        public GameplayTagContainer GetGrantedTags()
        {
            var tags = new GameplayTagContainer();
            foreach (var component in components)
                if (component is TargetTagsComponent targetTags && targetTags.tags != null)
                    tags.AddTags(targetTags.tags);

            return tags;
        }

        /// <summary>
        /// Every attribute the effect reads, with the evaluation channels its snapshots must keep: from modifier
        /// magnitudes, the duration, execution calculations and scoped modifiers.
        /// </summary>
        public Dictionary<AttributeCaptureDefinition, HashSet<int>> GetCaptureDefinitions()
        {
            var result = new Dictionary<AttributeCaptureDefinition, HashSet<int>>();

            void Add(AttributeCaptureDefinition _Capture, IEnumerable<int> _Channels = null)
            {
                if (_Capture.attribute.IsNone) return;
                if (!result.TryGetValue(_Capture, out var channels))
                    result.Add(_Capture, channels = new HashSet<int>());
                if (_Channels != null)
                    channels.UnionWith(_Channels);
            }

            void AddMagnitude(Magnitude _Magnitude)
            {
                if (_Magnitude == null) return;
                foreach (var capture in _Magnitude.GetCaptures())
                    Add(capture, _Magnitude.GetCaptureChannels(capture));
            }

            foreach (var modifier in modifiers)
                AddMagnitude(modifier?.magnitude);
            AddMagnitude(durationMagnitude);

            foreach (var execution in executions)
            {
                if (execution?.calculation != null)
                    foreach (var capture in execution.calculation.relevantAttributes)
                        Add(capture);

                if (execution?.calculationModifiers != null)
                    foreach (var scoped in execution.calculationModifiers)
                    {
                        if (scoped == null) continue;
                        Add(scoped.capture);
                        AddMagnitude(scoped.magnitude);
                    }
            }

            return result;
        }

        /// <summary>
        /// The first component of a type, or <see langword="null"/>.
        /// </summary>
        public T FindComponent<T>() where T : GameplayEffectComponent
        {
            foreach (var component in components)
                if (component is T found)
                    return found;

            return null;
        }
    }
}
