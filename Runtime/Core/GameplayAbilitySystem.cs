using System;
using System.Collections.Generic;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// One unit's gameplay state, the counterpart of UE's Ability System Component: its attributes, active effects,
    /// granted abilities and tag counts. Plain C#, created by an <see cref="AbilitySystemWorld"/>.
    /// </summary>
    /// <remarks>
    /// State changes only through the mutators below. Each one first checks that the unit isn't shutting
    /// down and that the world's net driver allows it, then acts, and flushes queued attribute changes before the
    /// outermost one returns. Callbacks may call mutators again: those run at once, inside the outer one. <br/>
    /// After <see cref="Shutdown"/>, every mutator does nothing and returns false.
    /// </remarks>
    public sealed class GameplayAbilitySystem
    {
        private readonly HashSet<object> onceKeys = new();
        private int mutationDepth;

        public AbilitySystemWorld World { get; }

        /// <summary>
        /// A name for logs and the debugger.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Whether the unit is shut down, or shutting down. Mutators then do nothing.
        /// </summary>
        public bool IsShuttingDown { get; private set; }

        internal AttributeContainer Attributes { get; }

        internal ActiveEffectsContainer Effects { get; }

        internal AbilityContainer Abilities { get; }

        /// <summary>
        /// The unit's owner and avatar. Set with <see cref="InitActorInfo"/>.
        /// </summary>
        public AbilityActorInfo ActorInfo { get; } = new();

        /// <summary>
        /// The unit's tags: loose tags and tags granted by active effects, reference-counted. Read-only here; change
        /// loose tags with <see cref="AddLooseTag"/> and <see cref="RemoveLooseTag"/>.
        /// </summary>
        public TagCountContainer Tags { get; } = new();

        /// <summary>
        /// Called once per actual change of a current value, at once. Never for meta attributes.
        /// </summary>
        public event Action<AttributeChange> OnAttributeChanged;

        /// <summary>
        /// Called once per attribute set added, after its attributes are initialised and consistent.
        /// </summary>
        public event Action<AttributeSetDefinition> OnAttributesInitialized;

        /// <summary>
        /// Called for every gameplay event the unit handles.
        /// </summary>
        public event Action<GameplayTag, GameplayEventData> OnGameplayEvent;



        internal GameplayAbilitySystem(AbilitySystemWorld _World, string _Name)
        {
            World = _World;
            Name = string.IsNullOrEmpty(_Name) ? "Unit" : _Name;
            Attributes = new AttributeContainer(this);
            Effects = new ActiveEffectsContainer(this);
            Abilities = new AbilityContainer(this);
            Tags.Changed += OnTagsChanged;
        }

        // Modifier qualifications may depend on the unit's tags.
        private void OnTagsChanged()
        {
            if (IsShuttingDown) return;

            Effects.QueueRechecks();
            if (Effects.TagDependentModifiers > 0)
                Attributes.ReevaluateAll();
        }

        public override string ToString() => Name;



        #region Mutation
        /// <summary>
        /// Start a mutation: false, and nothing to end, if the unit is shutting down or the net driver refuses.
        /// </summary>
        internal bool BeginMutation()
        {
            if (IsShuttingDown || !World.NetDriver.HasAuthority(this)) return false;

            mutationDepth++;
            return true;
        }

        /// <summary>
        /// End a mutation. The outermost one flushes the queued attribute changes.
        /// </summary>
        internal void EndMutation()
        {
            if (--mutationDepth == 0 && !IsShuttingDown)
                FlushPropagation();
        }

        internal bool IsMutating => mutationDepth > 0;

        /// <summary>
        /// Work through queued attribute changes. Mutators do it themselves; the world does it once more per step.
        /// </summary>
        internal void FlushPropagation()
        {
            if (flushing) return;

            flushing = true;
            try
            {
                int pass = 0;
                while (Attributes.HasPendingPropagation || Effects.HasPendingRechecks)
                {
                    if (++pass > World.Settings.MaxPropagationPasses)
                    {
                        string attributes = Attributes.ClearPropagation();
                        string effects = Effects.StopRechecks();
                        Attributes.ClearPropagation();
                        UnityEngine.Debug.LogError($"[{Name}] Attribute cycle: changes were still propagating after {World.Settings.MaxPropagationPasses} passes, " +
                                                   $"the propagation pass limit (attributes: {attributes}; effects re-checking their requirements: {effects}). " +
                                                   "They stop here: values stay as last computed, and those effects stay inhibited.");
                        break;
                    }

                    Effects.ProcessRechecks();
                    if (IsShuttingDown) break;
                    Attributes.PropagateOnce();
                }
            }
            finally
            {
                flushing = false;
            }
        }

        private bool flushing;

        /// <summary>
        /// One world step: the time after it, and its length.
        /// </summary>
        internal void Tick(double _Time, double _DeltaTime)
        {
            if (!BeginMutation()) return;
            try
            {
                Effects.Tick(_Time);
            }
            finally
            {
                EndMutation();
            }
        }

        /// <summary>
        /// The second half of a world step: the running ability tasks that tick.
        /// </summary>
        internal void TickTasks(double _Time, double _DeltaTime)
        {
            if (!BeginMutation()) return;
            try
            {
                Abilities.TickTasks(_Time, _DeltaTime);
            }
            finally
            {
                EndMutation();
            }
        }

        /// <summary>
        /// Shut the unit down: every later mutator does nothing and returns false, and the world stops ticking it.
        /// Called when its host is destroyed, and by the world when it is disposed.
        /// </summary>
        public void Shutdown()
        {
            if (IsShuttingDown) return;

            IsShuttingDown = true;
            Abilities.Clear();
            Effects.Clear();
            gameplayEventListeners.Clear();
            looseCues.Clear();
            World.Cues.ClearTarget(this);
            World.Unregister(this);
        }
        #endregion



        #region Attributes
        /// <summary>
        /// The unit's attribute sets, in the order they were added.
        /// </summary>
        public IReadOnlyList<AttributeSetDefinition> AttributeSets => Attributes.Sets;

        public bool HasAttribute(GameplayAttribute _Attribute) => Attributes.Has(_Attribute);

        /// <summary>
        /// The attributes the unit has, from its sets.
        /// </summary>
        public IEnumerable<GameplayAttribute> GetAttributes() => Attributes.Attributes;

        /// <summary>
        /// The base value, or 0 if the unit lacks the attribute.
        /// </summary>
        public float GetBase(GameplayAttribute _Attribute) => Attributes.GetBase(_Attribute);

        /// <summary>
        /// The current value, or 0 if the unit lacks the attribute.
        /// </summary>
        public float GetCurrent(GameplayAttribute _Attribute) => Attributes.GetCurrent(_Attribute);

        /// <summary>
        /// Give the unit a set's attributes and behaviours, at <paramref name="_Level"/> for initial values that scale.
        /// An attribute the unit already has keeps its values.
        /// </summary>
        /// <returns> Whether the set was added: false if it already was, or the unit can't be changed. </returns>
        public bool AddAttributeSet(AttributeSetDefinition _Set, float _Level = 1f)
        {
            if (_Set == null || !BeginMutation()) return false;
            try
            {
                return Attributes.AddSet(_Set, _Level);
            }
            finally
            {
                EndMutation();
            }
        }

        /// <summary>
        /// Remove a set. Attributes that no other set lists go; modifiers on them wait for them to come back.
        /// </summary>
        public bool RemoveAttributeSet(AttributeSetDefinition _Set)
        {
            if (_Set == null || !BeginMutation()) return false;
            try
            {
                return Attributes.RemoveSet(_Set);
            }
            finally
            {
                EndMutation();
            }
        }

        /// <summary>
        /// Write a base value directly, through <c>PreBaseChange</c> behaviours and clamping. For initialisation, cheats
        /// and save loading; gameplay changes values with effects.
        /// </summary>
        /// <returns> Whether the value was written: false if the unit lacks the attribute, or the value isn't finite. </returns>
        public bool SetAttributeBase(GameplayAttribute _Attribute, float _Value)
        {
            if (!BeginMutation()) return false;
            try
            {
                return Attributes.SetBase(_Attribute, _Value);
            }
            finally
            {
                EndMutation();
            }
        }
        #endregion



        #region Modifiers (internal: the effect system's)
        /// <summary>
        /// Execute one modifier on a base value, as an instant effect does.
        /// </summary>
        internal bool ExecuteModifier(GameplayAttribute _Attribute, ModifierOp _Op, float _Magnitude)
        {
            if (!BeginMutation()) return false;
            try
            {
                return Attributes.ExecuteModifier(_Attribute, _Op, _Magnitude);
            }
            finally
            {
                EndMutation();
            }
        }

        /// <summary>
        /// Add a modifier to the attribute's aggregator, as a duration or infinite effect does.
        /// </summary>
        /// <param name="_Qualifies"> Whether the modifier counts now; <see langword="null"/> for always. </param>
        internal ModifierHandle AddModifier(GameplayAttribute _Attribute, ModifierOp _Op, float _Magnitude, int _Channel = 0, int _StackCount = 1,
                                            Func<bool> _Qualifies = null)
        {
            if (!BeginMutation()) return ModifierHandle.Invalid;
            try
            {
                return Attributes.AddModifier(_Attribute, _Op, _Magnitude, _Channel, _StackCount, _Qualifies);
            }
            finally
            {
                EndMutation();
            }
        }

        internal bool RemoveModifier(ModifierHandle _Handle) => Mutate(() => Attributes.RemoveModifier(_Handle));

        internal bool SetModifierMagnitude(ModifierHandle _Handle, float _Magnitude) => Mutate(() => Attributes.SetModifierMagnitude(_Handle, _Magnitude));

        internal bool SetModifierStackCount(ModifierHandle _Handle, int _StackCount) => Mutate(() => Attributes.SetModifierStackCount(_Handle, _StackCount));

        /// <summary>
        /// Evaluate every attribute again, after modifier qualifications may have changed.
        /// </summary>
        internal bool ReevaluateAttributes() => Mutate(() =>
        {
            Attributes.ReevaluateAll();
            return true;
        });

        private bool Mutate(Func<bool> _Change)
        {
            if (!BeginMutation()) return false;
            try
            {
                return _Change();
            }
            finally
            {
                EndMutation();
            }
        }
        #endregion



        #region Effects
        /// <summary>
        /// Called after an effect was applied to this unit: the applied spec, and the active effect's handle or
        /// <see cref="ActiveEffectHandle.Invalid"/> for an instant execution.
        /// </summary>
        public event Action<GameplayEffectSpec, ActiveEffectHandle> OnEffectAppliedToSelf;

        /// <summary>
        /// Called on the instigator after an effect it caused was applied: the target, the applied spec, and the handle.
        /// </summary>
        public event Action<GameplayAbilitySystem, GameplayEffectSpec, ActiveEffectHandle> OnEffectAppliedToTarget;

        /// <summary>
        /// Called after an active effect ended.
        /// </summary>
        public event Action<ActiveGameplayEffect, RemovalInfo> OnEffectRemoved;

        /// <summary>
        /// Called after a stack's count changed: its handle, the old count and the new count.
        /// </summary>
        public event Action<ActiveEffectHandle, int, int> OnStackChanged;

        /// <summary>
        /// Called when a stack's duration ran out under the <see cref="StackExpirationPolicy.RefreshDuration"/> policy:
        /// the effect stays, and game code decides.
        /// </summary>
        public event Action<ActiveEffectHandle> OnStackExpired;

        /// <summary>
        /// Called after an active effect became inhibited (true) or was released (false).
        /// </summary>
        public event Action<ActiveEffectHandle, bool> OnInhibitionChanged;

        /// <summary>
        /// Called when an incoming effect was blocked: the refused spec, and the active effect whose immunity blocked it.
        /// </summary>
        public event Action<GameplayEffectSpec, ActiveGameplayEffect> OnImmunityBlocked;

        /// <summary>
        /// A spec of <paramref name="_Effect"/> with this unit as its instigator, and this unit's tags captured as the
        /// source tags. Changes nothing.
        /// </summary>
        /// <param name="_Context"> <see langword="null"/> for a new context with this unit as instigator. </param>
        public GameplayEffectSpec MakeOutgoingSpec(GameplayEffect _Effect, float _Level = 1f, GameplayEffectContext _Context = null)
        {
            if (_Effect == null) throw new ArgumentNullException(nameof(_Effect));

            var spec = new GameplayEffectSpec(_Effect, _Level, _Context ?? new GameplayEffectContext(this));
            spec.CaptureSourceTags(Tags.Snapshot());
            spec.CaptureSourceAttributes(this);
            return spec;
        }

        /// <summary>
        /// Apply a copy of a spec to this unit.
        /// </summary>
        /// <returns> The active effect's handle; <see cref="ActiveEffectHandle.Invalid"/> for an instant execution, or a refused application. </returns>
        public ActiveEffectHandle ApplySpecToSelf(GameplayEffectSpec _Spec)
        {
            if (_Spec == null || !BeginMutation()) return ActiveEffectHandle.Invalid;
            try
            {
                if (!World.NetDriver.CanApplyEffects(this)) return ActiveEffectHandle.Invalid;

                return Effects.Apply(_Spec);
            }
            finally
            {
                EndMutation();
            }
        }

        /// <summary>
        /// Apply a copy of a spec, usually made by this unit, to <paramref name="_Target"/>.
        /// </summary>
        public ActiveEffectHandle ApplySpecToTarget(GameplayEffectSpec _Spec, GameplayAbilitySystem _Target)
            => _Target != null && !IsShuttingDown ? _Target.ApplySpecToSelf(_Spec) : ActiveEffectHandle.Invalid;

        /// <summary>
        /// Apply an effect to this unit, at a level, with this unit as its instigator.
        /// </summary>
        public ActiveEffectHandle ApplyEffectToSelf(GameplayEffect _Effect, float _Level = 1f)
            => _Effect != null && !IsShuttingDown ? ApplySpecToSelf(MakeOutgoingSpec(_Effect, _Level)) : ActiveEffectHandle.Invalid;

        /// <summary>
        /// Remove an active effect, or some of its stacks.
        /// </summary>
        /// <param name="_Stacks"> How many stacks to remove; 0 or less for the whole effect. </param>
        /// <returns> Whether the handle found a live effect. </returns>
        public bool RemoveActiveEffect(ActiveEffectHandle _Handle, int _Stacks = -1) => Mutate(() => Effects.Remove(_Handle, _Stacks));

        /// <summary>
        /// Remove every active effect that matches the query, as premature removals.
        /// </summary>
        /// <returns> How many were removed. </returns>
        public int RemoveActiveEffects(GameplayEffectQuery _Query)
        {
            if (_Query == null || !BeginMutation()) return 0;
            try
            {
                return Effects.Remove(_Query, false);
            }
            finally
            {
                EndMutation();
            }
        }

        /// <summary>
        /// <see cref="RemoveActiveEffects"/>, leaving the effect being applied: for its own RemoveOther component.
        /// </summary>
        internal int RemoveActiveEffectsExceptCurrent(GameplayEffectQuery _Query)
        {
            if (_Query == null || !BeginMutation()) return 0;
            try
            {
                return Effects.Remove(_Query, true);
            }
            finally
            {
                EndMutation();
            }
        }

        /// <summary>
        /// Every live active effect matching the query, as a new list.
        /// </summary>
        public IReadOnlyList<ActiveGameplayEffect> GetActiveEffects(GameplayEffectQuery _Query)
            => _Query == null ? Array.Empty<ActiveGameplayEffect>() : Effects.Snapshot().FindAll(e => _Query.Matches(e.Spec));

        /// <summary>
        /// The live active effect with this handle, or <see langword="null"/>. Read it; don't keep it.
        /// </summary>
        public ActiveGameplayEffect GetActiveEffect(ActiveEffectHandle _Handle) => Effects.Find(_Handle);

        /// <summary>
        /// Every live active effect, in application order, as a new list.
        /// </summary>
        public IReadOnlyList<ActiveGameplayEffect> GetActiveEffects() => Effects.Snapshot();

        internal void RaiseEffectAppliedToSelf(GameplayEffectSpec _Spec, ActiveEffectHandle _Handle) => OnEffectAppliedToSelf?.Invoke(_Spec, _Handle);

        internal void RaiseEffectAppliedToTarget(GameplayAbilitySystem _Target, GameplayEffectSpec _Spec, ActiveEffectHandle _Handle)
            => OnEffectAppliedToTarget?.Invoke(_Target, _Spec, _Handle);

        internal void RaiseEffectRemoved(ActiveGameplayEffect _Effect, RemovalInfo _Info) => OnEffectRemoved?.Invoke(_Effect, _Info);

        internal void RaiseStackChanged(ActiveEffectHandle _Handle, int _Old, int _New) => OnStackChanged?.Invoke(_Handle, _Old, _New);

        internal void RaiseStackExpired(ActiveEffectHandle _Handle) => OnStackExpired?.Invoke(_Handle);

        internal void RaiseInhibitionChanged(ActiveEffectHandle _Handle, bool _Inhibited) => OnInhibitionChanged?.Invoke(_Handle, _Inhibited);

        internal void RaiseImmunityBlocked(GameplayEffectSpec _Spec, ActiveGameplayEffect _Blocker) => OnImmunityBlocked?.Invoke(_Spec, _Blocker);
        #endregion



        #region Abilities
        public event Action<AbilitySpec> OnAbilityGiven;

        public event Action<AbilitySpec> OnAbilityRemoved;

        public event Action<AbilitySpec> OnAbilityActivated;

        /// <summary>
        /// Called once per activation that ended: the spec, and whether it was cancelled.
        /// </summary>
        public event Action<AbilitySpec, bool> OnAbilityEnded;

        /// <summary>
        /// Called when an activation was refused by a check: the ability, and why.
        /// </summary>
        public event Action<GameplayAbility, ActivationFailure> OnAbilityFailed;

        /// <summary>
        /// Set the unit's owner and avatar. Called again when the avatar changes: running abilities are
        /// cancelled and their tasks end; attributes, effects, tags and granted abilities persist.
        /// </summary>
        public bool InitActorInfo(object _Owner, object _Avatar)
        {
            if (!BeginMutation()) return false;
            try
            {
                var previousAvatar = ActorInfo.Avatar;
                ActorInfo.Owner = _Owner;
                ActorInfo.Avatar = _Avatar;

                if (previousAvatar != null && !Equals(previousAvatar, _Avatar))
                    Abilities.OnAvatarChanged();

                return true;
            }
            finally
            {
                EndMutation();
            }
        }

        /// <summary>
        /// Grant an ability.
        /// </summary>
        /// <param name="_InputId"> The input the game maps to it, or -1. </param>
        /// <returns> The spec's handle, or <see cref="AbilitySpecHandle.Invalid"/> if the unit can't be changed. </returns>
        public AbilitySpecHandle GiveAbility(GameplayAbility _Ability, float _Level = 1f, int _InputId = -1, UnityEngine.Object _SourceObject = null)
        {
            if (_Ability == null || !BeginMutation()) return AbilitySpecHandle.Invalid;
            try
            {
                return Abilities.Give(_Ability, _Level, _InputId, _SourceObject, ActiveEffectHandle.Invalid);
            }
            finally
            {
                EndMutation();
            }
        }

        /// <summary>
        /// Grant an ability, activate it, and remove it when that activation ends. Removed at once if it can't activate.
        /// </summary>
        public AbilitySpecHandle GiveAbilityAndActivateOnce(GameplayAbility _Ability, float _Level = 1f, GameplayEventData? _Trigger = null)
        {
            if (_Ability == null || !BeginMutation()) return AbilitySpecHandle.Invalid;
            try
            {
                var handle = Abilities.Give(_Ability, _Level, -1, null, ActiveEffectHandle.Invalid);
                var spec = Abilities.Find(handle);
                if (spec == null) return AbilitySpecHandle.Invalid;

                spec.RemoveAfterActivation = true;
                if (!Abilities.TryActivate(handle, _Trigger) || !spec.IsActive)
                    Abilities.SetRemoveOnEnd(handle);

                return handle;
            }
            finally
            {
                EndMutation();
            }
        }

        /// <summary>
        /// Remove an ability. It can't activate from now on; if active it is cancelled, with force.
        /// </summary>
        public bool ClearAbility(AbilitySpecHandle _Handle) => Mutate(() => Abilities.Clear(_Handle));

        public bool ClearAllAbilities() => Mutate(() =>
        {
            foreach (var spec in Abilities.Snapshot())
                Abilities.Clear(spec.Handle);
            return true;
        });

        /// <summary>
        /// Remove an ability when its current activation ends, or now if it isn't active.
        /// </summary>
        public bool SetRemoveAbilityOnEnd(AbilitySpecHandle _Handle) => Mutate(() => Abilities.SetRemoveOnEnd(_Handle));

        /// <summary>
        /// Try to activate an ability.
        /// </summary>
        /// <param name="_Trigger"> The gameplay event that triggers it, if any: its source and target are checked. </param>
        public bool TryActivateAbility(AbilitySpecHandle _Handle, GameplayEventData? _Trigger = null) => Mutate(() => Abilities.TryActivate(_Handle, _Trigger));

        /// <summary>
        /// Try to activate every granted ability with any of the tags. True if one did.
        /// </summary>
        public bool TryActivateAbilitiesByTag(GameplayTagContainer _Tags) => Mutate(() =>
        {
            bool any = false;
            foreach (var spec in Abilities.Snapshot())
                if (spec.GetAllTags().HasAny(_Tags) && Abilities.TryActivate(spec.Handle, null))
                    any = true;
            return any;
        });

        /// <summary>
        /// Cancel every running activation of an ability that allows it.
        /// </summary>
        public bool CancelAbility(AbilitySpecHandle _Handle) => Mutate(() =>
        {
            var spec = Abilities.Find(_Handle);
            if (spec == null) return false;

            Abilities.Cancel(spec, false);
            return true;
        });

        /// <summary>
        /// Cancel the active abilities with any of <paramref name="_WithTags"/> (all of them when <see langword="null"/>)
        /// and none of <paramref name="_WithoutTags"/>, except <paramref name="_Ignore"/>.
        /// </summary>
        public bool CancelAbilities(GameplayTagContainer _WithTags = null, GameplayTagContainer _WithoutTags = null, AbilityInstance _Ignore = null)
            => Mutate(() =>
            {
                Abilities.CancelAbilities(_WithTags, _WithoutTags, _Ignore);
                return true;
            });

        /// <summary>
        /// The game's input action <paramref name="_InputId"/> was pressed: inactive abilities with that input
        /// activate; active ones are told.
        /// </summary>
        public bool AbilityInputPressed(int _InputId) => Mutate(() =>
        {
            Abilities.InputPressed(_InputId);
            return true;
        });

        public bool AbilityInputReleased(int _InputId) => Mutate(() =>
        {
            Abilities.InputReleased(_InputId);
            return true;
        });

        public AbilitySpec FindAbilitySpec(AbilitySpecHandle _Handle) => Abilities.Find(_Handle);

        /// <summary>
        /// The first granted spec of an ability asset, or <see langword="null"/>.
        /// </summary>
        public AbilitySpec FindAbility(GameplayAbility _Ability) => Abilities.Find(_Ability);

        /// <summary>
        /// Every granted ability, as a new list.
        /// </summary>
        public IReadOnlyList<AbilitySpec> GetActivatableAbilities() => Abilities.Snapshot();

        /// <summary>
        /// Whether an ability may activate now, and why not. Changes nothing.
        /// </summary>
        public bool CanActivateAbility(AbilitySpecHandle _Handle, out ActivationFailure _Failure)
        {
            var spec = Abilities.Find(_Handle);
            if (spec == null || IsShuttingDown)
            {
                _Failure = new ActivationFailure();
                return false;
            }

            return Abilities.CanActivate(spec, null, out _Failure);
        }

        /// <summary>
        /// Seconds left on an ability's cooldown: the longest time remaining among active effects that grant any of its
        /// cooldown tags. 0 when it is ready.
        /// </summary>
        public double GetCooldownRemaining(GameplayAbility _Ability)
        {
            if (_Ability == null) return 0;

            var tags = _Ability.GetCooldownTags();
            if (tags.IsEmpty) return 0;

            double remaining = 0;
            foreach (var effect in Effects.Snapshot())
                if (!effect.IsInhibited && effect.grantedTags.HasAny(tags))
                    remaining = Math.Max(remaining, effect.TimeRemaining());

            return remaining;
        }

        internal void RaiseAbilityGiven(AbilitySpec _Spec) => OnAbilityGiven?.Invoke(_Spec);

        internal void RaiseAbilityRemoved(AbilitySpec _Spec) => OnAbilityRemoved?.Invoke(_Spec);

        internal void RaiseAbilityActivated(AbilitySpec _Spec) => OnAbilityActivated?.Invoke(_Spec);

        internal void RaiseAbilityEnded(AbilitySpec _Spec, bool _WasCancelled) => OnAbilityEnded?.Invoke(_Spec, _WasCancelled);

        internal void RaiseAbilityFailed(GameplayAbility _Ability, ActivationFailure _Failure) => OnAbilityFailed?.Invoke(_Ability, _Failure);
        #endregion



        #region Tags
        public bool HasTag(GameplayTag _Tag) => Tags.HasTag(_Tag);

        public bool HasTagExact(GameplayTag _Tag) => Tags.HasTagExact(_Tag);

        public bool HasAll(GameplayTagContainer _Tags) => Tags.HasAll(_Tags);

        public bool HasAny(GameplayTagContainer _Tags) => Tags.HasAny(_Tags);

        public int GetTagCount(GameplayTag _Tag) => Tags.GetTagCount(_Tag);

        /// <summary>
        /// Call <paramref name="_Callback"/> when the count of <paramref name="_Tag"/> changes. Dispose the result to stop.
        /// </summary>
        public IDisposable RegisterTagEvent(GameplayTag _Tag, TagEventType _Type, Action<GameplayTag, int> _Callback)
            => Tags.RegisterTagEvent(_Tag, _Type, _Callback);

        /// <summary>
        /// Add a tag owned by no effect or ability.
        /// </summary>
        public bool AddLooseTag(GameplayTag _Tag, int _Count = 1) => Mutate(() =>
        {
            Tags.Add(_Tag, _Count);
            return true;
        });

        /// <summary>
        /// Remove a loose tag added with <see cref="AddLooseTag"/>.
        /// </summary>
        public bool RemoveLooseTag(GameplayTag _Tag, int _Count = 1) => Mutate(() =>
        {
            Tags.Remove(_Tag, _Count);
            return true;
        });
        #endregion



        #region Cues
        private readonly Dictionary<GameplayTag, int> looseCues = new();

        /// <summary>
        /// Raise a one-shot cue on this unit, shown when the step ends. Cues are presentation: nothing in
        /// gameplay reads them.
        /// </summary>
        public void ExecuteCue(GameplayTag _Cue, GameplayCueParameters _Parameters = null)
        {
            if (IsShuttingDown || _Cue.IsNone) return;
            World.Cues.Enqueue(this, _Cue, GameplayCueEvent.Executed, PrepareCueParameters(_Cue, _Parameters));
        }

        /// <summary>
        /// Start a lasting cue owned by no effect: counted, so it is removed when every <see cref="AddCue"/> was matched
        /// by a <see cref="RemoveCue"/>.
        /// </summary>
        public void AddCue(GameplayTag _Cue, GameplayCueParameters _Parameters = null)
        {
            if (IsShuttingDown || _Cue.IsNone) return;

            looseCues.TryGetValue(_Cue, out int count);
            looseCues[_Cue] = count + 1;
            if (count > 0) return;

            var parameters = PrepareCueParameters(_Cue, _Parameters);
            World.Cues.Enqueue(this, _Cue, GameplayCueEvent.OnActive, parameters);
            World.Cues.Enqueue(this, _Cue, GameplayCueEvent.WhileActive, parameters);
        }

        /// <summary>
        /// Undo one <see cref="AddCue"/>.
        /// </summary>
        public void RemoveCue(GameplayTag _Cue, GameplayCueParameters _Parameters = null)
        {
            if (IsShuttingDown || !looseCues.TryGetValue(_Cue, out int count)) return;

            if (count > 1)
            {
                looseCues[_Cue] = count - 1;
                return;
            }

            looseCues.Remove(_Cue);
            World.Cues.Enqueue(this, _Cue, GameplayCueEvent.Removed, PrepareCueParameters(_Cue, _Parameters));
        }

        /// <summary>
        /// Whether a cue added with <see cref="AddCue"/> is still active.
        /// </summary>
        public bool HasCue(GameplayTag _Cue) => looseCues.ContainsKey(_Cue);

        /// <summary>
        /// The tags of the cues added with <see cref="AddCue"/> and still active.
        /// </summary>
        public IEnumerable<GameplayTag> LooseCues => looseCues.Keys;

        private GameplayCueParameters PrepareCueParameters(GameplayTag _Cue, GameplayCueParameters _Parameters)
        {
            var parameters = _Parameters?.Copy() ?? new GameplayCueParameters();
            parameters.OriginalTag = _Cue;
            parameters.MatchedTag = _Cue;
            parameters.Instigator ??= this;
            if (parameters.TargetTags.IsEmpty)
                parameters.TargetTags = Tags.Snapshot();

            return parameters;
        }
        #endregion



        #region Events
        private readonly List<(GameplayTag Tag, Action<GameplayEventData> Callback)> gameplayEventListeners = new();

        /// <summary>
        /// Call <paramref name="_Callback"/> for every gameplay event this unit handles whose tag is
        /// <paramref name="_Tag"/> or a descendant of it. Dispose the result to stop.
        /// </summary>
        public IDisposable RegisterGameplayEventListener(GameplayTag _Tag, Action<GameplayEventData> _Callback)
        {
            if (_Callback == null) throw new ArgumentNullException(nameof(_Callback));

            var entry = (_Tag, _Callback);
            gameplayEventListeners.Add(entry);
            return new Disposer(() => gameplayEventListeners.Remove(entry));
        }

        /// <summary>
        /// Handle a gameplay event sent to this unit: listeners whose tag the event tag matches, then
        /// <see cref="OnGameplayEvent"/>, at once.
        /// </summary>
        /// <returns> The number of abilities the event triggered. </returns>
        public int HandleGameplayEvent(GameplayTag _EventTag, GameplayEventData _Data)
        {
            if (!BeginMutation()) return 0;
            try
            {
                int activated = Abilities.HandleGameplayEvent(_EventTag, _Data);

                foreach (var (tag, callback) in gameplayEventListeners.ToArray())
                {
                    if (!_EventTag.MatchesTag(tag)) continue;

                    try
                    {
                        callback(_Data);
                    }
                    catch (Exception exception)
                    {
                        UnityEngine.Debug.LogException(exception);
                    }
                }

                OnGameplayEvent?.Invoke(_EventTag, _Data);
                return activated;
            }
            finally
            {
                EndMutation();
            }
        }

        /// <summary>
        /// Per-unit memory for shared objects such as attribute behaviours: true the first time <paramref name="_Key"/>
        /// is marked on this unit, false after.
        /// </summary>
        public bool TryMarkOnce(object _Key) => _Key != null && onceKeys.Add(_Key);

        internal void RaiseAttributeChanged(AttributeChange _Change) => OnAttributeChanged?.Invoke(_Change);

        internal void RaiseAttributesInitialized(AttributeSetDefinition _Set) => OnAttributesInitialized?.Invoke(_Set);

        private sealed class Disposer : IDisposable
        {
            private Action dispose;

            public Disposer(Action _Dispose) => dispose = _Dispose;

            public void Dispose()
            {
                dispose?.Invoke();
                dispose = null;
            }
        }
        #endregion
    }
}
