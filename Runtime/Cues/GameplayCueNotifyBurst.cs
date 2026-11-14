using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// A built-in static notify: spawns a prefab and plays a sound at the target's avatar, for one-shot feedback.
    /// </summary>
    [CreateAssetMenu(menuName = "LMK/Ability System/Cue Notify (Burst)", fileName = "GC_Burst")]
    public sealed class GameplayCueNotifyBurst : GameplayCueNotifyStatic
    {
        [Tooltip("Spawned at the avatar, or at the cue's location, and destroyed after its lifetime.")]
        public GameObject prefab;

        public float prefabLifetime = 2f;

        public AudioClip sound;

        [Tooltip("Which events spawn the burst.")]
        public bool onExecute = true;

        public bool onActive;

        public override void OnExecute(GameplayAbilitySystem _Target, GameplayCueParameters _Parameters)
        {
            if (onExecute) Burst(_Target, _Parameters);
        }

        public override void OnActive(GameplayAbilitySystem _Target, GameplayCueParameters _Parameters)
        {
            if (onActive) Burst(_Target, _Parameters);
        }

        private void Burst(GameplayAbilitySystem _Target, GameplayCueParameters _Parameters)
        {
            var avatar = _Target.ActorInfo.AvatarGameObject;
            var position = _Parameters.Location ?? (avatar != null ? avatar.transform.position : Vector3.zero);

            if (prefab != null)
            {
                var instance = Instantiate(prefab, position, Quaternion.identity);
                if (Application.isPlaying) Destroy(instance, prefabLifetime);
            }

            if (sound != null && Application.isPlaying)
                AudioSource.PlayClipAtPoint(sound, position);
        }
    }
}
