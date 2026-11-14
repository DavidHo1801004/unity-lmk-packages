using System;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// What an ability's logic uses to act: its unit, spec and level; commit, cost and cooldown; end and
    /// cancel; effects; and tasks with their <c>await</c> helpers.
    /// </summary>
    /// <remarks>
    /// One context per instance. Calls about the activation (<see cref="End"/>, <see cref="Commit"/>, tasks) act on the
    /// activation running now, and do nothing when none is.
    /// </remarks>
    public sealed class AbilityContext
    {
        public GameplayAbilitySystem Owner { get; }

        public AbilityActorInfo ActorInfo => Owner.ActorInfo;

        public AbilitySpec Spec { get; }

        /// <summary>
        /// The instance, or <see langword="null"/> for a check made on the template before activation.
        /// </summary>
        public AbilityInstance Instance { get; }

        public float Level => Spec.Level;

        public bool IsActive => Instance != null && Instance.IsActive && !Instance.IsEnding;

        public GameplayAbility Ability => Spec.Ability;

        internal AbilityContext(GameplayAbilitySystem _Owner, AbilitySpec _Spec, AbilityInstance _Instance)
        {
            Owner = _Owner;
            Spec = _Spec;
            Instance = _Instance;
        }



        #region Commit
        /// <summary>
        /// Pay the cost and start the cooldown, all or nothing: false, with nothing paid, if either check
        /// fails now.
        /// </summary>
        public bool Commit() => IsActive && Owner.Abilities.Commit(Spec, true, true);

        public bool CommitCost() => IsActive && Owner.Abilities.Commit(Spec, true, false);

        public bool CommitCooldown() => IsActive && Owner.Abilities.Commit(Spec, false, true);

        public bool CheckCost() => Owner.Abilities.CheckCost(Spec);

        public bool CheckCooldown() => Owner.Abilities.CheckCooldown(Spec);
        #endregion



        #region End
        /// <summary>
        /// End the activation. Idempotent, and safe from inside <see cref="AbilityLogic.OnEnd"/>.
        /// </summary>
        public void End()
        {
            if (Instance != null) Owner.Abilities.End(Instance, false);
        }

        /// <summary>
        /// Cancel the activation, if its logic can be cancelled.
        /// </summary>
        public void Cancel()
        {
            if (Instance != null && Instance.Logic.CanBeCancelled) Owner.Abilities.End(Instance, true);
        }
        #endregion



        #region Effects
        /// <summary>
        /// A spec of <paramref name="_Effect"/> from the owner, at the ability's level unless given.
        /// </summary>
        public GameplayEffectSpec MakeOutgoingSpec(GameplayEffect _Effect, float? _Level = null)
        {
            var context = new GameplayEffectContext(Owner) { AbilityLevel = Mathf.RoundToInt(Level), SourceObject = Spec.Ability };
            return Owner.MakeOutgoingSpec(_Effect, _Level ?? Level, context);
        }

        public ActiveEffectHandle ApplyToOwner(GameplayEffectSpec _Spec) => Owner.ApplySpecToSelf(_Spec);

        public ActiveEffectHandle ApplyToTarget(GameplayEffectSpec _Spec, GameplayAbilitySystem _Target) => Owner.ApplySpecToTarget(_Spec, _Target);
        #endregion



        #region Cues
        /// <summary>
        /// Raise a one-shot cue on the owner, or on <paramref name="_Target"/>.
        /// </summary>
        public void ExecuteCue(GameplayTag _Cue, GameplayCueParameters _Parameters = null, GameplayAbilitySystem _Target = null)
            => (_Target ?? Owner).ExecuteCue(_Cue, WithSource(_Parameters));

        /// <summary>
        /// Start a lasting cue on the owner. It is removed when the activation ends, if <see cref="RemoveCue"/> didn't
        /// remove it first. Does nothing without a running activation.
        /// </summary>
        public void AddCue(GameplayTag _Cue, GameplayCueParameters _Parameters = null)
        {
            if (!IsActive || _Cue.IsNone) return;

            Instance.cues.Add(_Cue);
            Owner.AddCue(_Cue, WithSource(_Parameters));
        }

        /// <summary>
        /// Remove a cue this activation added.
        /// </summary>
        public void RemoveCue(GameplayTag _Cue, GameplayCueParameters _Parameters = null)
        {
            if (Instance == null || !Instance.cues.Remove(_Cue)) return;
            Owner.RemoveCue(_Cue, _Parameters);
        }

        private GameplayCueParameters WithSource(GameplayCueParameters _Parameters)
        {
            var parameters = _Parameters?.Copy() ?? new GameplayCueParameters();
            parameters.Instigator ??= Owner;
            parameters.SourceObject ??= Ability;
            parameters.Level = Level;
            return parameters;
        }
        #endregion



        #region Tasks
        /// <summary>
        /// Start a task owned by the running activation. It ends with the activation. Without a running activation the
        /// task is returned ended, and never reports.
        /// </summary>
        public T StartTask<T>(T _Task) where T : AbilityTask
        {
            if (_Task == null) throw new ArgumentNullException(nameof(_Task));

            if (!IsActive)
            {
                _Task.EndInternal(true);
                return _Task;
            }

            _Task.Instance = Instance;
            _Task.ActivationId = Instance.ActivationId;
            Instance.tasks.Add(_Task);

            try
            {
                _Task.OnStart();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _Task.End();
            }

            return _Task;
        }

        /// <summary>
        /// <c>await ctx.Delay(1)</c>: resumes after one second of world time, unless the activation ended.
        /// </summary>
        public WaitDelay Delay(double _Seconds) => StartTask(new WaitDelay(_Seconds));

        public WaitGameplayEvent WaitEvent(GameplayTag _Tag) => StartTask(new WaitGameplayEvent(_Tag));

        public WaitTagAdded WaitTagAdded(GameplayTag _Tag) => StartTask(new WaitTagAdded(_Tag));

        public WaitTagRemoved WaitTagRemoved(GameplayTag _Tag) => StartTask(new WaitTagRemoved(_Tag));

        public WaitAttributeChange WaitAttributeChange(GameplayAttribute _Attribute, ThresholdComparison _Comparison, float _Value)
            => StartTask(new WaitAttributeChange(_Attribute, _Comparison, _Value));

        public WaitEffectRemoved WaitEffectRemoved(ActiveEffectHandle _Handle) => StartTask(new WaitEffectRemoved(_Handle));

        public WaitInput WaitInputPress() => StartTask(new WaitInput(true));

        public WaitInput WaitInputRelease() => StartTask(new WaitInput(false));

        public WaitTargetData WaitTargetData(TargetingStrategy _Targeting) => StartTask(new WaitTargetData(_Targeting));

        /// <summary>
        /// <c>await ctx.PlayAnimation("Cast")</c>: plays an animator state on the avatar and resumes when it has played.
        /// See <see cref="PlayAnimationAndWait"/>.
        /// </summary>
        public PlayAnimationAndWait PlayAnimation(string _State, float _Duration = 0f, int _Layer = 0, float _CrossFade = 0.1f, float _Speed = 1f)
            => StartTask(new PlayAnimationAndWait(_State, _Duration, _Layer, _CrossFade, _Speed));
        #endregion
    }
}
