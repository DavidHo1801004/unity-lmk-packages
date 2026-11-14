using System;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// The clock of a world in a scene. The world runner steps the world with Unity's scaled delta time, so pause and
    /// time scale apply to every effect and ability. The default clock of a world.
    /// </summary>
    public sealed class ScaledClock : IAbilitySystemClock
    {
        public double Time { get; private set; }

        public void Advance(double _Seconds)
        {
            if (double.IsNaN(_Seconds) || double.IsInfinity(_Seconds) || _Seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(_Seconds), _Seconds, "A clock only moves forward by a finite amount.");

            Time += _Seconds;
        }
    }
}
