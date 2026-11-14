using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using UnityEngine;

using LMK.Runtime.Attributes;
using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// A small object owned by an ability activation (UE's <c>UAbilityTask</c>): it waits for something, reports back
    /// through its instance, and dies with the activation. A report that arrives after the activation
    /// ended, or after a new activation of the same instance began, is dropped.
    /// </summary>
    public abstract class AbilityTask
    {
        internal AbilityInstance Instance;
        internal int ActivationId;

        public bool IsEnded { get; private set; }

        /// <summary>
        /// The context of the activation that started the task.
        /// </summary>
        protected AbilityContext Context => Instance.Context;

        protected GameplayAbilitySystem System => Instance.Context.Owner;

        /// <summary>
        /// Whether the world calls <see cref="Tick"/> every step.
        /// </summary>
        protected internal virtual bool TicksWithWorld => false;

        protected internal virtual void OnStart() { }

        /// <param name="_Time"> The world time after the step. </param>
        protected internal virtual void Tick(double _Time, double _DeltaTime) { }

        /// <summary>
        /// Unsubscribe here.
        /// </summary>
        /// <param name="_AbilityEnded"> Ended because its activation ended, not by itself. </param>
        protected internal virtual void OnEnd(bool _AbilityEnded) { }

        /// <summary>
        /// End the task. Idempotent.
        /// </summary>
        public void End() => EndInternal(false);

        internal void EndInternal(bool _AbilityEnded)
        {
            if (IsEnded) return;

            IsEnded = true;
            try
            {
                OnEnd(_AbilityEnded);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            Instance?.tasks.Remove(this);
        }

        /// <summary>
        /// Report back, through the instance: dropped if the activation that started the task is over.
        /// </summary>
        protected void Deliver(Action _Callback)
        {
            if (!IsEnded) Instance.Deliver(ActivationId, _Callback);
        }
    }



    /// <summary>
    /// A task that completes once with a value. Await it, or listen to <see cref="Completed"/>.
    /// </summary>
    public abstract class AbilityTask<T> : AbilityTask
    {
        private readonly AbilityAwaiter<T> awaiter = new();
        private Action<T> completed;
        private bool hasResult;
        private T result;

        /// <summary>
        /// Called with the result, unless the activation is over. A listener added after the task completed, as when
        /// it completed while starting, is called at once.
        /// </summary>
        public event Action<T> Completed
        {
            add
            {
                if (hasResult) Instance?.Deliver(ActivationId, () => value?.Invoke(result));
                else completed += value;
            }
            remove => completed -= value;
        }

        public AbilityAwaiter<T> GetAwaiter() => awaiter;

        /// <summary>
        /// Report the result and end the task.
        /// </summary>
        protected void Complete(T _Value)
        {
            Deliver(() =>
            {
                hasResult = true;
                result = _Value;
                completed?.Invoke(_Value);
                awaiter.Complete(_Value);
            });
            End();
        }

        /// <summary>
        /// Report a result and keep running, e.g. for a task that fires on every event.
        /// </summary>
        protected void Report(T _Value) => Deliver(() => completed?.Invoke(_Value));
    }



    /// <summary>
    /// The awaiter of an ability task. Its continuation runs synchronously, inside the world step or the call that
    /// completed the task, and never runs if the activation ended first.
    /// </summary>
    public sealed class AbilityAwaiter<T> : INotifyCompletion
    {
        private Action continuation;
        private T result;

        public bool IsCompleted { get; private set; }

        public T GetResult() => result;

        public void OnCompleted(Action _Continuation)
        {
            if (IsCompleted) _Continuation();
            else continuation += _Continuation;
        }

        internal void Complete(T _Value)
        {
            if (IsCompleted) return;

            result = _Value;
            IsCompleted = true;

            var next = continuation;
            continuation = null;
            next?.Invoke();
        }
    }



    #region Tasks
    /// <summary>
    /// Completes when the world clock has advanced <c>seconds</c> since the task started.
    /// </summary>
    public sealed class WaitDelay : AbilityTask<bool>
    {
        private readonly double seconds;
        private double end;

        public WaitDelay(double _Seconds) => seconds = Math.Max(0, _Seconds);

        protected internal override bool TicksWithWorld => true;

        protected internal override void OnStart() => end = System.World.Time + seconds;

        protected internal override void Tick(double _Time, double _DeltaTime)
        {
            if (_Time >= end - 1e-6) Complete(true);
        }
    }



    /// <summary>
    /// Completes when the owner handles a gameplay event whose tag is the task's tag or a descendant of it.
    /// </summary>
    public sealed class WaitGameplayEvent : AbilityTask<GameplayEventData>
    {
        private readonly GameplayTag tag;
        private readonly bool onlyOnce;
        private IDisposable registration;

        /// <param name="_OnlyOnce"> Complete on the first event; otherwise report every event until ended. </param>
        public WaitGameplayEvent(GameplayTag _Tag, bool _OnlyOnce = true)
        {
            tag = _Tag;
            onlyOnce = _OnlyOnce;
        }

        protected internal override void OnStart() => registration = System.RegisterGameplayEventListener(tag, data =>
        {
            if (onlyOnce) Complete(data);
            else Report(data);
        });

        protected internal override void OnEnd(bool _AbilityEnded) => registration?.Dispose();
    }



    /// <summary>
    /// Completes when the owner's count for a tag crosses zero: up for <see cref="WaitTagAdded"/>, down for
    /// <see cref="WaitTagRemoved"/>. Completes at once if the tag is already as wanted.
    /// </summary>
    public class WaitTagChange : AbilityTask<GameplayTag>
    {
        private readonly GameplayTag tag;
        private readonly bool added;
        private IDisposable registration;

        protected WaitTagChange(GameplayTag _Tag, bool _Added)
        {
            tag = _Tag;
            added = _Added;
        }

        protected internal override void OnStart()
        {
            if (System.HasTag(tag) == added)
            {
                Complete(tag);
                return;
            }

            registration = System.RegisterTagEvent(tag, TagEventType.NewOrRemoved, (_, count) =>
            {
                if ((count > 0) == added) Complete(tag);
            });
        }

        protected internal override void OnEnd(bool _AbilityEnded) => registration?.Dispose();
    }

    public sealed class WaitTagAdded : WaitTagChange
    {
        public WaitTagAdded(GameplayTag _Tag) : base(_Tag, true) { }
    }

    public sealed class WaitTagRemoved : WaitTagChange
    {
        public WaitTagRemoved(GameplayTag _Tag) : base(_Tag, false) { }
    }



    /// <summary>
    /// Completes with the new value when an attribute's current value changes so that it meets the comparison.
    /// </summary>
    public sealed class WaitAttributeChange : AbilityTask<float>
    {
        private readonly GameplayAttribute attribute;
        private readonly ThresholdComparison comparison;
        private readonly float value;

        public WaitAttributeChange(GameplayAttribute _Attribute, ThresholdComparison _Comparison, float _Value)
        {
            attribute = _Attribute;
            comparison = _Comparison;
            value = _Value;
        }

        protected internal override void OnStart() => System.OnAttributeChanged += OnChanged;

        protected internal override void OnEnd(bool _AbilityEnded) => System.OnAttributeChanged -= OnChanged;

        private void OnChanged(AttributeChange _Change)
        {
            if (_Change.Attribute != attribute) return;

            bool passes = comparison switch
            {
                ThresholdComparison.LessOrEqual => _Change.NewValue <= value,
                ThresholdComparison.Less => _Change.NewValue < value,
                ThresholdComparison.GreaterOrEqual => _Change.NewValue >= value,
                _ => _Change.NewValue > value,
            };

            if (passes) Complete(_Change.NewValue);
        }
    }



    /// <summary>
    /// Completes with the removal info when an active effect ends.
    /// </summary>
    public sealed class WaitEffectRemoved : AbilityTask<RemovalInfo>
    {
        private readonly ActiveEffectHandle handle;

        public WaitEffectRemoved(ActiveEffectHandle _Handle) => handle = _Handle;

        protected internal override void OnStart()
        {
            if (System.GetActiveEffect(handle) == null)
            {
                Complete(new RemovalInfo(true, 0));
                return;
            }

            System.OnEffectRemoved += OnRemoved;
        }

        protected internal override void OnEnd(bool _AbilityEnded) => System.OnEffectRemoved -= OnRemoved;

        private void OnRemoved(ActiveGameplayEffect _Effect, RemovalInfo _Info)
        {
            if (_Effect.Handle == handle) Complete(_Info);
        }
    }



    /// <summary>
    /// Completes with the new count when an active effect's stack count changes.
    /// </summary>
    public sealed class WaitEffectStackChange : AbilityTask<int>
    {
        private readonly ActiveEffectHandle handle;

        public WaitEffectStackChange(ActiveEffectHandle _Handle) => handle = _Handle;

        protected internal override void OnStart() => System.OnStackChanged += OnChanged;

        protected internal override void OnEnd(bool _AbilityEnded) => System.OnStackChanged -= OnChanged;

        private void OnChanged(ActiveEffectHandle _Handle, int _Old, int _New)
        {
            if (_Handle == handle) Complete(_New);
        }
    }



    /// <summary>
    /// Completes when the ability's input is pressed (or released). Completes at once if it already is, unless asked
    /// to wait for a new press.
    /// </summary>
    public sealed class WaitInput : AbilityTask<bool>
    {
        internal readonly bool pressed;
        private readonly bool skipCurrent;

        public WaitInput(bool _Pressed, bool _SkipCurrentState = true)
        {
            pressed = _Pressed;
            skipCurrent = _SkipCurrentState;
        }

        protected internal override void OnStart()
        {
            if (!skipCurrent && Instance.Spec.InputPressed == pressed)
                Complete(true);
        }

        internal void OnInput(bool _Pressed)
        {
            if (_Pressed == pressed) Complete(true);
        }
    }



    /// <summary>
    /// What a targeting strategy found.
    /// </summary>
    public sealed class TargetData
    {
        public bool Cancelled;

        public readonly List<GameplayAbilitySystem> Targets = new();

        public Vector3? Point;

        public Vector3? Direction;

        public static TargetData Cancel() => new() { Cancelled = true };
    }



    /// <summary>
    /// How an ability picks its targets: inline-instanced on the logic, cloned with it. It reports through
    /// the callbacks it is given, at once or later (after player input, for example).
    /// </summary>
    [Serializable, CanReferenceInline]
    public abstract class TargetingStrategy
    {
        /// <param name="_Confirm"> Call with the targets found. </param>
        /// <param name="_Cancel"> Call when targeting was abandoned. </param>
        public abstract void Begin(AbilityContext _Context, Action<TargetData> _Confirm, Action _Cancel);

        /// <summary>
        /// The ability ended before targeting finished: stop listening.
        /// </summary>
        public virtual void Abort(AbilityContext _Context) { }
    }



    /// <summary>
    /// Completes when the targeting strategy confirms or cancels.
    /// </summary>
    public sealed class WaitTargetData : AbilityTask<TargetData>
    {
        private readonly TargetingStrategy targeting;

        public WaitTargetData(TargetingStrategy _Targeting) => targeting = _Targeting;

        protected internal override void OnStart()
        {
            if (targeting == null)
            {
                Complete(TargetData.Cancel());
                return;
            }

            targeting.Begin(Context, Complete, () => Complete(TargetData.Cancel()));
        }

        protected internal override void OnEnd(bool _AbilityEnded)
        {
            if (_AbilityEnded) targeting?.Abort(Context);
        }
    }
    #endregion
}
