namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Who may change a unit's state. Every mutator asks it first. A networked game answers with each peer's real
    /// role; single-player answers yes to everything.
    /// </summary>
    public interface IAbilitySystemNetDriver
    {
        /// <summary>
        /// Whether this process may change <paramref name="_System"/>'s state.
        /// </summary>
        bool HasAuthority(GameplayAbilitySystem _System);

        /// <summary>
        /// Whether this process may apply effects to <paramref name="_System"/>.
        /// </summary>
        bool CanApplyEffects(GameplayAbilitySystem _System);
    }



    /// <summary>
    /// The net driver of a single-player game: this process owns every unit.
    /// </summary>
    public sealed class SinglePlayerNetDriver : IAbilitySystemNetDriver
    {
        public bool HasAuthority(GameplayAbilitySystem _System) => true;

        public bool CanApplyEffects(GameplayAbilitySystem _System) => true;
    }
}
