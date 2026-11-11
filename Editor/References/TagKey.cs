using System;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// The identity of a stored tag reference: (registry ID, tag ID), as kept by the reference index.
    /// </summary>
    /// <remarks>
    /// A zero <see cref="Registry"/> means the registry is unknown: a prefab instance override that changed only
    /// the tag ID stores no registry, since that field kept the prefab's value. Such a key matches the tag ID in
    /// any registry. Tag IDs are random 128-bit values, so this never matches the wrong tag in practice.
    /// </remarks>
    internal readonly struct TagKey : IEquatable<TagKey>
    {
        public readonly TagGuid Registry;
        public readonly TagGuid Id;

        public TagKey(TagGuid _Registry, TagGuid _Id)
        {
            Registry = _Registry;
            Id = _Id;
        }

        public static TagKey Of(GameplayTag _Tag) => new(_Tag.RegistryId, _Tag.Id);

        public bool HasRegistry => !Registry.IsZero;

        /// <summary>
        /// Whether this key refers to the tag (<paramref name="_Registry"/>, <paramref name="_Id"/>).
        /// </summary>
        public bool Matches(TagGuid _Registry, TagGuid _Id)
            => Id == _Id && (Registry.IsZero || Registry == _Registry);

        public bool Matches(GameplayTag _Tag) => Matches(_Tag.RegistryId, _Tag.Id);

        /// <summary>
        /// Whether the key resolves to no live tag in <paramref name="_Snapshot"/>: the tag was deleted, or its
        /// registry isn't loaded.
        /// </summary>
        public bool IsMissingIn(TagRegistrySnapshot _Snapshot)
        {
            if (HasRegistry)
                return _Snapshot.Find(Registry)?.Contains(Id) != true;

            foreach (var registry in _Snapshot.Registries)
                if (registry.Contains(Id))
                    return false;

            return true;
        }

        public bool Equals(TagKey _Other) => Registry == _Other.Registry && Id == _Other.Id;

        public override bool Equals(object _Obj) => _Obj is TagKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Registry, Id);

        public override string ToString() => $"{Registry}:{Id}";

        /// <summary>
        /// Parse the <see cref="ToString"/> form.
        /// </summary>
        public static bool TryParse(string _Text, out TagKey _Key)
        {
            _Key = default;
            if (string.IsNullOrEmpty(_Text)) return false;

            int colon = _Text.IndexOf(':');
            if (colon < 0
                || !TagGuid.TryParse(_Text.Substring(0, colon), out var registry)
                || !TagGuid.TryParse(_Text.Substring(colon + 1), out var id)
                || id.IsZero)
                return false;

            _Key = new TagKey(registry, id);
            return true;
        }
    }
}
