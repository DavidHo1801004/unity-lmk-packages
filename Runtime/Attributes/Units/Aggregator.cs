using System;
using System.Collections.Generic;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// A modifier added to an attribute by a duration or infinite effect, identified by a handle issued by the world.
    /// </summary>
    internal readonly struct ModifierHandle : IEquatable<ModifierHandle>
    {
        public static readonly ModifierHandle Invalid = default;

        public readonly int Id;

        public bool IsValid => Id > 0;

        public ModifierHandle(int _Id)
        {
            Id = _Id;
        }

        public bool Equals(ModifierHandle _Other) => Id == _Other.Id;

        public override bool Equals(object _Obj) => _Obj is ModifierHandle other && Equals(other);

        public override int GetHashCode() => Id;

        public override string ToString() => IsValid ? $"Modifier {Id}" : "Invalid";
    }



    /// <summary>
    /// The modifiers on one attribute of one unit, and the UE 5.4 formula that turns a base value into a current value:
    /// <code>
    /// value = (base + Σ AddBase) · (1 + Σ(MultiplyAdditive − 1)) / (1 + Σ(DivideAdditive − 1)) · Π MultiplyCompound + Σ AddFinal
    /// </code>
    /// </summary>
    /// <remarks>
    /// Channels are evaluated in ascending order, each channel's result being the next one's base. In a channel, the
    /// first applied qualified <see cref="ModifierOp.Override"/> replaces the result. A modifier counts only while its
    /// qualification passes. A stacked modifier is folded by its stack count: additive ops use <c>m × count</c>,
    /// multiply and divide ops <c>1 + (m − 1) × count</c>, and override ignores the count. <br/>
    /// Modifiers are kept by attribute identity, not by instance: when a unit loses an attribute they wait, and they
    /// apply again when it comes back.
    /// </remarks>
    internal sealed class Aggregator
    {
        internal sealed class Modifier
        {
            public ModifierHandle Handle;
            public ModifierOp Op;
            public float Magnitude;
            public int Channel;
            public int StackCount = 1;

            /// <summary>
            /// Whether the modifier counts now, e.g. its tag requirements pass. <see langword="null"/>: always.
            /// </summary>
            public Func<bool> Qualifies;

            public bool IsQualified => Qualifies == null || Qualifies();
        }

        // In application order: handles grow, so the first applied comes first.
        private readonly List<Modifier> modifiers = new();

        public int Count => modifiers.Count;

        public IReadOnlyList<Modifier> Modifiers => modifiers;

        public void Add(Modifier _Modifier) => modifiers.Add(_Modifier);

        public Modifier Find(ModifierHandle _Handle)
        {
            foreach (var modifier in modifiers)
                if (modifier.Handle.Equals(_Handle))
                    return modifier;

            return null;
        }

        public bool Remove(ModifierHandle _Handle)
        {
            int index = modifiers.FindIndex(e => e.Handle.Equals(_Handle));
            if (index < 0) return false;

            modifiers.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// The value of <paramref name="_Base"/> after every qualified modifier of the channels up to
        /// <paramref name="_UpToChannel"/>, included.
        /// </summary>
        /// <param name="_ZeroDivisor"> Set when a channel's divisors summed to zero and were counted as 1. </param>
        public float Evaluate(float _Base, out bool _ZeroDivisor, int _UpToChannel = int.MaxValue)
        {
            _ZeroDivisor = false;
            if (modifiers.Count == 0) return _Base;

            var channels = new SortedSet<int>();
            foreach (var modifier in modifiers)
                if (modifier.Channel <= _UpToChannel)
                    channels.Add(modifier.Channel);

            float value = _Base;
            foreach (int channel in channels)
            {
                value = EvaluateChannel(channel, value, out bool zero);
                _ZeroDivisor |= zero;
            }

            return value;
        }

        private float EvaluateChannel(int _Channel, float _Base, out bool _ZeroDivisor)
        {
            _ZeroDivisor = false;

            float addBase = 0f, multiplyAdditive = 1f, divideAdditive = 1f, multiplyCompound = 1f, addFinal = 0f;
            bool hasOverride = false;
            float overrideValue = 0f;

            foreach (var modifier in modifiers)
            {
                if (modifier.Channel != _Channel || !modifier.IsQualified) continue;

                float m = modifier.Magnitude;
                float count = Math.Max(1, modifier.StackCount);

                switch (modifier.Op)
                {
                    case ModifierOp.AddBase: addBase += m * count; break;
                    case ModifierOp.MultiplyAdditive: multiplyAdditive += (m - 1f) * count; break;
                    case ModifierOp.DivideAdditive: divideAdditive += (m - 1f) * count; break;
                    case ModifierOp.MultiplyCompound: multiplyCompound *= 1f + (m - 1f) * count; break;
                    case ModifierOp.AddFinal: addFinal += m * count; break;
                    case ModifierOp.Override:
                        if (!hasOverride)
                        {
                            hasOverride = true;
                            overrideValue = m;
                        }
                        break;
                }
            }

            if (hasOverride) return overrideValue;

            if (divideAdditive == 0f)
            {
                _ZeroDivisor = true;
                divideAdditive = 1f;
            }

            return (_Base + addBase) * multiplyAdditive / divideAdditive * multiplyCompound + addFinal;
        }
    }
}
