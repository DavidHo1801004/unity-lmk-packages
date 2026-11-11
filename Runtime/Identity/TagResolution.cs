using System;

namespace LMK.Runtime.GameplayTags
{
    /// <summary>
    /// How a <see cref="GameplayTag"/> reference resolved against the active <see cref="GameplayTagDatabase"/>.
    /// </summary>
    public enum TagStatus
    {
        /// <summary>
        /// Unassigned: the tag ID is all zero. Not a missing reference.
        /// </summary>
        None,

        /// <summary>
        /// The registry and the tag exist.
        /// </summary>
        Live,

        /// <summary>
        /// The registry exists, but the tag is not in it.
        /// </summary>
        Deleted,

        /// <summary>
        /// The registry is not loaded, or no database is active yet.
        /// </summary>
        MissingRegistry,
    }



    /// <summary>
    /// Result of resolving a tag reference: its status and, if live, its handle.
    /// </summary>
    public readonly struct TagResolution : IEquatable<TagResolution>
    {
        public static readonly TagResolution None = new(TagStatus.None, TagHandle.None);
        public static readonly TagResolution Deleted = new(TagStatus.Deleted, TagHandle.None);
        public static readonly TagResolution MissingRegistry = new(TagStatus.MissingRegistry, TagHandle.None);

        public readonly TagStatus Status;

        /// <summary>
        /// The live tag's handle. <see cref="TagHandle.None"/> unless <see cref="IsLive"/>.
        /// </summary>
        public readonly TagHandle Handle;

        public bool IsLive => Status == TagStatus.Live;

        /// <summary>
        /// Whether the reference is assigned but does not resolve: <see cref="TagStatus.Deleted"/>
        /// or <see cref="TagStatus.MissingRegistry"/>.
        /// </summary>
        public bool IsMissing => Status == TagStatus.Deleted || Status == TagStatus.MissingRegistry;



        public TagResolution(TagStatus _Status, TagHandle _Handle)
        {
            Status = _Status;
            Handle = _Handle;
        }

        public static TagResolution Live(TagHandle _Handle)
            => new(TagStatus.Live, _Handle);



        public bool Equals(TagResolution _Other) => Status == _Other.Status && Handle.Equals(_Other.Handle);

        public override bool Equals(object _Obj) => _Obj is TagResolution other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Status, Handle);

        public override string ToString() => IsLive ? $"Live({Handle})" : Status.ToString();
    }
}
