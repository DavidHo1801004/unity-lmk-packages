using System;

using LMK.Runtime.Registry;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// How a <see cref="GameplayAttribute"/> reference resolved against the active <see cref="GameplayAttributeDatabase"/>.
    /// </summary>
    public enum AttributeStatus
    {
        /// <summary>
        /// Unassigned: the attribute ID is all zero. Not a missing reference.
        /// </summary>
        None,

        /// <summary>
        /// The registry and the attribute exist.
        /// </summary>
        Live,

        /// <summary>
        /// The registry exists, but the attribute is not in it.
        /// </summary>
        Deleted,

        /// <summary>
        /// The registry is not loaded (e.g. its package was removed), or no database is active yet.
        /// </summary>
        MissingRegistry,
    }



    /// <summary>
    /// Result of resolving an attribute reference: its status and, if live, its handle.
    /// </summary>
    public readonly struct AttributeResolution : IEquatable<AttributeResolution>
    {
        public static readonly AttributeResolution None = new(AttributeStatus.None, AttributeHandle.None);
        public static readonly AttributeResolution Deleted = new(AttributeStatus.Deleted, AttributeHandle.None);
        public static readonly AttributeResolution MissingRegistry = new(AttributeStatus.MissingRegistry, AttributeHandle.None);

        public readonly AttributeStatus Status;

        /// <summary>
        /// The live attribute's handle. <see cref="AttributeHandle.None"/> unless <see cref="IsLive"/>.
        /// </summary>
        public readonly AttributeHandle Handle;

        public bool IsLive => Status == AttributeStatus.Live;

        /// <summary>
        /// Whether the reference is assigned but does not resolve: <see cref="AttributeStatus.Deleted"/>
        /// or <see cref="AttributeStatus.MissingRegistry"/>.
        /// </summary>
        public bool IsMissing => Status == AttributeStatus.Deleted || Status == AttributeStatus.MissingRegistry;



        public AttributeResolution(AttributeStatus _Status, AttributeHandle _Handle)
        {
            Status = _Status;
            Handle = _Handle;
        }

        /// <summary>
        /// The attribute form of a registry resolution: a live row's index is the attribute's handle.
        /// </summary>
        public static AttributeResolution From(RegistryResolution _Resolution)
            => new((AttributeStatus)_Resolution.Status, _Resolution.IsLive ? new AttributeHandle(_Resolution.Row) : AttributeHandle.None);



        public bool Equals(AttributeResolution _Other) => Status == _Other.Status && Handle.Equals(_Other.Handle);

        public override bool Equals(object _Obj) => _Obj is AttributeResolution other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Status, Handle);

        public static bool operator ==(AttributeResolution _A, AttributeResolution _B) => _A.Equals(_B);

        public static bool operator !=(AttributeResolution _A, AttributeResolution _B) => !_A.Equals(_B);

        public override string ToString() => IsLive ? $"Live({Handle})" : Status.ToString();
    }
}
