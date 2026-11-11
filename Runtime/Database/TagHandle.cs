using System;

namespace LMK.Runtime.GameplayTags
{
    /// <summary>
    /// Index of a live tag in the active <see cref="GameplayTagDatabase"/>. <br/>
    /// Handles are rebuilt whenever the database changes, so they must never be serialized or kept across builds.
    /// </summary>
    public readonly struct TagHandle : IEquatable<TagHandle>
    {
        public static readonly TagHandle None = new(-1);

        public readonly int Index;

        public bool IsValid => Index >= 0;



        internal TagHandle(int _Index)
        {
            Index = _Index;
        }



        public bool Equals(TagHandle _Other) => Index == _Other.Index;

        public override bool Equals(object _Obj) => _Obj is TagHandle other && Equals(other);

        public override int GetHashCode() => Index;

        public static bool operator ==(TagHandle _A, TagHandle _B) => _A.Index == _B.Index;

        public static bool operator !=(TagHandle _A, TagHandle _B) => _A.Index != _B.Index;

        public override string ToString() => IsValid ? Index.ToString() : "None";
    }
}
