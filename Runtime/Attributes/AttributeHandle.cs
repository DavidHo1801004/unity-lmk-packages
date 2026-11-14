using System;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Index of a live attribute in the active <see cref="GameplayAttributeDatabase"/>. <br/>
    /// Handles are rebuilt whenever the database changes, so they must never be serialized or kept across builds.
    /// </summary>
    public readonly struct AttributeHandle : IEquatable<AttributeHandle>
    {
        /// <summary>
        /// The handle of an attribute that doesn't resolve. Every operation on it is a no-op.
        /// </summary>
        public static readonly AttributeHandle None = new(-1);

        public readonly int Index;

        public bool IsValid => Index >= 0;



        internal AttributeHandle(int _Index)
        {
            Index = _Index;
        }



        public bool Equals(AttributeHandle _Other) => Index == _Other.Index;

        public override bool Equals(object _Obj) => _Obj is AttributeHandle other && Equals(other);

        public override int GetHashCode() => Index;

        public static bool operator ==(AttributeHandle _A, AttributeHandle _B) => _A.Index == _B.Index;

        public static bool operator !=(AttributeHandle _A, AttributeHandle _B) => _A.Index != _B.Index;

        public override string ToString() => IsValid ? Index.ToString() : "None";
    }
}
