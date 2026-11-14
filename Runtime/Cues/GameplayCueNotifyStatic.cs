using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// One-shot cue feedback with no per-target state (UE's <c>UGameplayCueNotify_Static</c>): a particle burst, a sound,
    /// a camera shake. One asset serves every target. Never change gameplay state from a notify.
    /// </summary>
    public abstract class GameplayCueNotifyStatic : ScriptableObject
    {
        [Tooltip("The cue this notify handles, under GameplayCue.* in a tag registry.")]
        public GameplayTag cueTag;

        [Tooltip("Stop here: notifies of parent tags don't run for this cue.")]
        public bool isOverride = true;

        public virtual void OnExecute(GameplayAbilitySystem _Target, GameplayCueParameters _Parameters) { }

        public virtual void OnActive(GameplayAbilitySystem _Target, GameplayCueParameters _Parameters) { }

        public virtual void WhileActive(GameplayAbilitySystem _Target, GameplayCueParameters _Parameters) { }

        public virtual void OnRemove(GameplayAbilitySystem _Target, GameplayCueParameters _Parameters) { }
    }
}
