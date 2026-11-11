using System;

using UnityEngine;

namespace LMK.Runtime.GameplayTags
{
    /// <summary>
    /// A reference to one gameplay tag, such as "Status.Debuff.Stun". <br/>
    /// Its identity is (registry ID, tag ID), so it survives renames. The stored name is only used for display
    /// when the tag can't be resolved.
    /// </summary>
    /// <remarks>
    /// Two kinds of comparison exist, and code has to choose one: <br/>
    /// - Data equality (<see cref="Equals(GameplayTag)"/>): same registry and tag ID. Stable, safe as a key. <br/>
    /// - Gameplay matching (<see cref="MatchesTag"/>, <see cref="MatchesTagExact"/>): resolved handles.
    ///   Unresolved tags never match anything, not even each other. <br/>
    /// There is deliberately no <c>operator ==</c>. <br/>
    /// <b>FROZEN LAYOUT:</b> renaming or reordering the serialized fields breaks every saved reference.
    /// </remarks>
    [Serializable]
    public struct GameplayTag : IEquatable<GameplayTag>
    {
        /// <summary>
        /// Separator between the segments of a tag path.
        /// </summary>
        public const char SEPARATOR = TagPath.SEPARATOR;

        /// <summary>
        /// The unassigned tag. It matches nothing and is not a missing reference.
        /// </summary>
        public static readonly GameplayTag None = default;

        [SerializeField]
        private TagGuid _registry;

        [SerializeField]
        private TagGuid _id;

        [SerializeField]
        private string _lastKnownName;

        /// <summary>
        /// ID of the registry the tag belongs to. Always explicit, never implied.
        /// </summary>
        public TagGuid RegistryId => _registry;

        /// <summary>
        /// ID of the tag in its registry. Zero for <see cref="None"/>.
        /// </summary>
        public TagGuid Id => _id;

        /// <summary>
        /// The path when the tag was assigned. Display and diagnostics only: it never affects resolution.
        /// </summary>
        public string LastKnownName => _lastKnownName ?? string.Empty;

        /// <summary>
        /// Whether the tag is unassigned.
        /// </summary>
        public bool IsNone => _id.IsZero;

        /// <summary>
        /// Resolve against the active database.
        /// </summary>
        public TagResolution Resolve() => GameplayTagDatabase.ResolveActive(_registry, _id);

        /// <summary>
        /// The live tag's handle, or <see cref="TagHandle.None"/> if the tag doesn't resolve.
        /// </summary>
        public TagHandle Handle => Resolve().Handle;

        /// <summary>
        /// Whether the tag resolves to a live tag.
        /// </summary>
        public bool IsLive => Resolve().IsLive;

        /// <summary>
        /// The current path if the tag is live. Otherwise, <see cref="LastKnownName"/>.
        /// </summary>
        public string Path
        {
            get
            {
                var resolution = Resolve();
                return resolution.IsLive ? GameplayTagDatabase.Active.GetPath(resolution.Handle) : LastKnownName;
            }
        }

        /// <summary>
        /// The direct parent of a live tag, e.g. "A.B" for "A.B.C". <see cref="None"/> for top level or unresolved tags.
        /// </summary>
        public GameplayTag Parent
        {
            get
            {
                var resolution = Resolve();
                if (!resolution.IsLive) return None;

                var database = GameplayTagDatabase.Active;
                return database.GetTag(database.GetParent(resolution.Handle));
            }
        }



        internal GameplayTag(TagGuid _Registry, TagGuid _Id, string _LastKnownName)
        {
            _registry = _Registry;
            _id = _Id;
            _lastKnownName = _LastKnownName;
        }

        /// <summary>
        /// A reference from its IDs. Used by generated code and save data.
        /// </summary>
        public static GameplayTag FromId(TagGuid _Registry, TagGuid _Id, string _LastKnownName = null)
            => _Id.IsZero ? None : new GameplayTag(_Registry, _Id, _LastKnownName);

        /// <summary>
        /// The live tag currently at <paramref name="_Path"/> in a registry, or <see cref="None"/>. <br/>
        /// <b>Debug and console use only:</b> a path lookup does not survive renames.
        /// </summary>
        public static GameplayTag FromName(TagGuid _Registry, string _Path)
        {
            var database = GameplayTagDatabase.Active;
            return database != null ? database.GetTag(database.FindByPath(_Registry, _Path)) : None;
        }

        /// <summary>
        /// The live tag currently at <paramref name="_Path"/> in the main registry, or <see cref="None"/>. <br/>
        /// <b>Debug and console use only:</b> a path lookup does not survive renames.
        /// </summary>
        public static GameplayTag FromName(string _Path)
        {
            var database = GameplayTagDatabase.Active;
            return database != null ? FromName(database.MainRegistryId, _Path) : None;
        }



        #region Matching
        /// <summary>
        /// Whether this tag is <paramref name="_Parent"/> or one of its descendants. <br/>
        /// E.g. "A.B.C" matches "A.B.C", "A.B" and "A". Never matches across registries or for unresolved tags.
        /// </summary>
        public bool MatchesTag(GameplayTag _Parent)
        {
            var self = ResolveForGameplay();
            var parent = _Parent.ResolveForGameplay();
            if (!self.IsValid || !parent.IsValid) return false;

            return GameplayTagDatabase.Active.IsSameOrDescendantOf(self, parent);
        }

        /// <summary>
        /// Whether both tags resolve to the same live tag.
        /// </summary>
        public bool MatchesTagExact(GameplayTag _Other)
        {
            var self = ResolveForGameplay();
            return self.IsValid && self == _Other.ResolveForGameplay();
        }

        private TagHandle ResolveForGameplay()
        {
            var resolution = Resolve();
            if (resolution.IsMissing)
                GameplayTagDatabase.ReportUnresolved(this, resolution.Status);

            return resolution.Handle;
        }
        #endregion



        #region Equality
        /// <summary>
        /// Data equality: same registry and tag ID. Independent of the database.
        /// </summary>
        public bool Equals(GameplayTag _Other)
            => _registry.Equals(_Other._registry) && _id.Equals(_Other._id);

        public override bool Equals(object _Obj)
            => _Obj is GameplayTag other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(_registry, _id);

        public override string ToString()
            => IsNone ? "None" : Path;
        #endregion
    }
}
