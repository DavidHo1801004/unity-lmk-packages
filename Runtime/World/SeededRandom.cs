using System;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Random source of an ability system world. Gameplay code draws only from here, never from
    /// <see cref="UnityEngine.Random"/>, so a run with the same seed and inputs can be reproduced.
    /// </summary>
    public interface IAbilitySystemRandom
    {
        /// <summary>
        /// A value in [0, 1).
        /// </summary>
        double NextDouble();

        /// <summary>
        /// An integer in [<paramref name="_Min"/>, <paramref name="_Max"/>).
        /// </summary>
        int Range(int _Min, int _Max);
    }



    /// <summary>
    /// A small, fast, seeded generator (SplitMix64). The same seed always gives the same sequence, on every platform.
    /// </summary>
    public sealed class SeededRandom : IAbilitySystemRandom
    {
        private ulong state;

        /// <summary>
        /// The seed the generator started from.
        /// </summary>
        public ulong Seed { get; }

        public SeededRandom(ulong _Seed = 0x9E3779B97F4A7C15UL)
        {
            Seed = _Seed;
            state = _Seed;
        }

        public ulong NextULong()
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        // The top 53 bits: every double in [0, 1) at that resolution is equally likely.
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        public int Range(int _Min, int _Max)
        {
            if (_Max <= _Min) throw new ArgumentOutOfRangeException(nameof(_Max), _Max, "The upper bound must be above the lower bound.");

            ulong span = (ulong)((long)_Max - _Min);
            return (int)(_Min + (long)(NextULong() % span));
        }
    }
}
