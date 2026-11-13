using System;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// The identity of a stored tag reference: (registry ID, tag ID), as kept by the reference index.
    /// The tag form of a <see cref="RegistryKey"/> of the tag kind.
    /// </summary>
    /// <remarks>
    /// A zero <see cref="Registry"/> means the registry is unknown: a prefab instance override that changed only
    /// the tag ID stores no registry, since that field kept the prefab's value. Such a key matches the tag ID in
    /// any registry. Tag IDs are random 128-bit values, so this never matches the wrong tag in practice.
    /// </remarks>
    internal readonly struct TagKey : IEquatable<TagKey>
    {
        public readonly RegistryGuid Registry;
        public readonly RegistryGuid Id;

        public TagKey(RegistryGuid _Registry, RegistryGuid _Id)
        {
            Registry = _Registry;
            Id = _Id;
        }

        public static TagKey Of(GameplayTag _Tag) => new(_Tag.RegistryId, _Tag.Id);

        public static TagKey Of(in RegistryKey _Key) => new(_Key.Registry, _Key.Id);

        public bool HasRegistry => !Registry.IsZero;

        /// <summary>
        /// Whether this key stands for the tag (registry, ID). A key without a registry matches the ID in any registry.
        /// </summary>
        public bool Matches(RegistryGuid _Registry, RegistryGuid _Id) => Id == _Id && (!HasRegistry || Registry == _Registry);

        public bool Matches(GameplayTag _Tag) => Matches(_Tag.RegistryId, _Tag.Id);

        public bool Equals(TagKey _Other) => Registry == _Other.Registry && Id == _Other.Id;

        public override bool Equals(object _Obj) => _Obj is TagKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Registry, Id);

        public override string ToString() => $"{Registry}:{Id}";
    }
}
