using System;
using System.Collections.Generic;
using System.Linq;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// What is wrong with an effect asset.
    /// </summary>
    public enum EffectIssueCode
    {
        /// <summary> An instant effect has granted tags, stacking or a period: they would silently do nothing. </summary>
        InstantWithDurationFeatures,

        /// <summary> A modifier names no attribute, or one that resolves as Deleted or MissingRegistry. </summary>
        UnresolvedAttribute,

        /// <summary> A period between 0 and the settings' minimum. </summary>
        PeriodTooSmall,

        /// <summary> The effect reaches itself through the effects it applies. </summary>
        ApplicationCycle,

        /// <summary> A SetByCaller magnitude has no tag. </summary>
        SetByCallerWithoutTag,

        /// <summary> A duration effect with a constant duration ≤ 0: it runs as instant. A warning. </summary>
        NonPositiveDuration,

        /// <summary> A duration or infinite effect, with no period, modifies a meta attribute: the modifier is ignored. </summary>
        MetaAttributeOnDurationEffect,

        /// <summary> The effect's granted tags fail its own ongoing requirements: it would inhibit itself. </summary>
        SelfInhibition,

        /// <summary> The effect's granted tags match its own removal requirements: it would remove itself. </summary>
        SelfRemoval,
    }

    public enum EffectIssueSeverity
    {
        Warning,
        Error,
    }



    public readonly struct EffectIssue
    {
        public readonly EffectIssueCode Code;
        public readonly EffectIssueSeverity Severity;
        public readonly string Message;

        public EffectIssue(EffectIssueCode _Code, EffectIssueSeverity _Severity, string _Message)
        {
            Code = _Code;
            Severity = _Severity;
            Message = _Message;
        }

        public override string ToString() => $"{Severity}: {Message}";
    }



    /// <summary>
    /// The issues of one effect asset.
    /// </summary>
    public sealed class EffectValidationResult
    {
        private readonly List<EffectIssue> issues = new();

        public IReadOnlyList<EffectIssue> Issues => issues;

        public bool HasErrors => issues.Any(e => e.Severity == EffectIssueSeverity.Error);

        public bool HasError(EffectIssueCode _Code) => issues.Any(e => e.Code == _Code && e.Severity == EffectIssueSeverity.Error);

        public bool HasWarning(EffectIssueCode _Code) => issues.Any(e => e.Code == _Code && e.Severity == EffectIssueSeverity.Warning);

        internal void Add(EffectIssueCode _Code, EffectIssueSeverity _Severity, string _Message) => issues.Add(new EffectIssue(_Code, _Severity, _Message));

        public override string ToString() => string.Join("\n", issues);
    }



    /// <summary>
    /// Checks an effect asset for mistakes that would silently do nothing, or loop. The runtime survives
    /// each of them; the validator says so before it happens.
    /// </summary>
    public static class EffectValidator
    {
        /// <param name="_Settings"> The project's settings, for the period floor; <see langword="null"/> for the defaults. </param>
        public static EffectValidationResult Validate(GameplayEffect _Effect, AbilitySystemSettings _Settings = null)
        {
            var result = new EffectValidationResult();
            if (_Effect == null) return result;

            double minPeriod = _Settings != null ? _Settings.MinPeriod : AbilitySystemSettings.DEFAULT_MIN_PERIOD;
            string name = _Effect.name;

            CheckInstant(_Effect, name, result);
            CheckModifiers(_Effect, name, result);
            CheckPeriod(_Effect, name, minPeriod, result);
            CheckDuration(_Effect, name, result);
            CheckCycles(_Effect, name, result);
            CheckMeta(_Effect, name, result);
            CheckOwnRequirements(_Effect, name, result);
            CheckCaptures(_Effect, name, result);

            return result;
        }

        private static void CheckInstant(GameplayEffect _Effect, string _Name, EffectValidationResult _Result)
        {
            if (_Effect.DurationPolicy != DurationPolicy.Instant) return;

            var features = new List<string>();
            if (!_Effect.GetGrantedTags().IsEmpty) features.Add("granted tags");
            if (_Effect.Stacking != null && _Effect.Stacking.type != StackingType.None) features.Add("stacking");
            if (_Effect.Period.Value != 0f) features.Add("a period");
            if (_Effect.FindComponent<TargetTagRequirementsComponent>() is { NeedsRechecks: true }) features.Add("ongoing or removal requirements");
            if (_Effect.FindComponent<ImmunityComponent>() != null) features.Add("immunity");
            if (_Effect.FindComponent<BlockAbilityTagsComponent>() is { tags: { IsEmpty: false } }) features.Add("blocked ability tags");

            if (features.Count > 0)
                _Result.Add(EffectIssueCode.InstantWithDurationFeatures, EffectIssueSeverity.Error,
                    $"\"{_Name}\" is instant but has {string.Join(", ", features)}. An instant effect never stays active, so they do nothing.");
        }

        private static void CheckModifiers(GameplayEffect _Effect, string _Name, EffectValidationResult _Result)
        {
            for (int i = 0; i < _Effect.Modifiers.Count; i++)
            {
                var modifier = _Effect.Modifiers[i];
                if (modifier == null) continue;

                var status = modifier.attribute.Resolve().Status;
                if (status != AttributeStatus.Live)
                    _Result.Add(EffectIssueCode.UnresolvedAttribute, EffectIssueSeverity.Error,
                        status == AttributeStatus.None
                            ? $"\"{_Name}\": modifier {i + 1} names no attribute."
                            : $"\"{_Name}\": modifier {i + 1} names \"{modifier.attribute.LastKnownName}\", which doesn't resolve ({status}). It is skipped.");

                CheckSetByCaller(modifier.magnitude, $"modifier {i + 1}", _Name, _Result);
            }
        }

        private static void CheckSetByCaller(Magnitude _Magnitude, string _Where, string _Name, EffectValidationResult _Result)
        {
            if (_Magnitude is SetByCallerMagnitude setByCaller && setByCaller.dataTag.IsNone)
                _Result.Add(EffectIssueCode.SetByCallerWithoutTag, EffectIssueSeverity.Error,
                    $"\"{_Name}\": the SetByCaller magnitude of {_Where} has no tag, so no caller can set it.");
        }

        private static void CheckPeriod(GameplayEffect _Effect, string _Name, double _MinPeriod, EffectValidationResult _Result)
        {
            if (_Effect.DurationPolicy == DurationPolicy.Instant) return;

            float period = _Effect.Period.Value;
            if (period > 0f && period < _MinPeriod)
                _Result.Add(EffectIssueCode.PeriodTooSmall, EffectIssueSeverity.Error,
                    $"\"{_Name}\" has a period of {period} s, below the minimum of {_MinPeriod} s. It is clamped at runtime.");
        }

        private static void CheckDuration(GameplayEffect _Effect, string _Name, EffectValidationResult _Result)
        {
            if (_Effect.DurationPolicy != DurationPolicy.HasDuration) return;

            CheckSetByCaller(_Effect.DurationMagnitude, "the duration", _Name, _Result);

            bool nonPositive = _Effect.DurationMagnitude == null
                || (_Effect.DurationMagnitude is ScalableFloatMagnitude constant && constant.IsConstant && constant.value.Value <= 0f);
            if (nonPositive)
                _Result.Add(EffectIssueCode.NonPositiveDuration, EffectIssueSeverity.Warning,
                    $"\"{_Name}\" has a duration ≤ 0. It runs as an instant effect.");
        }

        /// <summary>
        /// Whether the effect reaches itself through the effects it applies: on application, on completion, on overflow.
        /// </summary>
        private static void CheckCycles(GameplayEffect _Effect, string _Name, EffectValidationResult _Result)
        {
            var path = new List<GameplayEffect>();
            var visited = new HashSet<GameplayEffect>();

            bool Reaches(GameplayEffect _Current)
            {
                foreach (var next in AppliedBy(_Current))
                {
                    if (next == _Effect)
                    {
                        path.Add(next);
                        return true;
                    }

                    if (!visited.Add(next)) continue;

                    path.Add(next);
                    if (Reaches(next)) return true;
                    path.RemoveAt(path.Count - 1);
                }

                return false;
            }

            if (Reaches(_Effect))
                _Result.Add(EffectIssueCode.ApplicationCycle, EffectIssueSeverity.Error,
                    $"\"{_Name}\" applies itself again: {string.Join(" → ", new[] { _Effect }.Concat(path).Select(e => $"\"{e.name}\""))}. " +
                    "At runtime the chain stops at the application depth limit.");
        }

        private static IEnumerable<GameplayEffect> AppliedBy(GameplayEffect _Effect)
        {
            foreach (var component in _Effect.Components)
                if (component is AdditionalEffectsComponent additional)
                    foreach (var effect in additional.AllEffects())
                        yield return effect;

            if (_Effect.Stacking?.overflowEffects != null)
                foreach (var effect in _Effect.Stacking.overflowEffects)
                    if (effect != null)
                        yield return effect;

            foreach (var execution in _Effect.Executions)
                if (execution?.conditionalEffects != null)
                    foreach (var conditional in execution.conditionalEffects)
                        if (conditional?.effect != null)
                            yield return conditional.effect;
        }

        private static void CheckMeta(GameplayEffect _Effect, string _Name, EffectValidationResult _Result)
        {
            if (_Effect.DurationPolicy == DurationPolicy.Instant || _Effect.Period.Value > 0f) return;

            var database = GameplayAttributeDatabase.Active;
            if (database == null) return;

            foreach (var modifier in _Effect.Modifiers)
            {
                if (modifier == null || !database.IsMeta(modifier.attribute.Handle)) continue;

                _Result.Add(EffectIssueCode.MetaAttributeOnDurationEffect, EffectIssueSeverity.Error,
                    $"\"{_Name}\" modifies the meta attribute \"{modifier.attribute.Name}\" while active. Only instant and periodic executions " +
                    "may write a meta attribute; this modifier is ignored at runtime.");
            }
        }

        private static void CheckOwnRequirements(GameplayEffect _Effect, string _Name, EffectValidationResult _Result)
        {
            var requirements = _Effect.FindComponent<TargetTagRequirementsComponent>();
            var granted = _Effect.GetGrantedTags();
            if (requirements == null || granted.IsEmpty) return;

            var ongoing = requirements.ongoing;
            if (ongoing != null && !ongoing.IsEmpty)
            {
                bool grantsIgnored = ongoing.ignoreTags != null && ongoing.ignoreTags.Any(granted.HasTag);
                bool breaksQuery = ongoing.query != null && !ongoing.query.IsEmpty
                                && ongoing.query.Matches(new GameplayTags.GameplayTagContainer()) && !ongoing.query.Matches(granted);

                if (grantsIgnored || breaksQuery)
                    _Result.Add(EffectIssueCode.SelfInhibition, EffectIssueSeverity.Error,
                        $"\"{_Name}\" grants tags its own ongoing requirements forbid ({granted}). It would inhibit itself; at runtime the " +
                        "propagation pass limit leaves it inhibited.");
            }

            var removal = requirements.removal;
            if (removal != null && !removal.IsEmpty && removal.RequirementsMet(granted))
                _Result.Add(EffectIssueCode.SelfRemoval, EffectIssueSeverity.Error,
                    $"\"{_Name}\" grants tags that match its own removal requirements ({granted}). It would remove itself as soon as it applies.");
        }

        private static void CheckCaptures(GameplayEffect _Effect, string _Name, EffectValidationResult _Result)
        {
            foreach (var capture in _Effect.GetCaptureDefinitions().Keys)
            {
                var status = capture.attribute.Resolve().Status;
                if (status == AttributeStatus.Live) continue;

                _Result.Add(EffectIssueCode.UnresolvedAttribute, EffectIssueSeverity.Error,
                    $"\"{_Name}\" captures \"{capture.attribute.LastKnownName}\", which doesn't resolve ({status}). It reads as 0.");
            }
        }
    }
}
