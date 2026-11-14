using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// The notifies a cue manager routes to. A scene's runner lists the sets it uses.
    /// </summary>
    [CreateAssetMenu(menuName = "LMK/Ability System/Cue Set", fileName = "GameplayCueSet")]
    public sealed class GameplayCueSet : ScriptableObject
    {
        public List<GameplayCueNotifyStatic> staticNotifies = new();

        [Tooltip("Prefabs with a GameplayCueNotifyActor component.")]
        public List<GameplayCueNotifyActor> actorNotifies = new();
    }
}
