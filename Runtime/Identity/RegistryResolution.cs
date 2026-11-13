using System;

namespace LMK.Runtime.Registry
{
    /// <summary>
    /// How a (registry ID, row ID) reference resolved against a registry database.
    /// </summary>
    public enum RegistryStatus
    {
        /// <summary>
        /// Unassigned: the row ID is all zero. Not a missing reference.
        /// </summary>
        None,

        /// <summary>
        /// The registry and the row exist.
        /// </summary>
        Live,

        /// <summary>
        /// The registry exists, but the row is not in it.
        /// </summary>
        Deleted,

        /// <summary>
        /// The registry is not loaded, or no database is active yet.
        /// </summary>
        MissingRegistry,
    }



    /// <summary>
    /// Result of resolving a reference: its status and, if live, the row's index in the database. <br/>
    /// Resolution is exact: there is never a fallback by name.
    /// </summary>
    public readonly struct RegistryResolution : IEquatable<RegistryResolution>
    {
        /// <summary>
        /// The row index of a reference that is not live.
        /// </summary>
        public const int NO_ROW = -1;

        public static readonly RegistryResolution None = new(RegistryStatus.None, NO_ROW);
        public static readonly RegistryResolution Deleted = new(RegistryStatus.Deleted, NO_ROW);
        public static readonly RegistryResolution MissingRegistry = new(RegistryStatus.MissingRegistry, NO_ROW);

        public readonly RegistryStatus Status;

        /// <summary>
        /// Index of the live row in the database that resolved it, valid for that database only. <br/>
        /// <see cref="NO_ROW"/> unless <see cref="IsLive"/>.
        /// </summary>
        public readonly int Row;

        public bool IsLive => Status == RegistryStatus.Live;

        /// <summary>
        /// Whether the reference is assigned but does not resolve: <see cref="RegistryStatus.Deleted"/>
        /// or <see cref="RegistryStatus.MissingRegistry"/>.
        /// </summary>
        public bool IsMissing => Status == RegistryStatus.Deleted || Status == RegistryStatus.MissingRegistry;



        private RegistryResolution(RegistryStatus _Status, int _Row)
        {
            Status = _Status;
            Row = _Row;
        }

        /// <summary>
        /// A live resolution to row <paramref name="_Row"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="_Row"/> is negative. </exception>
        public static RegistryResolution Live(int _Row)
        {
            if (_Row < 0) throw new ArgumentOutOfRangeException(nameof(_Row), _Row, "A live row index is not negative.");
            return new RegistryResolution(RegistryStatus.Live, _Row);
        }



        public bool Equals(RegistryResolution _Other) => Status == _Other.Status && Row == _Other.Row;

        public override bool Equals(object _Obj) => _Obj is RegistryResolution other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Status, Row);

        public static bool operator ==(RegistryResolution _A, RegistryResolution _B) => _A.Equals(_B);

        public static bool operator !=(RegistryResolution _A, RegistryResolution _B) => !_A.Equals(_B);

        public override string ToString() => IsLive ? $"Live({Row})" : Status.ToString();
    }
}
