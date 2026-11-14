using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Attributes;
using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// One step of a <see cref="SequenceLogic"/>. Inline-instanced and cloned with the logic.
    /// </summary>
    [Serializable, CanReferenceInline]
    public abstract class AbilityAction
    {
        /// <summary>
        /// Run the step. Call <paramref name="_Next"/> with true to go on, or false to cancel the ability; at once or
        /// later. A step whose activation ended is never continued.
        /// </summary>
        public abstract void Run(AbilityContext _Context, Action<bool> _Next);
    }



    /// <summary>
    /// An ability assembled from steps, with no code: commit, wait, apply effects, send events. It ends
    /// after the last step, and is cancelled when a step fails.
    /// </summary>
    [Serializable]
    public sealed class SequenceLogic : AbilityLogic
    {
        [SerializeReference, InlineInstanced]
        public List<AbilityAction> actions = new();

        public override void Activate(AbilityContext _Context, GameplayEventData? _Trigger) => RunFrom(_Context, 0, _Context.Instance.ActivationId);

        private void RunFrom(AbilityContext _Context, int _Index, int _ActivationId)
        {
            // A continuation from an earlier activation of this instance.
            if (!_Context.IsActive || _Context.Instance.ActivationId != _ActivationId) return;

            if (_Index >= actions.Count)
            {
                _Context.End();
                return;
            }

            var action = actions[_Index];
            if (action == null)
            {
                RunFrom(_Context, _Index + 1, _ActivationId);
                return;
            }

            action.Run(_Context, ok =>
            {
                if (ok) RunFrom(_Context, _Index + 1, _ActivationId);
                else if (_Context.IsActive && _Context.Instance.ActivationId == _ActivationId) _Context.Cancel();
            });
        }
    }



    /// <summary>
    /// Commit: pay the cost and start the cooldown. Fails the sequence if either check fails.
    /// </summary>
    [Serializable]
    public sealed class CommitAction : AbilityAction
    {
        public override void Run(AbilityContext _Context, Action<bool> _Next) => _Next(_Context.Commit());
    }



    /// <summary>
    /// Wait some seconds of world time.
    /// </summary>
    [Serializable]
    public sealed class WaitAction : AbilityAction
    {
        public float seconds = 1f;

        public override void Run(AbilityContext _Context, Action<bool> _Next) => _Context.Delay(seconds).Completed += _ => _Next(true);
    }



    /// <summary>
    /// Apply an effect to the ability's owner.
    /// </summary>
    [Serializable]
    public sealed class ApplyEffectToOwnerAction : AbilityAction
    {
        public GameplayEffect effect;

        public override void Run(AbilityContext _Context, Action<bool> _Next)
        {
            if (effect != null)
                _Context.ApplyToOwner(_Context.MakeOutgoingSpec(effect));

            _Next(true);
        }
    }



    /// <summary>
    /// Find targets, then apply an effect to each. Fails the sequence if targeting is cancelled.
    /// </summary>
    [Serializable]
    public sealed class ApplyEffectToTargetsAction : AbilityAction
    {
        [SerializeReference, InlineInstanced]
        public TargetingStrategy targeting;

        public GameplayEffect effect;

        public override void Run(AbilityContext _Context, Action<bool> _Next)
        {
            _Context.WaitTargetData(targeting).Completed += data =>
            {
                if (data == null || data.Cancelled)
                {
                    _Next(false);
                    return;
                }

                if (effect != null)
                {
                    var spec = _Context.MakeOutgoingSpec(effect);
                    foreach (var target in data.Targets)
                        _Context.ApplyToTarget(spec, target);
                }

                _Next(true);
            };
        }
    }



    /// <summary>
    /// Send a gameplay event to the ability's owner.
    /// </summary>
    [Serializable]
    public sealed class SendEventAction : AbilityAction
    {
        public GameplayTag eventTag;

        public float magnitude;

        public override void Run(AbilityContext _Context, Action<bool> _Next)
        {
            _Context.Owner.HandleGameplayEvent(eventTag, new GameplayEventData(eventTag, _Context.Owner, _Context.Owner, magnitude));
            _Next(true);
        }
    }
}
