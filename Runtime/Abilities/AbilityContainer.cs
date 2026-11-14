using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// A unit's granted abilities, and the paths that run them: granting and removal,
    /// activation, commit, end and cancel, triggers, input, blocked tags and tasks. Only its
    /// <see cref="GameplayAbilitySystem"/> calls it, inside a mutation.
    /// </summary>
    /// <remarks>
    /// The list is locked while it is iterated and during an activation, so logic can grant and clear abilities: a
    /// cleared ability can't activate from that moment, and its slot goes when the list unlocks.
    /// </remarks>
    internal sealed class AbilityContainer
    {
        private readonly GameplayAbilitySystem owner;
        private readonly LockableList<AbilitySpec> specs = new(e => e.IsRemoved);
        private readonly Dictionary<GameplayTag, int> blockedTags = new();
        private int nextActivationId;

        public AbilityContainer(GameplayAbilitySystem _Owner)
        {
            owner = _Owner;
        }

        private AbilitySystemWorld World => owner.World;

        public AbilitySpec Find(AbilitySpecHandle _Handle) => _Handle.IsValid ? specs.Find(e => e.Handle == _Handle) : null;

        public AbilitySpec Find(GameplayAbility _Ability) => _Ability != null ? specs.Find(e => e.Ability == _Ability) : null;

        public List<AbilitySpec> Snapshot() => specs.Snapshot();



        #region Granting and removal
        /// <summary>
        /// Grant an ability: a per-actor instance is cloned now, and an <c>OwnedTagPresent</c> trigger
        /// whose tag is already on the owner activates it at once.
        /// </summary>
        public AbilitySpecHandle Give(GameplayAbility _Ability, float _Level, int _InputId, UnityEngine.Object _SourceObject, ActiveEffectHandle _GrantedBy)
        {
            if (_Ability == null) return AbilitySpecHandle.Invalid;

            var spec = new AbilitySpec(new AbilitySpecHandle(World.NextHandle()), _Ability, _Level, _InputId, _SourceObject) { GrantedByEffect = _GrantedBy };
            specs.Add(spec);
            World.TrackAbilityAsset(_Ability);

            if (_Ability.instancingPolicy == InstancingPolicy.InstancedPerActor)
            {
                spec.PerActorInstance = CreateInstance(spec);
                if (spec.PerActorInstance != null)
                    Call(() => spec.PerActorInstance.Logic.OnGranted(spec.PerActorInstance.Context));
            }

            RegisterTriggers(spec);
            owner.RaiseAbilityGiven(spec);

            foreach (var trigger in _Ability.triggers)
                if (trigger.source != AbilityTriggerSource.GameplayEvent && owner.HasTag(trigger.tag) && !spec.IsRemoved)
                    TryActivate(spec.Handle, null);

            return spec.Handle;
        }

        /// <summary>
        /// Clear an ability: it can't activate from now on. An active one is cancelled, with force, and goes when its
        /// activations end; otherwise it goes now.
        /// </summary>
        public bool Clear(AbilitySpecHandle _Handle)
        {
            var spec = Find(_Handle);
            if (spec == null) return false;

            spec.PendingRemove = true;
            if (spec.IsActive) Cancel(spec, true);
            if (!spec.IsRemoved && !spec.IsActive) Remove(spec);
            return true;
        }

        /// <summary>
        /// Remove the ability when its current activation ends, or now if it isn't active.
        /// </summary>
        public bool SetRemoveOnEnd(AbilitySpecHandle _Handle)
        {
            var spec = Find(_Handle);
            if (spec == null) return false;

            if (spec.IsActive) spec.RemoveAfterActivation = true;
            else Remove(spec);
            return true;
        }

        private void Remove(AbilitySpec _Spec)
        {
            if (_Spec.IsRemoved) return;

            _Spec.IsRemoved = true;
            UnregisterTriggers(_Spec);

            if (_Spec.PerActorInstance != null)
                Call(() => _Spec.PerActorInstance.Logic.OnRemoved(_Spec.PerActorInstance.Context));

            owner.RaiseAbilityRemoved(_Spec);
            specs.Erase(_Spec);
        }

        private AbilityInstance CreateInstance(AbilitySpec _Spec)
        {
            AbilityLogic logic;
            try
            {
                logic = World.AbilityLogicCloner.Clone(_Spec.Ability);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return null;
            }

            if (logic == null)
            {
                Debug.LogError($"[{owner.Name}] Ability \"{_Spec.Ability.name}\" has no logic. It can't activate.");
                return null;
            }

            return new AbilityInstance(owner, _Spec, logic);
        }
        #endregion



        #region Activation
        /// <summary>
        /// Try to activate an ability. True if the logic's <c>Activate</c> ran.
        /// </summary>
        public bool TryActivate(AbilitySpecHandle _Handle, GameplayEventData? _Trigger)
        {
            var spec = Find(_Handle);
            if (spec == null || spec.PendingRemove || owner.IsShuttingDown) return false;

            var settings = World.Settings;
            if (World.ActivationDepth >= settings.MaxActivationDepth)
            {
                Debug.LogError($"[{owner.Name}] \"{spec.Ability.name}\" was not activated: abilities activating abilities reached the activation depth " +
                               $"limit of {settings.MaxActivationDepth}. Look for abilities that trigger themselves or each other.");
                return false;
            }

            var ability = spec.Ability;
            if (ability.instancingPolicy == InstancingPolicy.InstancedPerActor && spec.IsActive)
            {
                if (!ability.retriggerInstancedAbility) return false;
                End(spec.PerActorInstance, true);
            }

            if (!CanActivate(spec, _Trigger, out var failure))
            {
                owner.RaiseAbilityFailed(ability, failure);
                return false;
            }

            var instance = AcquireInstance(spec);
            if (instance == null) return false;

            World.ActivationDepth++;
            specs.Lock();
            try
            {
                PreActivate(spec, instance);

                try
                {
                    instance.Logic.Activate(instance.Context, _Trigger);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    End(instance, true);
                }

                return true;
            }
            finally
            {
                specs.Unlock();
                World.ActivationDepth--;
            }
        }

        /// <summary>
        /// Whether an ability may activate now, and why not.
        /// </summary>
        public bool CanActivate(AbilitySpec _Spec, GameplayEventData? _Trigger, out ActivationFailure _Failure)
        {
            _Failure = new ActivationFailure();
            var ability = _Spec.Ability;

            if (owner.ActorInfo.Avatar == null) _Failure.Reasons |= AbilityFailureReason.NoAvatar;
            else if (AreTagsBlocked(_Spec.GetAllTags())) _Failure.Reasons |= AbilityFailureReason.Blocked;
            else if (owner.HasAny(ability.activationBlockedTags)) _Failure.Reasons |= AbilityFailureReason.TagsBlocked;
            else if (!owner.HasAll(ability.activationRequiredTags)) _Failure.Reasons |= AbilityFailureReason.TagsMissing;
            else if (_Trigger.HasValue && !PassesSourceAndTarget(ability, _Trigger.Value)) _Failure.Reasons |= AbilityFailureReason.SourceOrTargetTags;
            else if (!CheckCooldown(_Spec)) _Failure.Reasons |= AbilityFailureReason.OnCooldown;
            else if (!CheckCost(_Spec)) _Failure.Reasons |= AbilityFailureReason.CannotAffordCost;
            else
            {
                var logic = _Spec.PerActorInstance?.Logic ?? ability.logic;
                var context = _Spec.PerActorInstance?.Context ?? new AbilityContext(owner, _Spec, null);
                bool allowed = true;
                var failureTags = _Failure.Tags;
                Call(() => allowed = logic == null || logic.CanActivate(context, failureTags));
                if (!allowed) _Failure.Reasons |= AbilityFailureReason.Logic;
            }

            return _Failure.Reasons == AbilityFailureReason.None;
        }

        private static bool PassesSourceAndTarget(GameplayAbility _Ability, GameplayEventData _Trigger)
        {
            if (_Trigger.Instigator != null
                && (_Trigger.Instigator.HasAny(_Ability.sourceBlockedTags) || !_Trigger.Instigator.HasAll(_Ability.sourceRequiredTags)))
                return false;

            if (_Trigger.Target != null
                && (_Trigger.Target.HasAny(_Ability.targetBlockedTags) || !_Trigger.Target.HasAll(_Ability.targetRequiredTags)))
                return false;

            return true;
        }

        private AbilityInstance AcquireInstance(AbilitySpec _Spec)
        {
            if (_Spec.Ability.instancingPolicy == InstancingPolicy.InstancedPerActor)
                return _Spec.PerActorInstance;

            var instance = CreateInstance(_Spec);
            if (instance != null)
                _Spec.executionInstances.Add(instance);

            return instance;
        }

        private void PreActivate(AbilitySpec _Spec, AbilityInstance _Instance)
        {
            var ability = _Spec.Ability;

            _Instance.IsActive = true;
            _Instance.IsEnding = false;
            _Instance.ActivationId = ++nextActivationId;
            _Spec.ActiveCount++;

            owner.Tags.AddTags(ability.activationOwnedTags);
            BlockTags(ability.blockAbilitiesWithTag);
            CancelAbilities(ability.cancelAbilitiesWithTag, null, _Instance);

            owner.RaiseAbilityActivated(_Spec);
        }
        #endregion



        #region Commit
        public bool Commit(AbilitySpec _Spec, bool _Cost, bool _Cooldown)
        {
            if ((_Cooldown && !CheckCooldown(_Spec)) || (_Cost && !CheckCost(_Spec))) return false;

            if (_Cooldown && _Spec.Ability.cooldownEffect != null)
                owner.ApplySpecToSelf(MakeSpec(_Spec, _Spec.Ability.cooldownEffect));

            if (_Cost && _Spec.Ability.costEffect != null)
                owner.ApplySpecToSelf(MakeSpec(_Spec, _Spec.Ability.costEffect));

            return true;
        }

        /// <summary>
        /// Whether the cost is affordable: every AddBase modifier must leave its attribute at zero or
        /// more, and the owner must have each attribute. Checked as a whole before anything is paid.
        /// </summary>
        public bool CheckCost(AbilitySpec _Spec)
        {
            var cost = _Spec.Ability.costEffect;
            if (cost == null) return true;

            var spec = MakeSpec(_Spec, cost);
            spec.CalculateModifierMagnitudes();

            for (int i = 0; i < cost.Modifiers.Count; i++)
            {
                var modifier = cost.Modifiers[i];
                if (modifier == null || modifier.op != ModifierOp.AddBase) continue;
                if (!owner.HasAttribute(modifier.attribute)) return false;
                if (owner.GetCurrent(modifier.attribute) + spec.GetMagnitude(i, false) < 0) return false;
            }

            return true;
        }

        public bool CheckCooldown(AbilitySpec _Spec)
        {
            var cooldown = _Spec.Ability.cooldownEffect;
            return cooldown == null || !owner.HasAny(cooldown.GetGrantedTags());
        }

        private GameplayEffectSpec MakeSpec(AbilitySpec _Spec, GameplayEffect _Effect)
        {
            var context = new GameplayEffectContext(owner) { AbilityLevel = Mathf.RoundToInt(_Spec.Level), SourceObject = _Spec.Ability };
            return owner.MakeOutgoingSpec(_Effect, _Spec.Level, context);
        }
        #endregion



        #region End and cancel
        /// <summary>
        /// End an activation: idempotent and safe to re-enter. One <c>OnEnd</c>, one event, balanced tags
        /// and blocks.
        /// </summary>
        public void End(AbilityInstance _Instance, bool _WasCancelled)
        {
            if (_Instance == null || !_Instance.IsActive || _Instance.IsEnding) return;

            var spec = _Instance.Spec;
            var ability = spec.Ability;
            _Instance.IsEnding = true;

            Call(() => _Instance.Logic.OnEnd(_Instance.Context, _WasCancelled));

            foreach (var task in _Instance.tasks.ToArray())
                task.EndInternal(true);
            _Instance.tasks.Clear();

            foreach (var cue in _Instance.cues)
                owner.RemoveCue(cue);
            _Instance.cues.Clear();

            owner.Tags.RemoveTags(ability.activationOwnedTags);
            UnblockTags(ability.blockAbilitiesWithTag);

            spec.ActiveCount--;
            _Instance.IsActive = false;
            _Instance.IsEnding = false;

            owner.RaiseAbilityEnded(spec, _WasCancelled);

            if (ability.instancingPolicy == InstancingPolicy.InstancedPerExecution)
                spec.executionInstances.Remove(_Instance);

            if ((spec.PendingRemove || spec.RemoveAfterActivation) && !spec.IsActive)
                Remove(spec);
        }

        /// <summary>
        /// Cancel every running activation of a spec that allows it, or every one with force.
        /// </summary>
        public void Cancel(AbilitySpec _Spec, bool _Force)
        {
            foreach (var instance in _Spec.ActiveInstances.ToList())
                if (_Force || instance.Logic.CanBeCancelled)
                    End(instance, true);
        }

        /// <summary>
        /// Cancel the active abilities that have any of <paramref name="_WithTags"/> and none of
        /// <paramref name="_WithoutTags"/>. Empty <paramref name="_WithTags"/> matches every ability. Never
        /// <paramref name="_Ignore"/>.
        /// </summary>
        public void CancelAbilities(GameplayTagContainer _WithTags, GameplayTagContainer _WithoutTags, AbilityInstance _Ignore)
        {
            if (_WithTags != null && _WithTags.IsEmpty) return;

            specs.Lock();
            try
            {
                foreach (var spec in specs.Snapshot())
                {
                    if (!spec.IsActive) continue;

                    var tags = spec.GetAllTags();
                    if (_WithTags != null && !tags.HasAny(_WithTags)) continue;
                    if (_WithoutTags != null && !_WithoutTags.IsEmpty && tags.HasAny(_WithoutTags)) continue;

                    foreach (var instance in spec.ActiveInstances.ToList())
                        if (instance != _Ignore && instance.Logic.CanBeCancelled)
                            End(instance, true);
                }
            }
            finally
            {
                specs.Unlock();
            }
        }

        /// <summary>
        /// End every activation, with force: the avatar changed, or the unit shuts down.
        /// </summary>
        public void CancelAll()
        {
            foreach (var spec in specs.Snapshot())
                Cancel(spec, true);
        }
        #endregion



        #region Blocked tags
        /// <summary>
        /// Block abilities with any of these tags (or their descendants). Reference-counted.
        /// </summary>
        public void BlockTags(GameplayTagContainer _Tags)
        {
            if (_Tags == null) return;

            foreach (var tag in _Tags)
                blockedTags[tag] = blockedTags.TryGetValue(tag, out int count) ? count + 1 : 1;
        }

        public void UnblockTags(GameplayTagContainer _Tags)
        {
            if (_Tags == null) return;

            foreach (var tag in _Tags)
            {
                if (!blockedTags.TryGetValue(tag, out int count)) continue;

                if (count <= 1) blockedTags.Remove(tag);
                else blockedTags[tag] = count - 1;
            }
        }

        /// <summary>
        /// Whether any of the tags is blocked: a blocked tag blocks its descendants too.
        /// </summary>
        public bool AreTagsBlocked(GameplayTagContainer _Tags)
        {
            foreach (var tag in _Tags)
                foreach (var blocked in blockedTags.Keys)
                    if (tag.MatchesTag(blocked))
                        return true;

            return false;
        }
        #endregion



        #region Triggers and events
        private void RegisterTriggers(AbilitySpec _Spec)
        {
            foreach (var trigger in _Spec.Ability.triggers)
            {
                if (trigger.source == AbilityTriggerSource.GameplayEvent) continue;

                var handle = _Spec.Handle;
                var source = trigger.source;
                _Spec.triggerRegistrations.Add(owner.Tags.RegisterTagEvent(trigger.tag, TagEventType.NewOrRemoved, (_, count) =>
                {
                    if (count > 0) owner.TryActivateAbility(handle);
                    else if (source == AbilityTriggerSource.OwnedTagPresent) owner.CancelAbility(handle);
                }));
            }
        }

        private static void UnregisterTriggers(AbilitySpec _Spec)
        {
            foreach (var registration in _Spec.triggerRegistrations)
                registration.Dispose();

            _Spec.triggerRegistrations.Clear();
        }

        /// <summary>
        /// Activate every ability with a <c>GameplayEvent</c> trigger the event's tag matches.
        /// </summary>
        /// <returns> How many activated. </returns>
        public int HandleGameplayEvent(GameplayTag _EventTag, GameplayEventData _Data)
        {
            int activated = 0;
            specs.Lock();
            try
            {
                foreach (var spec in specs.Snapshot())
                {
                    foreach (var trigger in spec.Ability.triggers)
                    {
                        if (trigger.source != AbilityTriggerSource.GameplayEvent || !_EventTag.MatchesTag(trigger.tag)) continue;
                        if (spec.IsRemoved || spec.PendingRemove) break;

                        if (TryActivate(spec.Handle, _Data)) activated++;
                        break;
                    }
                }
            }
            finally
            {
                specs.Unlock();
            }

            return activated;
        }
        #endregion



        #region Input
        public void InputPressed(int _InputId)
        {
            foreach (var spec in specs.Snapshot())
            {
                if (spec.InputId != _InputId || spec.PendingRemove) continue;

                spec.InputPressed = true;
                if (!spec.IsActive)
                {
                    TryActivate(spec.Handle, null);
                    continue;
                }

                foreach (var instance in spec.ActiveInstances.ToList())
                {
                    Call(() => instance.Logic.OnInputPressed(instance.Context));
                    NotifyInput(instance, true);
                }
            }
        }

        public void InputReleased(int _InputId)
        {
            foreach (var spec in specs.Snapshot())
            {
                if (spec.InputId != _InputId) continue;

                spec.InputPressed = false;
                foreach (var instance in spec.ActiveInstances.ToList())
                {
                    Call(() => instance.Logic.OnInputReleased(instance.Context));
                    NotifyInput(instance, false);
                }
            }
        }

        private static void NotifyInput(AbilityInstance _Instance, bool _Pressed)
        {
            foreach (var task in _Instance.tasks.OfType<WaitInput>().ToList())
                task.OnInput(_Pressed);
        }
        #endregion



        #region Tasks and lifecycle
        /// <summary>
        /// Tick every running task that asks for it, after the unit's effects ticked.
        /// </summary>
        public void TickTasks(double _Time, double _DeltaTime)
        {
            foreach (var spec in specs.Snapshot())
                foreach (var instance in spec.ActiveInstances.ToList())
                    foreach (var task in instance.tasks.ToArray())
                        if (!task.IsEnded && task.TicksWithWorld)
                            task.Tick(_Time, _DeltaTime);
        }

        /// <summary>
        /// The avatar changed: running activations are cancelled, and per-actor logic is told.
        /// </summary>
        public void OnAvatarChanged()
        {
            CancelAll();

            foreach (var spec in specs.Snapshot())
                if (spec.PerActorInstance != null)
                    Call(() => spec.PerActorInstance.Logic.OnAvatarSet(spec.PerActorInstance.Context));
        }

        /// <summary>
        /// The unit shuts down: every activation is cancelled and every ability dropped, without events.
        /// </summary>
        public void Clear()
        {
            CancelAll();

            foreach (var spec in specs.Snapshot())
            {
                spec.IsRemoved = true;
                UnregisterTriggers(spec);
            }

            specs.Clear();
            blockedTags.Clear();
        }
        #endregion



        private static void Call(Action _Hook)
        {
            try
            {
                _Hook();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
