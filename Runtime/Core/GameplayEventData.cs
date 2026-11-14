using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// The payload of a gameplay event (UE's <c>FGameplayEventData</c>), sent with
    /// <see cref="GameplayAbilitySystem.HandleGameplayEvent"/>.
    /// </summary>
    public struct GameplayEventData
    {
        public GameplayTag EventTag;

        /// <summary>
        /// The unit that caused the event, or <see langword="null"/>.
        /// </summary>
        public GameplayAbilitySystem Instigator;

        /// <summary>
        /// The unit the event is about, or <see langword="null"/>.
        /// </summary>
        public GameplayAbilitySystem Target;

        public float Magnitude;

        public GameplayEventData(GameplayTag _EventTag, GameplayAbilitySystem _Instigator = null, GameplayAbilitySystem _Target = null, float _Magnitude = 0f)
        {
            EventTag = _EventTag;
            Instigator = _Instigator;
            Target = _Target;
            Magnitude = _Magnitude;
        }
    }
}
