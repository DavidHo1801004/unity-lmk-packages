using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Cue feedback that lives on a target (UE's <c>AGameplayCueNotify_Actor</c>): a burning loop, a shield mesh. The
    /// component sits on a prefab; the cue manager keeps one pooled instance per target from <c>OnActive</c> to
    /// <c>Removed</c>. Never change gameplay state from a notify.
    /// </summary>
    public class GameplayCueNotifyActor : MonoBehaviour
    {
        [Tooltip("The cue this notify handles, under GameplayCue.* in a tag registry.")]
        public GameplayTag cueTag;

        [Tooltip("Stop here: notifies of parent tags don't run for this cue.")]
        public bool isOverride = true;

        [Tooltip("Parent the instance to the target's avatar while active.")]
        public bool attachToAvatar = true;

        [Tooltip("An Executed cue spawns a temporary instance that returns to the pool after this many seconds of world time.")]
        public float executedLifetime = 2f;

        /// <summary>
        /// The unit the instance shows a cue for, while it is out of the pool.
        /// </summary>
        public GameplayAbilitySystem Target { get; internal set; }

        public virtual void OnExecute(GameplayAbilitySystem _Target, GameplayCueParameters _Parameters) { }

        public virtual void OnActive(GameplayAbilitySystem _Target, GameplayCueParameters _Parameters) { }

        public virtual void WhileActive(GameplayAbilitySystem _Target, GameplayCueParameters _Parameters) { }

        public virtual void OnRemove(GameplayAbilitySystem _Target, GameplayCueParameters _Parameters) { }
    }
}
