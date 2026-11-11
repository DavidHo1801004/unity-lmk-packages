using System;

using UnityEngine;

namespace LMK.Runtime.GameplayTags
{
    /// <summary>
    /// A 128-bit identifier, serialized by Unity as two 64-bit halves. <br/>
    /// Used for registry IDs and tag IDs. All zero means "none".
    /// </summary>
    /// <remarks>
    /// The text form is 32 lowercase hex digits (<see cref="Guid"/> "N" format). <br/>
    /// <see cref="Hi"/> is the first 16 digits and <see cref="Lo"/> the last 16, read as big-endian numbers,
    /// so the text, the serialized fields and generated code always agree. <see cref="Guid.ToByteArray"/>
    /// is never used, because its byte order is mixed-endian. <br/>
    /// <b>FROZEN LAYOUT:</b> renaming or reordering the serialized fields breaks every saved reference.
    /// </remarks>
    [Serializable]
    public struct TagGuid : IEquatable<TagGuid>, IComparable<TagGuid>
    {
        /// <summary>
        /// Number of hex digits in the text form.
        /// </summary>
        public const int TEXT_LENGTH = 32;

        /// <summary>
        /// The all-zero value, meaning "none".
        /// </summary>
        public static readonly TagGuid Zero = default;

        [SerializeField]
        private ulong _hi;

        [SerializeField]
        private ulong _lo;

        /// <summary>
        /// First 16 hex digits of the text form.
        /// </summary>
        public ulong Hi => _hi;

        /// <summary>
        /// Last 16 hex digits of the text form.
        /// </summary>
        public ulong Lo => _lo;

        /// <summary>
        /// Whether this is <see cref="Zero"/>.
        /// </summary>
        public bool IsZero => _hi == 0 && _lo == 0;



        public TagGuid(ulong _Hi, ulong _Lo)
        {
            _hi = _Hi;
            _lo = _Lo;
        }

        /// <summary>
        /// A new random (v4) identifier. Never <see cref="Zero"/>.
        /// </summary>
        public static TagGuid NewGuid()
        {
            TagGuid result;
            do result = Parse(Guid.NewGuid().ToString("N"));
            while (result.IsZero);

            return result;
        }



        #region Text
        /// <summary>
        /// Parse 32 hex digits. Upper and lower case are accepted.
        /// </summary>
        /// <exception cref="FormatException"> If <paramref name="_Text"/> is not 32 hex digits. </exception>
        public static TagGuid Parse(string _Text)
        {
            if (TryParse(_Text, out var result)) return result;

            throw new FormatException($"\"{_Text}\" is not a {TEXT_LENGTH}-digit hex identifier.");
        }

        /// <summary>
        /// Parse 32 hex digits. Upper and lower case are accepted.
        /// </summary>
        public static bool TryParse(string _Text, out TagGuid _Result)
        {
            _Result = Zero;

            if (_Text == null || _Text.Length != TEXT_LENGTH) return false;
            if (!TryParseHex(_Text, 0, out ulong hi)) return false;
            if (!TryParseHex(_Text, TEXT_LENGTH / 2, out ulong lo)) return false;

            _Result = new TagGuid(hi, lo);
            return true;
        }

        /// <summary>
        /// 32 lowercase hex digits.
        /// </summary>
        public override string ToString()
            => _hi.ToString("x16") + _lo.ToString("x16");

        private static bool TryParseHex(string _Text, int _Start, out ulong _Value)
        {
            _Value = 0;

            for (int i = _Start; i < _Start + TEXT_LENGTH / 2; i++)
            {
                char c = _Text[i];
                int digit;

                if (c >= '0' && c <= '9') digit = c - '0';
                else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
                else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
                else return false;

                _Value = (_Value << 4) | (uint)digit;
            }

            return true;
        }
        #endregion



        #region Equality
        public bool Equals(TagGuid _Other)
            => _hi == _Other._hi && _lo == _Other._lo;

        public override bool Equals(object _Obj)
            => _Obj is TagGuid other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(_hi, _lo);

        /// <summary>
        /// Unsigned order of <see cref="Hi"/>, then <see cref="Lo"/>. Matches the ordinal order of the text form.
        /// </summary>
        public int CompareTo(TagGuid _Other)
        {
            int compare = _hi.CompareTo(_Other._hi);
            return compare != 0 ? compare : _lo.CompareTo(_Other._lo);
        }

        public static bool operator ==(TagGuid _A, TagGuid _B) => _A.Equals(_B);

        public static bool operator !=(TagGuid _A, TagGuid _B) => !_A.Equals(_B);
        #endregion
    }
}
