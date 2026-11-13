using System;
using System.Globalization;

using UnityEditor;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// A row reference as stored in an asset: its kind, (registry ID, row ID) and last known name.
    /// </summary>
    /// <remarks>
    /// A zero <see cref="Registry"/> means the registry is unknown: a prefab instance override that changed only
    /// the row ID stores no registry, since that field kept the prefab's value. Such a reference matches the row
    /// ID in any registry of its kind. Row IDs are random 128-bit values, so this never matches the wrong row in
    /// practice.
    /// </remarks>
    public readonly struct RegistryReference : IEquatable<RegistryReference>
    {
        /// <summary>
        /// The kind of the reference. Never <see langword="null"/> once a scan is complete.
        /// </summary>
        public readonly RegistryKind Kind;

        public readonly RegistryGuid Registry;
        public readonly RegistryGuid Id;
        public readonly string Name;

        public RegistryReference(RegistryKind _Kind, RegistryGuid _Registry, RegistryGuid _Id, string _Name)
        {
            Kind = _Kind;
            Registry = _Registry;
            Id = _Id;
            Name = _Name ?? string.Empty;
        }

        public bool HasRegistry => !Registry.IsZero;

        /// <summary>
        /// Whether this reference refers to row (<paramref name="_Registry"/>, <paramref name="_Id"/>) of its kind.
        /// </summary>
        public bool Matches(RegistryGuid _Registry, RegistryGuid _Id)
            => Id == _Id && (Registry.IsZero || Registry == _Registry);

        public RegistryReference WithKind(RegistryKind _Kind) => new(_Kind, Registry, Id, Name);

        /// <summary>
        /// Data identity: kind, registry and row ID. The name is not compared.
        /// </summary>
        public bool Equals(RegistryReference _Other) => Kind == _Other.Kind && Registry == _Other.Registry && Id == _Other.Id;

        public override bool Equals(object _Obj) => _Obj is RegistryReference other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind?.Id, Registry, Id);

        public override string ToString() => $"{Kind?.Id ?? "?"}:{Registry}:{Id}";
    }



    /// <summary>
    /// Reads and writes reference properties of every kind. Every reference struct has the same frozen layout:
    /// <c>_registry</c> and <c>_id</c>, each <c>_hi</c> and <c>_lo</c>, then <c>_lastKnownName</c>.
    /// </summary>
    public static class RegistryProperties
    {
        public const string REGISTRY_PROPERTY = "_registry";
        public const string ID_PROPERTY = "_id";
        public const string NAME_PROPERTY = "_lastKnownName";
        public const string HI_PROPERTY = "_hi";
        public const string LO_PROPERTY = "_lo";

        /// <summary>
        /// The kind whose reference struct <paramref name="_Property"/> holds, or <see langword="null"/> if it holds none.
        /// </summary>
        public static RegistryKind KindOf(SerializedProperty _Property)
        {
            if (_Property == null || _Property.propertyType != SerializedPropertyType.Generic || _Property.isArray) return null;

            foreach (var kind in RegistryKinds.All)
            {
                if (_Property.type != kind.ReferenceType.Name) continue;

                return _Property.FindPropertyRelative(ID_PROPERTY) != null && _Property.FindPropertyRelative(REGISTRY_PROPERTY) != null
                    ? kind
                    : null;
            }

            return null;
        }

        public static bool IsReference(SerializedProperty _Property) => KindOf(_Property) != null;

        /// <summary>
        /// Read a reference property of <paramref name="_Kind"/>.
        /// </summary>
        public static RegistryReference Read(SerializedProperty _Property, RegistryKind _Kind)
        {
            var registry = ReadGuid(_Property.FindPropertyRelative(REGISTRY_PROPERTY));
            var id = ReadGuid(_Property.FindPropertyRelative(ID_PROPERTY));

            return new RegistryReference(_Kind, registry, id, _Property.FindPropertyRelative(NAME_PROPERTY)?.stringValue);
        }

        /// <summary>
        /// Write all three fields of a reference property. A zero <paramref name="_Id"/> writes None: no registry and
        /// no name. Does not apply the modified properties.
        /// </summary>
        public static void Write(SerializedProperty _Property, RegistryGuid _Registry, RegistryGuid _Id, string _Name)
        {
            bool none = _Id.IsZero;
            WriteGuid(_Property.FindPropertyRelative(REGISTRY_PROPERTY), none ? RegistryGuid.Zero : _Registry);
            WriteGuid(_Property.FindPropertyRelative(ID_PROPERTY), _Id);

            var name = _Property.FindPropertyRelative(NAME_PROPERTY);
            if (name != null)
                name.stringValue = none ? string.Empty : _Name ?? string.Empty;
        }

        private static RegistryGuid ReadGuid(SerializedProperty _Property)
            => new(_Property.FindPropertyRelative(HI_PROPERTY).ulongValue, _Property.FindPropertyRelative(LO_PROPERTY).ulongValue);

        private static void WriteGuid(SerializedProperty _Property, RegistryGuid _Value)
        {
            _Property.FindPropertyRelative(HI_PROPERTY).ulongValue = _Value.Hi;
            _Property.FindPropertyRelative(LO_PROPERTY).ulongValue = _Value.Lo;
        }

        internal static RegistryGuid ParseGuid(string _Hi, string _Lo)
            => new(ulong.Parse(_Hi, NumberStyles.None, CultureInfo.InvariantCulture), ulong.Parse(_Lo, NumberStyles.None, CultureInfo.InvariantCulture));
    }
}
