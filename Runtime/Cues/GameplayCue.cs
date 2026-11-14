using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// What happened to a cue.
    /// </summary>
    public enum GameplayCueEvent
    {
        /// <summary> An instant effect applied, or a periodic effect executed: one-shot feedback. </summary>
        Executed,

        /// <summary> A duration or infinite effect became active, and isn't inhibited. </summary>
        OnActive,

        /// <summary> Raised with <see cref="OnActive"/>, and again for a late observer. </summary>
        WhileActive,

        /// <summary> The effect was removed or inhibited. </summary>
        Removed,
    }



    /// <summary>
    /// What a cue notify knows about the gameplay that raised it (UE's <c>FGameplayCueParameters</c>). Read it; never
    /// change gameplay state from a notify.
    /// </summary>
    public sealed class GameplayCueParameters
    {
        public float RawMagnitude;

        /// <summary>
        /// The level's position in the cue's level range, 0 to 1.
        /// </summary>
        public float NormalizedMagnitude;

        public GameplayEffectContext EffectContext;

        /// <summary>
        /// The tag of the notify that runs: the cue's tag, or the ancestor routing fell back to.
        /// </summary>
        public GameplayTag MatchedTag;

        /// <summary>
        /// The cue's own tag.
        /// </summary>
        public GameplayTag OriginalTag;

        public GameplayTagContainer SourceTags = new();

        public GameplayTagContainer TargetTags = new();

        public Vector3? Location;

        public Vector3? Normal;

        public GameplayAbilitySystem Instigator;

        public UnityEngine.Object EffectCauser;

        public UnityEngine.Object SourceObject;

        public float Level = 1f;

        /// <summary>
        /// A copy, so the notify of one tag can't change what the next one sees.
        /// </summary>
        public GameplayCueParameters Copy()
        {
            var copy = (GameplayCueParameters)MemberwiseClone();
            copy.SourceTags = new GameplayTagContainer(SourceTags);
            copy.TargetTags = new GameplayTagContainer(TargetTags);
            return copy;
        }
    }



    /// <summary>
    /// Receives cue events once a world step is over, e.g. the <c>GameplayCueManager</c> of a scene.
    /// </summary>
    public interface IGameplayCueHandler
    {
        void HandleCue(GameplayAbilitySystem _Target, GameplayTag _CueTag, GameplayCueEvent _Event, GameplayCueParameters _Parameters);

        /// <summary>
        /// The unit shut down: drop whatever is still showing for it.
        /// </summary>
        void ClearTarget(GameplayAbilitySystem _Target);
    }



    /// <summary>
    /// Where gameplay sends cue events. Mutators enqueue them; the world flushes the queue at the end of
    /// each step, so a notify never runs in the middle of a mutation.
    /// </summary>
    public interface ICueDispatcher
    {
        void Enqueue(GameplayAbilitySystem _Target, GameplayTag _CueTag, GameplayCueEvent _Event, GameplayCueParameters _Parameters);

        /// <summary>
        /// Deliver the queued events.
        /// </summary>
        void Flush();

        void ClearTarget(GameplayAbilitySystem _Target);
    }



    /// <summary>
    /// The default dispatcher: queues events and hands them to its <see cref="Handler"/> on flush. An add and a remove
    /// of the same cue on the same unit in one step cancel out.
    /// </summary>
    public sealed class GameplayCueQueue : ICueDispatcher
    {
        private readonly List<(GameplayAbilitySystem Target, GameplayTag Tag, GameplayCueEvent Event, GameplayCueParameters Parameters)> queue = new();

        /// <summary>
        /// Who receives the events, or <see langword="null"/> to drop them (a world without presentation).
        /// </summary>
        public IGameplayCueHandler Handler { get; set; }

        public int Count => queue.Count;

        public void Enqueue(GameplayAbilitySystem _Target, GameplayTag _CueTag, GameplayCueEvent _Event, GameplayCueParameters _Parameters)
        {
            if (_Target == null || _CueTag.IsNone) return;
            queue.Add((_Target, _CueTag, _Event, _Parameters ?? new GameplayCueParameters { OriginalTag = _CueTag }));
        }

        public void Flush()
        {
            if (queue.Count == 0) return;

            var events = new List<(GameplayAbilitySystem Target, GameplayTag Tag, GameplayCueEvent Event, GameplayCueParameters Parameters)>(queue);
            queue.Clear();

            // An add and a remove of the same cue in one step: neither is shown.
            var added = new HashSet<(GameplayAbilitySystem, GameplayTag)>();
            var removed = new HashSet<(GameplayAbilitySystem, GameplayTag)>();
            foreach (var (target, tag, type, _) in events)
            {
                if (type == GameplayCueEvent.OnActive) added.Add((target, tag));
                if (type == GameplayCueEvent.Removed) removed.Add((target, tag));
            }

            added.IntersectWith(removed);

            foreach (var (target, tag, type, parameters) in events)
            {
                if (type != GameplayCueEvent.Executed && added.Contains((target, tag))) continue;

                try
                {
                    Handler?.HandleCue(target, tag, type, parameters);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        public void ClearTarget(GameplayAbilitySystem _Target)
        {
            queue.RemoveAll(e => e.Target == _Target);
            Handler?.ClearTarget(_Target);
        }
    }
}
