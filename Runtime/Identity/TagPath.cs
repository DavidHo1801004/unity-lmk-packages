namespace LMK.Runtime.GameplayTags
{
    /// <summary>
    /// Rules and helpers for tag paths of the form "A.B.C". <br/>
    /// A path is made of segments separated by <see cref="SEPARATOR"/>. Each segment matches <c>[A-Za-z0-9_]+</c>.
    /// Matching is case-sensitive and ordinal.
    /// </summary>
    public static class TagPath
    {
        /// <summary>
        /// Separator between the segments of a tag path.
        /// </summary>
        public const char SEPARATOR = '.';



        #region Validation
        /// <summary>
        /// Whether <paramref name="_Char"/> can be part of a segment.
        /// </summary>
        public static bool IsValidSegmentChar(char _Char)
            => (_Char >= 'A' && _Char <= 'Z')
            || (_Char >= 'a' && _Char <= 'z')
            || (_Char >= '0' && _Char <= '9')
            || _Char == '_';

        /// <summary>
        /// Whether <paramref name="_Segment"/> is one valid segment, without separators.
        /// </summary>
        public static bool IsValidSegment(string _Segment, out string _Error)
        {
            _Error = null;

            if (string.IsNullOrEmpty(_Segment))
            {
                _Error = "Tag name is empty.";
                return false;
            }

            for (int i = 0; i < _Segment.Length; i++)
            {
                if (IsValidSegmentChar(_Segment[i])) continue;

                _Error = $"\"{_Segment}\" contains invalid character '{_Segment[i]}' at index {i}. Use letters, digits or '_'.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether <paramref name="_Path"/> is a well formed tag path: non-empty segments separated by <see cref="SEPARATOR"/>.
        /// </summary>
        /// <param name="_Error"> Why the path is invalid, or <see langword="null"/>. </param>
        public static bool IsValidPath(string _Path, out string _Error)
        {
            _Error = null;

            if (string.IsNullOrEmpty(_Path))
            {
                _Error = "Tag path is empty.";
                return false;
            }

            int segmentLength = 0;
            for (int i = 0; i < _Path.Length; i++)
            {
                char c = _Path[i];

                if (c == SEPARATOR)
                {
                    if (segmentLength == 0)
                    {
                        _Error = $"Tag \"{_Path}\" has an empty segment at index {i}.";
                        return false;
                    }

                    segmentLength = 0;
                    continue;
                }

                if (!IsValidSegmentChar(c))
                {
                    _Error = $"Tag \"{_Path}\" contains invalid character '{c}' at index {i}. Use letters, digits or '_'.";
                    return false;
                }

                segmentLength++;
            }

            if (segmentLength == 0)
            {
                _Error = $"Tag \"{_Path}\" ends with '{SEPARATOR}'.";
                return false;
            }

            return true;
        }
        #endregion



        #region Structure
        /// <summary>
        /// The parent path, e.g. "A.B" for "A.B.C". Empty for a top level path.
        /// </summary>
        public static string GetParent(string _Path)
        {
            if (string.IsNullOrEmpty(_Path)) return string.Empty;

            int index = _Path.LastIndexOf(SEPARATOR);
            return index < 0 ? string.Empty : _Path[..index];
        }

        /// <summary>
        /// The last segment, e.g. "C" for "A.B.C".
        /// </summary>
        public static string GetSegment(string _Path)
        {
            if (string.IsNullOrEmpty(_Path)) return string.Empty;

            int index = _Path.LastIndexOf(SEPARATOR);
            return index < 0 ? _Path : _Path[(index + 1)..];
        }

        /// <summary>
        /// Join a parent path and a segment. A null or empty parent gives the segment alone.
        /// </summary>
        public static string Combine(string _Parent, string _Segment)
            => string.IsNullOrEmpty(_Parent) ? _Segment : _Parent + SEPARATOR + _Segment;

        /// <summary>
        /// Whether <paramref name="_Path"/> is <paramref name="_Parent"/>, or a descendant of it. <br/>
        /// Checks the separator boundary, so "A.Bc" is not a descendant of "A.B".
        /// </summary>
        public static bool IsSameOrChildOf(string _Path, string _Parent)
        {
            if (string.IsNullOrEmpty(_Path) || string.IsNullOrEmpty(_Parent)) return false;
            if (_Path.Length == _Parent.Length) return string.Equals(_Path, _Parent, System.StringComparison.Ordinal);

            return _Path.Length > _Parent.Length
                && _Path[_Parent.Length] == SEPARATOR
                && string.CompareOrdinal(_Path, 0, _Parent, 0, _Parent.Length) == 0;
        }

        /// <summary>
        /// Whether <paramref name="_Path"/> is a strict descendant of <paramref name="_Parent"/>, at any depth.
        /// </summary>
        public static bool IsChildOf(string _Path, string _Parent)
            => _Path != null && _Parent != null && _Path.Length > _Parent.Length && IsSameOrChildOf(_Path, _Parent);
        #endregion
    }
}
