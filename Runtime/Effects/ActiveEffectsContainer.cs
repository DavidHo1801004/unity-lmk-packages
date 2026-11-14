using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// A unit's active effects, and the paths that change them: application, immunity,
    /// stacking, execution, ticking, inhibition and removal. Only its <see cref="GameplayAbilitySystem"/> calls it, inside a
    /// mutation.
    /// </summary>
    /// <remarks>
    /// The list is locked while it is ticked and during an application, so callbacks can add and remove effects: an
    /// added effect works at once and ticks from the next step; a removed one is dead at once.
    /// </remarks>
    internal sealed class ActiveEffectsContainer
    {
        // Times within this of a period or of the end count as reached: steps are sums of doubles.
        private const double EPSILON = 1e-6;

        private readonly GameplayAbilitySystem owner;
        private readonly LockableList<ActiveGameplayEffect> effects = new(e => e.IsRemoved);
        private readonly List<ActiveGameplayEffect> pendingRechecks = new();

        // The effect being applied, which its own RemoveOther component must not remove.
        private ActiveEffectHandle applying;

        /// <summary>
        /// Active modifiers whose target tag requirements must be checked again when the unit's tags change.
        /// </summary>
        public int TagDependentModifiers { get; private set; }

        public ActiveEffectsContainer(GameplayAbilitySystem _Owner)
        {
            owner = _Owner;
        }

        private AbilitySystemWorld World => owner.World;

        private AttributeContainer Attributes => owner.Attributes;

        public int Count => effects.LiveCount;

        public ActiveGameplayEffect Find(ActiveEffectHandle _Handle)
            => _Handle.IsValid ? effects.Find(e => e.Handle == _Handle) : null;

        public List<ActiveGameplayEffect> Snapshot() => effects.Snapshot();



        #region Application
        /// <summary>
        /// Apply a copy of <paramref name="_Spec"/>. Returns the active effect's handle, or
        /// <see cref="ActiveEffectHandle.Invalid"/> for an instant execution or a refused application.
        /// </summary>
        public ActiveEffectHandle Apply(GameplayEffectSpec _Spec)
        {
            var settings = World.Settings;
            if (World.ApplicationDepth >= settings.MaxApplicationDepth)
            {
                Debug.LogError($"[{owner.Name}] \"{_Spec.Def.name}\" was not applied: effects applying effects reached the application depth limit " +
                               $"of {settings.MaxApplicationDepth}. Look for effects that apply each other.");
                return ActiveEffectHandle.Invalid;
            }

            World.ApplicationDepth++;
            effects.Lock();
            try
            {
                var spec = _Spec.Copy();
                var def = spec.Def;

                var blocker = FindImmunity(spec);
                if (blocker != null)
                {
                    owner.RaiseImmunityBlocked(spec, blocker);
                    return ActiveEffectHandle.Invalid;
                }

                foreach (var component in def.Components)
                    if (component != null && !component.CanApply(owner, spec))
                        return ActiveEffectHandle.Invalid;

                spec.CapturedTargetTags = owner.Tags.Snapshot();
                spec.CaptureTargetAttributes(owner);
                spec.Duration = EvaluateDuration(spec);
                spec.Period = EvaluatePeriod(spec);
                spec.CalculateModifierMagnitudes();

                bool asInstant = def.DurationPolicy == DurationPolicy.Instant;
                if (def.DurationPolicy == DurationPolicy.HasDuration && spec.Duration <= 0)
                {
                    Debug.LogWarning($"[{owner.Name}] \"{def.name}\" has a duration <= 0 ({spec.Duration} s). It is executed as an instant effect.");
                    asInstant = true;
                }

                var handle = ActiveEffectHandle.Invalid;
                if (asInstant)
                {
                    Execute(spec);
                }
                else
                {
                    var existing = FindStackable(spec);
                    handle = existing != null ? ApplyStack(existing, spec) : AddActive(spec);
                    if (!handle.IsValid) return ActiveEffectHandle.Invalid;
                }

                var previousApplying = applying;
                applying = handle;
                try
                {
                    foreach (var component in def.Components)
                        component?.OnApplied(owner, spec);
                }
                finally
                {
                    applying = previousApplying;
                }

                owner.RaiseEffectAppliedToSelf(spec, handle);
                spec.Context.Instigator?.RaiseEffectAppliedToTarget(owner, spec, handle);
                return handle;
            }
            finally
            {
                effects.Unlock();
                World.ApplicationDepth--;
            }
        }

        private static double EvaluateDuration(GameplayEffectSpec _Spec) => _Spec.Def.DurationPolicy switch
        {
            DurationPolicy.HasDuration => _Spec.Def.DurationMagnitude?.Calculate(_Spec) ?? 0f,
            DurationPolicy.Infinite => -1,
            _ => 0,
        };

        private double EvaluatePeriod(GameplayEffectSpec _Spec)
        {
            if (_Spec.Def.DurationPolicy == DurationPolicy.Instant) return 0;

            double period = _Spec.Def.Period.At(_Spec.Level);
            if (!(period > 0)) return 0;

            double minPeriod = World.Settings.MinPeriod;
            if (period < minPeriod)
            {
                Debug.LogWarning($"[{owner.Name}] \"{_Spec.Def.name}\" has a period of {period} s, below the minimum: period clamped to {minPeriod} s.");
                period = minPeriod;
            }

            return period;
        }

        private ActiveEffectHandle AddActive(GameplayEffectSpec _Spec)
        {
            var effect = new ActiveGameplayEffect(new ActiveEffectHandle(World.NextHandle()), owner, _Spec, World.Time);
            effects.Add(effect);
            Link(effect);

            bool active = true;
            foreach (var component in _Spec.Def.Components)
                if (component != null)
                    active &= component.OnActiveAdded(owner, effect);

            SetInhibited(effect, !active, true);

            if (effect.IsPeriodic && _Spec.Def.ExecutePeriodicEffectOnApplication && !effect.IsInhibited && !effect.IsRemoved)
                ExecutePeriod(effect);

            return effect.Handle;
        }

        /// <summary>
        /// The first active, uninhibited effect whose immunity blocks <paramref name="_Spec"/>, or <see langword="null"/>.
        /// </summary>
        private ActiveGameplayEffect FindImmunity(GameplayEffectSpec _Spec)
        {
            foreach (var effect in effects.Snapshot())
            {
                if (effect.IsInhibited) continue;

                foreach (var component in effect.Def.Components)
                    if (component is ImmunityComponent immunity && immunity.Blocks(_Spec))
                        return effect;
            }

            return null;
        }
        #endregion



        #region Stacking
        /// <summary>
        /// The active effect a new application stacks onto: by target, the same effect; by source, the
        /// same effect from the same source unit.
        /// </summary>
        private ActiveGameplayEffect FindStackable(GameplayEffectSpec _Spec)
        {
            var stacking = _Spec.Def.Stacking;
            if (stacking == null || stacking.type == StackingType.None) return null;

            var source = _Spec.Context.InstigatorEvenIfShutDown;
            return effects.Find(e => e.Def == _Spec.Def
                                  && (stacking.type == StackingType.AggregateByTarget || e.Spec.Context.InstigatorEvenIfShutDown == source));
        }

        /// <summary>
        /// Add an application to an existing stack. The latest application's level, context, SetByCaller values and
        /// snapshots win, for every stack.
        /// </summary>
        private ActiveEffectHandle ApplyStack(ActiveGameplayEffect _Existing, GameplayEffectSpec _Spec)
        {
            var stacking = _Spec.Def.Stacking;
            bool atLimit = stacking.limitCount > 0 && _Existing.StackCount >= stacking.limitCount;

            if (atLimit)
            {
                foreach (var overflow in stacking.overflowEffects)
                    if (overflow != null)
                        owner.ApplySpecToSelf(GameplayEffectSpec.FromParent(overflow, _Spec));

                if (stacking.denyOverflowApplication)
                {
                    if (stacking.clearStackOnOverflow)
                        Remove(_Existing, true);

                    return ActiveEffectHandle.Invalid;
                }
            }

            if (_Existing.IsRemoved) return ActiveEffectHandle.Invalid;

            int oldCount = _Existing.StackCount;
            int newCount = oldCount + Math.Max(1, _Spec.StackCount);
            if (stacking.limitCount > 0)
                newCount = Math.Min(newCount, stacking.limitCount);

            Unlink(_Existing);
            _Spec.StackCount = newCount;
            _Existing.Spec = _Spec;
            Link(_Existing);

            double now = World.Time;
            if (stacking.durationRefreshPolicy == StackDurationRefreshPolicy.RefreshOnSuccessfulApplication)
                RefreshDuration(_Existing, now);

            if (stacking.periodResetPolicy == StackPeriodResetPolicy.ResetOnSuccessfulApplication)
            {
                _Existing.PeriodStart = now;
                _Existing.PeriodsDone = 0;
            }

            if (!_Existing.IsInhibited)
            {
                UpdateModifiers(_Existing);

                // Each application to the stack shows again, as in UE, unless the effect suppresses stacking cues.
                if (!_Spec.Def.SuppressStackingCues)
                {
                    RaiseCues(_Spec, GameplayCueEvent.OnActive);
                    RaiseCues(_Spec, GameplayCueEvent.WhileActive);
                }
            }

            owner.RaiseStackChanged(_Existing.Handle, oldCount, newCount);
            return _Existing.Handle;
        }

        private static void RefreshDuration(ActiveGameplayEffect _Effect, double _Now)
        {
            _Effect.StartTime = _Now;
            _Effect.EndTime = _Effect.HasDuration ? _Now + _Effect.Spec.Duration : double.PositiveInfinity;
        }

        private void SetStackCount(ActiveGameplayEffect _Effect, int _Count)
        {
            int oldCount = _Effect.StackCount;
            _Effect.Spec.StackCount = Math.Max(1, _Count);
            if (!_Effect.IsInhibited)
                UpdateModifiers(_Effect);

            owner.RaiseStackChanged(_Effect.Handle, oldCount, _Effect.StackCount);
        }

        /// <summary>
        /// The duration ran out: the stack's expiration policy decides.
        /// </summary>
        private void OnDurationExpired(ActiveGameplayEffect _Effect, double _Now)
        {
            switch (_Effect.Def.Stacking?.expirationPolicy ?? StackExpirationPolicy.ClearEntireStack)
            {
                case StackExpirationPolicy.RemoveSingleStackAndRefreshDuration when _Effect.StackCount > 1:
                    SetStackCount(_Effect, _Effect.StackCount - 1);
                    RefreshDuration(_Effect, _Now);
                    break;

                case StackExpirationPolicy.RefreshDuration when _Effect.Def.Stacking.type != StackingType.None:
                    RefreshDuration(_Effect, _Now);
                    owner.RaiseStackExpired(_Effect.Handle);
                    break;

                default:
                    Remove(_Effect, false);
                    break;
            }
        }
        #endregion



        #region Execution
        /// <summary>
        /// Execute the spec on base values: its modifiers in asset order, then each execution
        /// calculation with its output modifiers and conditional effects, then the components. A modifier on an
        /// attribute the unit lacks is skipped; once the unit shuts down, the rest are.
        /// </summary>
        private void Execute(GameplayEffectSpec _Spec)
        {
            bool cuesHandledManually = false;
            var executed = _Spec.Def.Cues.Count > 0 ? new List<(GameplayAttribute, float)>() : null;

            var modifiers = _Spec.Def.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                if (owner.IsShuttingDown) return;

                var modifier = modifiers[i];
                if (modifier == null || !Qualifies(modifier, _Spec)) continue;

                float magnitude = _Spec.GetMagnitude(i, true);
                Attributes.ExecuteModifier(modifier.attribute, modifier.op, magnitude, _Spec);
                executed?.Add((modifier.attribute, magnitude));
            }

            foreach (var execution in _Spec.Def.Executions)
            {
                if (owner.IsShuttingDown) return;
                if (execution?.calculation == null) continue;

                var output = new ExecutionOutput();
                try
                {
                    execution.calculation.Execute(new ExecutionParams(_Spec, owner, execution.calculationModifiers), output);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    continue;
                }

                cuesHandledManually |= output.CuesHandledManually;
                foreach (var (attribute, op, magnitude) in output.modifiers)
                {
                    if (owner.IsShuttingDown) return;
                    Attributes.ExecuteModifier(attribute, op, magnitude, _Spec);
                    executed?.Add((attribute, magnitude));
                }

                if (!output.TriggersConditionalEffects) continue;

                foreach (var conditional in execution.conditionalEffects)
                {
                    if (conditional?.effect == null) continue;
                    if (conditional.requiredSourceTags != null && !_Spec.CapturedSourceTags.HasAll(conditional.requiredSourceTags)) continue;

                    owner.ApplySpecToSelf(GameplayEffectSpec.FromParent(conditional.effect, _Spec));
                }
            }

            foreach (var component in _Spec.Def.Components)
            {
                if (owner.IsShuttingDown) return;
                component?.OnExecuted(owner, _Spec);
            }

            if (!cuesHandledManually && !owner.IsShuttingDown)
                RaiseCues(_Spec, GameplayCueEvent.Executed, executed);
        }

        /// <summary>
        /// One period of a periodic effect. Magnitudes that follow live captures are calculated again first.
        /// </summary>
        private void ExecutePeriod(ActiveGameplayEffect _Effect)
        {
            if (_Effect.unlinks.Count > 0)
                _Effect.Spec.CalculateModifierMagnitudes();

            Execute(_Effect.Spec);
        }

        private bool Qualifies(ModifierInfo _Modifier, GameplayEffectSpec _Spec)
            => (_Modifier.sourceTags == null || _Modifier.sourceTags.RequirementsMet(_Spec.CapturedSourceTags))
            && (_Modifier.targetTags == null || _Modifier.targetTags.RequirementsMet(owner.Tags));
        #endregion



        #region Cues
        /// <summary>
        /// Queue the effect's cues. <paramref name="_Executed"/>: the magnitudes an execution applied,
        /// for the cue's magnitude attribute; without them the modifiers' current magnitudes are used.
        /// </summary>
        private void RaiseCues(GameplayEffectSpec _Spec, GameplayCueEvent _Event, List<(GameplayAttribute Attribute, float Magnitude)> _Executed = null)
        {
            foreach (var cue in _Spec.Def.Cues)
            {
                if (cue?.cueTags == null || cue.cueTags.IsEmpty) continue;

                var parameters = MakeCueParameters(_Spec, cue, _Executed);
                foreach (var tag in cue.cueTags)
                {
                    var copy = parameters.Copy();
                    copy.OriginalTag = copy.MatchedTag = tag;
                    World.Cues.Enqueue(owner, tag, _Event, copy);
                }
            }
        }

        private GameplayCueParameters MakeCueParameters(GameplayEffectSpec _Spec, GameplayEffectCue _Cue, List<(GameplayAttribute Attribute, float Magnitude)> _Executed)
        {
            var context = _Spec.Context;
            var parameters = new GameplayCueParameters
            {
                RawMagnitude = CueMagnitude(_Spec, _Cue.magnitudeAttribute, _Executed),
                NormalizedMagnitude = NormalizeLevel(_Cue, _Spec.Level),
                EffectContext = context,
                SourceTags = new GameplayTagContainer(_Spec.CapturedSourceTags),
                TargetTags = owner.Tags.Snapshot(),
                Location = context?.Origin,
                Instigator = context?.Instigator,
                EffectCauser = context?.EffectCauser,
                SourceObject = context?.SourceObject,
                Level = _Spec.Level,
            };

            return parameters;
        }

        private static float CueMagnitude(GameplayEffectSpec _Spec, GameplayAttribute _Attribute, List<(GameplayAttribute Attribute, float Magnitude)> _Executed)
        {
            if (_Attribute.IsNone) return 0f;

            if (_Executed != null)
            {
                float sum = 0f;
                bool found = false;
                foreach (var (attribute, magnitude) in _Executed)
                    if (attribute == _Attribute)
                    {
                        sum += magnitude;
                        found = true;
                    }

                if (found) return sum;
            }

            var modifiers = _Spec.Def.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
                if (modifiers[i] != null && modifiers[i].attribute == _Attribute)
                    return _Spec.GetMagnitude(i, true);

            return 0f;
        }

        /// <summary>
        /// The level's place in the cue's level range, 0 to 1. 1 when the range is empty, as in UE.
        /// </summary>
        internal static float NormalizeLevel(GameplayEffectCue _Cue, float _Level)
        {
            float range = _Cue.maxLevel - _Cue.minLevel;
            if (range <= 0f) return 1f;

            return Mathf.Clamp01((_Level - _Cue.minLevel) / range);
        }
        #endregion



        #region Contribution and inhibition
        /// <summary>
        /// Start or stop an effect's contribution: its modifiers, unless periodic, and its granted tags.
        /// An inhibited effect keeps running its duration.
        /// </summary>
        private void SetInhibited(ActiveGameplayEffect _Effect, bool _Inhibited, bool _Initial = false)
        {
            if (!_Initial && _Effect.IsInhibited == _Inhibited) return;

            _Effect.IsInhibited = _Inhibited;
            if (_Inhibited)
            {
                if (!_Initial) RemoveContribution(_Effect);
            }
            else
            {
                AddContribution(_Effect);
                if (!_Initial) ApplyPeriodicInhibitionPolicy(_Effect);
            }

            if (_Initial) return;

            foreach (var component in _Effect.Def.Components)
                component?.OnInhibitionChanged(owner, _Effect, _Inhibited);

            owner.RaiseInhibitionChanged(_Effect.Handle, _Inhibited);
        }

        private void ApplyPeriodicInhibitionPolicy(ActiveGameplayEffect _Effect)
        {
            if (!_Effect.IsPeriodic || _Effect.IsRemoved) return;

            double now = World.Time;
            switch (_Effect.Def.PeriodicInhibitionPolicy)
            {
                case PeriodicInhibitionPolicy.NeverReset:
                    // Keep the schedule, without catching up the periods missed while inhibited.
                    int done = (int)Math.Floor((now - _Effect.PeriodStart) / _Effect.Spec.Period + EPSILON);
                    _Effect.PeriodsDone = Math.Max(_Effect.PeriodsDone, done);
                    break;

                case PeriodicInhibitionPolicy.ResetPeriod:
                    _Effect.PeriodStart = now;
                    _Effect.PeriodsDone = 0;
                    break;

                case PeriodicInhibitionPolicy.ExecuteAndResetPeriod:
                    ExecutePeriod(_Effect);
                    _Effect.PeriodStart = now;
                    _Effect.PeriodsDone = 0;
                    break;
            }
        }

        private void AddContribution(ActiveGameplayEffect _Effect)
        {
            var spec = _Effect.Spec;
            var previousCause = Attributes.Cause;
            Attributes.Cause = spec;
            try
            {
                // A periodic effect's modifiers never enter an aggregator: each period executes them.
                if (!_Effect.IsPeriodic)
                {
                    var modifiers = spec.Def.Modifiers;
                    for (int i = 0; i < modifiers.Count; i++)
                    {
                        var modifier = modifiers[i];
                        if (modifier == null) continue;

                        bool tagDependent = modifier.targetTags != null && !modifier.targetTags.IsEmpty;
                        bool needsCheck = tagDependent || (modifier.sourceTags != null && !modifier.sourceTags.IsEmpty);
                        Func<bool> qualifies = needsCheck ? () => Qualifies(modifier, _Effect.Spec) : null;

                        var handle = Attributes.AddModifier(modifier.attribute, modifier.op, spec.GetMagnitude(i, false), modifier.evaluationChannel,
                                                            spec.StackCount, qualifies);
                        _Effect.modifierHandles.Add((i, handle));
                        if (tagDependent) TagDependentModifiers++;
                    }
                }

                _Effect.grantedTags = spec.GetAllGrantedTags();
                owner.Tags.AddTags(_Effect.grantedTags);

                RaiseCues(spec, GameplayCueEvent.OnActive);
                RaiseCues(spec, GameplayCueEvent.WhileActive);

                foreach (var component in spec.Def.Components)
                {
                    if (component is BlockAbilityTagsComponent block)
                        owner.Abilities.BlockTags(block.tags);

                    if (component is not AbilitiesComponent grants) continue;

                    foreach (var grant in grants.abilities)
                    {
                        if (grant?.ability == null) continue;

                        var handle = owner.Abilities.Give(grant.ability, grant.level.At(spec.Level), grant.inputId, null, _Effect.Handle);
                        if (handle.IsValid)
                            _Effect.grantedAbilities.Add((handle, grant.removalPolicy));
                    }
                }
            }
            finally
            {
                Attributes.Cause = previousCause;
            }
        }

        private void RemoveContribution(ActiveGameplayEffect _Effect)
        {
            var previousCause = Attributes.Cause;
            Attributes.Cause = _Effect.Spec;
            try
            {
                var modifiers = _Effect.Spec.Def.Modifiers;
                foreach (var (index, handle) in _Effect.modifierHandles)
                {
                    Attributes.RemoveModifier(handle);
                    if (modifiers[index]?.targetTags != null && !modifiers[index].targetTags.IsEmpty)
                        TagDependentModifiers--;
                }

                _Effect.modifierHandles.Clear();
                owner.Tags.RemoveTags(_Effect.grantedTags);
                RaiseCues(_Effect.Spec, GameplayCueEvent.Removed);

                foreach (var component in _Effect.Def.Components)
                    if (component is BlockAbilityTagsComponent block)
                        owner.Abilities.UnblockTags(block.tags);

                foreach (var (handle, policy) in _Effect.grantedAbilities)
                {
                    switch (policy)
                    {
                        case GrantedAbilityRemovalPolicy.CancelAbilityImmediately:
                            owner.Abilities.Clear(handle);
                            break;

                        case GrantedAbilityRemovalPolicy.RemoveAbilityOnEnd:
                            owner.Abilities.SetRemoveOnEnd(handle);
                            break;

                        default:
                            var spec = owner.Abilities.Find(handle);
                            if (spec != null) spec.GrantedByEffect = ActiveEffectHandle.Invalid;
                            break;
                    }
                }

                _Effect.grantedAbilities.Clear();
            }
            finally
            {
                Attributes.Cause = previousCause;
            }
        }

        /// <summary>
        /// Give the aggregator the effect's current magnitudes and stack count: after a new stack, a removed stack, or
        /// a live capture change.
        /// </summary>
        private void UpdateModifiers(ActiveGameplayEffect _Effect)
        {
            var previousCause = Attributes.Cause;
            Attributes.Cause = _Effect.Spec;
            try
            {
                foreach (var (index, handle) in _Effect.modifierHandles)
                {
                    Attributes.SetModifierMagnitude(handle, _Effect.Spec.GetMagnitude(index, false));
                    Attributes.SetModifierStackCount(handle, _Effect.Spec.StackCount);
                }
            }
            finally
            {
                Attributes.Cause = previousCause;
            }
        }
        #endregion



        #region Requirement re-checks
        public bool HasPendingRechecks => pendingRechecks.Count > 0;

        /// <summary>
        /// The unit's tags changed: every effect with ongoing or removal requirements is checked again in the next
        /// propagation pass.
        /// </summary>
        public void QueueRechecks()
        {
            foreach (var effect in effects.Snapshot())
                if (!pendingRechecks.Contains(effect) && effect.Def.FindComponent<TargetTagRequirementsComponent>() is { NeedsRechecks: true })
                    pendingRechecks.Add(effect);
        }

        /// <summary>
        /// One pass of re-checks: removal requirements that match remove the effect; ongoing requirements inhibit or
        /// release it.
        /// </summary>
        public void ProcessRechecks()
        {
            var batch = pendingRechecks.ToList();
            pendingRechecks.Clear();

            effects.Lock();
            try
            {
                foreach (var effect in batch)
                {
                    if (effect.IsRemoved || owner.IsShuttingDown) continue;

                    var requirements = effect.Def.FindComponent<TargetTagRequirementsComponent>();
                    if (requirements == null) continue;

                    if (requirements.IsRemovalMet(owner)) Remove(effect, true);
                    else SetInhibited(effect, !requirements.IsOngoingMet(owner));
                }
            }
            finally
            {
                effects.Unlock();
            }
        }

        /// <summary>
        /// The propagation pass limit stopped the re-checks: effects still waiting for one stay inhibited, so a
        /// flip-flop ends switched off.
        /// </summary>
        public string StopRechecks()
        {
            var batch = pendingRechecks.ToList();
            pendingRechecks.Clear();

            foreach (var effect in batch)
                if (!effect.IsRemoved)
                    SetInhibited(effect, true);

            pendingRechecks.Clear();
            return string.Join(", ", batch.Select(e => $"\"{e.Def.name}\"").Distinct());
        }
        #endregion



        #region Live captures
        /// <summary>
        /// Follow the effect's live captures: when a captured attribute changes, its magnitudes are calculated again.
        /// </summary>
        private void Link(ActiveGameplayEffect _Effect)
        {
            foreach (var captured in _Effect.Spec.LiveCaptures)
            {
                var unit = captured.Unit;
                var attribute = captured.Definition.attribute;

                void OnPropagated(GameplayAttribute _Attribute, float _Old, float _New)
                {
                    if (_Attribute != attribute || _Effect.IsRemoved || owner.IsShuttingDown) return;
                    Recalculate(_Effect);
                }

                unit.Attributes.Propagated += OnPropagated;
                _Effect.unlinks.Add(() => unit.Attributes.Propagated -= OnPropagated);
            }
        }

        private static void Unlink(ActiveGameplayEffect _Effect)
        {
            foreach (var unlink in _Effect.unlinks)
                unlink();

            _Effect.unlinks.Clear();
        }

        private void Recalculate(ActiveGameplayEffect _Effect)
        {
            _Effect.Spec.CalculateModifierMagnitudes();
            if (!_Effect.IsInhibited && !_Effect.IsPeriodic)
                UpdateModifiers(_Effect);
        }
        #endregion



        #region Ticking
        /// <summary>
        /// Run every effect's due periods and expiry at world time <paramref name="_Now"/>. Periods are
        /// start plus n periods, so they never drift; missed ones are caught up in order, never past the end; a period
        /// on the end time executes before the effect expires.
        /// </summary>
        public void Tick(double _Now)
        {
            effects.Lock();
            try
            {
                effects.ForEach(effect =>
                {
                    if (owner.IsShuttingDown || effect.IsRemoved) return;

                    if (effect.IsPeriodic && !effect.IsInhibited)
                    {
                        while (true)
                        {
                            double due = effect.PeriodStart + (effect.PeriodsDone + 1) * effect.Spec.Period;
                            if (_Now < due - EPSILON || (effect.HasDuration && due > effect.EndTime + EPSILON)) break;

                            effect.PeriodsDone++;
                            ExecutePeriod(effect);
                            if (effect.IsRemoved || owner.IsShuttingDown) return;
                        }
                    }

                    if (effect.HasDuration && _Now >= effect.EndTime - EPSILON)
                        OnDurationExpired(effect, _Now);
                });
            }
            finally
            {
                effects.Unlock();
            }
        }
        #endregion



        #region Removal
        /// <summary>
        /// Remove stacks of an active effect: fewer than it has only lowers the count.
        /// </summary>
        /// <param name="_Stacks"> How many stacks; 0 or less for all of them. </param>
        public bool Remove(ActiveEffectHandle _Handle, int _Stacks)
        {
            var effect = Find(_Handle);
            if (effect == null) return false;

            if (_Stacks > 0 && _Stacks < effect.StackCount)
            {
                SetStackCount(effect, effect.StackCount - _Stacks);
                return true;
            }

            Remove(effect, true);
            return true;
        }

        /// <summary>
        /// Remove every active effect that matches <paramref name="_Query"/>, as premature removals.
        /// </summary>
        /// <param name="_ExceptCurrent"> Leave the effect being applied right now. </param>
        public int Remove(GameplayEffectQuery _Query, bool _ExceptCurrent)
        {
            if (_Query == null) return 0;

            int count = 0;
            effects.Lock();
            try
            {
                foreach (var effect in effects.Snapshot())
                {
                    if (_ExceptCurrent && effect.Handle == applying) continue;
                    if (effect.IsRemoved || !_Query.Matches(effect.Spec)) continue;

                    Remove(effect, true);
                    count++;
                }
            }
            finally
            {
                effects.Unlock();
            }

            return count;
        }

        /// <summary>
        /// End an effect: its handle is dead at once, its contribution is withdrawn, and its slot goes now or when the
        /// list unlocks.
        /// </summary>
        public void Remove(ActiveGameplayEffect _Effect, bool _Premature)
        {
            if (_Effect.IsRemoved) return;

            _Effect.IsRemoved = true;
            if (!_Effect.IsInhibited)
                RemoveContribution(_Effect);

            Unlink(_Effect);
            pendingRechecks.Remove(_Effect);

            var info = new RemovalInfo(_Premature, _Effect.StackCount);
            foreach (var component in _Effect.Def.Components)
                component?.OnRemoved(owner, _Effect, info);

            owner.RaiseEffectRemoved(_Effect, info);
            effects.Erase(_Effect);
        }

        /// <summary>
        /// Drop every effect without running completion effects or events: the unit is shutting down. Live links that
        /// other units hold on this one freeze at their last values.
        /// </summary>
        public void Clear()
        {
            foreach (var effect in effects.Snapshot())
            {
                effect.IsRemoved = true;
                Unlink(effect);
            }

            effects.Clear();
            pendingRechecks.Clear();
            TagDependentModifiers = 0;
        }
        #endregion
    }
}
