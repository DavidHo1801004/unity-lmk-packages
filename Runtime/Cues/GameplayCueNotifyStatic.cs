using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// One-shot cue feedback with no per-target state (UE's <c>UGameplayCueNotify_Static</c>): a particle burst, a sound,
    /// a camera shake. One asset serves every target. Never change gameplay state from a notify.
    /// </summary>
    [Icon(ICON_PATH)]
    public abstract class GameplayCueNotifyStatic : ScriptableObject
    {
        /// <summary>
        /// The icon of notify assets. Unity doesn't inherit <see cref="IconAttribute"/>: subclasses that want it add
        /// <c>[Icon(ICON_PATH)]</c> themselves.
        /// </summary>
        protected const string ICON_PATH = "Packages/com.lmk.abilitysystem/Editor/Assets/Icons/GameplayCueNotify.png";

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
