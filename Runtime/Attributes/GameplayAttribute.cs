using System;

using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// A reference to one gameplay attribute, such as "Vitals.Health". <br/>
    /// Its identity is (registry ID, attribute ID), so it survives renames. The stored name is only used for display
    /// when the attribute can't be resolved.
    /// </summary>
    /// <remarks>
    /// Attributes have no hierarchy, so equality has one meaning: same registry and attribute ID. It is independent
    /// of the database, and <c>operator ==</c> compares the same way. <br/>
    /// An attribute that doesn't resolve has the handle <see cref="AttributeHandle.None"/>, and every operation on
    /// it is a no-op. <br/>
    /// <b>FROZEN LAYOUT:</b> renaming or reordering the serialized fields breaks every saved reference.
    /// </remarks>
    [Serializable]
    public struct GameplayAttribute : IEquatable<GameplayAttribute>, IRegistryReference
    {
        /// <summary>
        /// Separator between an attribute's group and its own name, e.g. "Vitals.Health".
        /// </summary>
        public const char SEPARATOR = '.';

        /// <summary>
        /// The unassigned attribute. It resolves to nothing and is not a missing reference.
        /// </summary>
        public static readonly GameplayAttribute None = default;

        [SerializeField]
        private RegistryGuid _registry;

        [SerializeField]
        private RegistryGuid _id;

        [SerializeField]
        private string _lastKnownName;

        /// <summary>
        /// ID of the registry the attribute belongs to. Always explicit, never implied.
        /// </summary>
        public RegistryGuid RegistryId => _registry;

        /// <summary>
        /// ID of the attribute in its registry. Zero for <see cref="None"/>.
        /// </summary>
        public RegistryGuid Id => _id;

        /// <summary>
        /// The name when the attribute was assigned. Display and diagnostics only: it never affects resolution.
        /// </summary>
        public string LastKnownName => _lastKnownName ?? string.Empty;

        /// <summary>
        /// Whether the attribute is unassigned.
        /// </summary>
        public bool IsNone => _id.IsZero;

        /// <summary>
        /// Resolve against the active database.
        /// </summary>
        public AttributeResolution Resolve() => GameplayAttributeDatabase.ResolveActive(_registry, _id);

        /// <summary>
        /// The live attribute's handle, or <see cref="AttributeHandle.None"/> if it doesn't resolve. Never serialize it.
        /// </summary>
        public AttributeHandle Handle => Resolve().Handle;

        /// <summary>
        /// Whether the attribute resolves to a live attribute.
        /// </summary>
        public bool IsLive => Resolve().IsLive;

        /// <summary>
        /// The current name if the attribute is live. Otherwise, <see cref="LastKnownName"/>.
        /// </summary>
        public string Name
        {
            get
            {
                var resolution = Resolve();
                return resolution.IsLive ? GameplayAttributeDatabase.Active.GetName(resolution.Handle) : LastKnownName;
            }
        }



        internal GameplayAttribute(RegistryGuid _Registry, RegistryGuid _Id, string _LastKnownName)
        {
            _registry = _Registry;
            _id = _Id;
            _lastKnownName = _LastKnownName;
        }

        /// <summary>
        /// A reference from its IDs. Used by generated code and save data.
        /// </summary>
        public static GameplayAttribute FromId(RegistryGuid _Registry, RegistryGuid _Id, string _LastKnownName = null)
            => _Id.IsZero ? None : new GameplayAttribute(_Registry, _Id, _LastKnownName);

        /// <summary>
        /// The live attribute currently named <paramref name="_Name"/> in a registry, or <see cref="None"/>. <br/>
        /// <b>Debug and console use only:</b> a name lookup does not survive renames.
        /// </summary>
        public static GameplayAttribute FromName(RegistryGuid _Registry, string _Name)
        {
            var database = GameplayAttributeDatabase.Active;
            return database != null ? database.GetAttribute(database.FindAttribute(_Registry, _Name)) : None;
        }

        /// <summary>
        /// The live attribute currently named <paramref name="_Name"/> in the main registry, or <see cref="None"/>. <br/>
        /// <b>Debug and console use only:</b> a name lookup does not survive renames.
        /// </summary>
        public static GameplayAttribute FromName(string _Name)
        {
            var database = GameplayAttributeDatabase.Active;
            return database != null ? FromName(database.MainRegistryId, _Name) : None;
        }



        #region Equality
        /// <summary>
        /// Same registry and attribute ID. Independent of the database.
        /// </summary>
        public bool Equals(GameplayAttribute _Other)
            => _registry.Equals(_Other._registry) && _id.Equals(_Other._id);

        public override bool Equals(object _Obj)
            => _Obj is GameplayAttribute other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(_registry, _id);

        public static bool operator ==(GameplayAttribute _A, GameplayAttribute _B) => _A.Equals(_B);

        public static bool operator !=(GameplayAttribute _A, GameplayAttribute _B) => !_A.Equals(_B);

        public override string ToString()
            => IsNone ? "None" : Name;
        #endregion
    }
}
