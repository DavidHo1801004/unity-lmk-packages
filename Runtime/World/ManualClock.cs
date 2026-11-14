using System;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Clock that only moves when told to. Used by tests and tools that step a world by hand.
    /// </summary>
    public sealed class ManualClock : IAbilitySystemClock
    {
        public double Time { get; private set; }



        public ManualClock(double _StartTime = 0)
        {
            if (!IsFinite(_StartTime) || _StartTime < 0)
                throw new ArgumentOutOfRangeException(nameof(_StartTime), _StartTime, "Start time must be finite and not negative.");

            Time = _StartTime;
        }



        /// <summary>
        /// Moves the clock forward by <paramref name="_Seconds"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="_Seconds"/> is negative or not finite. </exception>
        public void Advance(double _Seconds)
        {
            if (!IsFinite(_Seconds) || _Seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(_Seconds), _Seconds, "A clock only moves forward by a finite amount.");

            Time += _Seconds;
        }

        private static bool IsFinite(double _Value) => !double.IsNaN(_Value) && !double.IsInfinity(_Value);
    }
}
