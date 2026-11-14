namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Time source of an ability system world. <br/>
    /// Gameplay code reads time only from here, never from <see cref="UnityEngine.Time"/>, so runs can be reproduced and tested.
    /// </summary>
    /// <remarks>
    /// The world advances its clock once per step (<see cref="AbilitySystemWorld.Step"/>). Nothing else moves it.
    /// </remarks>
    public interface IAbilitySystemClock
    {
        /// <summary>
        /// Seconds since the clock started. Never decreases.
        /// </summary>
        double Time { get; }

        /// <summary>
        /// Moves the clock forward by <paramref name="_Seconds"/>.
        /// </summary>
        /// <exception cref="System.ArgumentOutOfRangeException"> <paramref name="_Seconds"/> is negative or not finite. </exception>
        void Advance(double _Seconds);
    }
}
