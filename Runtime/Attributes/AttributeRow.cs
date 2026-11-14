using System;
using System.Globalization;

using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// What an attribute's clamp bound is.
    /// </summary>
    public enum AttributeBoundKind : byte
    {
        /// <summary> No bound. </summary>
        None,

        /// <summary> A constant value. </summary>
        Constant,

        /// <summary> The current value of another attribute. </summary>
        Attribute,
    }



    /// <summary>
    /// Flags of an attribute in its registry.
    /// </summary>
    [Flags]
    public enum AttributeFlags : byte
    {
        None = 0,

        /// <summary>
        /// A scratch value, e.g. incoming damage: no bounds, no change events, reset to its default after each
        /// execution.
        /// </summary>
        Meta = 1 << 0,

        /// <summary>
        /// Rounded to the nearest integer after evaluation.
        /// </summary>
        Integer = 1 << 1,
    }



    /// <summary>
    /// What happens to an attribute when the attribute that is its <c>max</c> bound changes.
    /// </summary>
    public enum AttributeMaxChange : byte
    {
        /// <summary>
        /// The value is clamped to the new maximum, and otherwise kept.
        /// </summary>
        Clamp,

        /// <summary>
        /// The value keeps its ratio to the maximum, e.g. 50 of 100 becomes 75 of 150.
        /// </summary>
        KeepRatio,
    }



    /// <summary>
    /// A clamp bound as stored in a registry: none, a constant, or a reference to another attribute by
    /// (registry ID, attribute ID).
    /// </summary>
    [Serializable]
    public struct AttributeBound : IEquatable<AttributeBound>
    {
        public static readonly AttributeBound None = default;

        [SerializeField] private AttributeBoundKind kind;
        [SerializeField] private float constant;
        [SerializeField] private RegistryGuid registry;
        [SerializeField] private RegistryGuid id;

        public AttributeBoundKind Kind => kind;

        public bool IsNone => kind == AttributeBoundKind.None;

        public bool IsConstant => kind == AttributeBoundKind.Constant;

        public bool IsAttribute => kind == AttributeBoundKind.Attribute;

        /// <summary>
        /// The value of a constant bound, 0 otherwise.
        /// </summary>
        public float ConstantValue => kind == AttributeBoundKind.Constant ? constant : 0f;

        /// <summary>
        /// The registry of the attribute an attribute bound names, zero otherwise.
        /// </summary>
        public RegistryGuid RegistryId => kind == AttributeBoundKind.Attribute ? registry : RegistryGuid.Zero;

        /// <summary>
        /// The attribute an attribute bound names, zero otherwise.
        /// </summary>
        public RegistryGuid AttributeId => kind == AttributeBoundKind.Attribute ? id : RegistryGuid.Zero;

        /// <summary>
        /// The attribute an attribute bound names, as a reference, or <see cref="GameplayAttribute.None"/>.
        /// </summary>
        public GameplayAttribute Attribute => kind == AttributeBoundKind.Attribute ? GameplayAttribute.FromId(registry, id) : GameplayAttribute.None;



        public static AttributeBound FromConstant(float _Value)
            => new() { kind = AttributeBoundKind.Constant, constant = _Value };

        public static AttributeBound FromAttribute(RegistryGuid _Registry, RegistryGuid _Id)
            => _Id.IsZero ? None : new AttributeBound { kind = AttributeBoundKind.Attribute, registry = _Registry, id = _Id };

        public static AttributeBound FromAttribute(GameplayAttribute _Attribute) => FromAttribute(_Attribute.RegistryId, _Attribute.Id);

        /// <summary>
        /// Whether this bound names the attribute (<paramref name="_Registry"/>, <paramref name="_Id"/>).
        /// </summary>
        public bool Names(RegistryGuid _Registry, RegistryGuid _Id)
            => kind == AttributeBoundKind.Attribute && registry == _Registry && id == _Id;



        public bool Equals(AttributeBound _Other)
            => kind == _Other.kind && kind switch
            {
                AttributeBoundKind.Constant => constant.Equals(_Other.constant),
                AttributeBoundKind.Attribute => registry == _Other.registry && id == _Other.id,
                _ => true,
            };

        public override bool Equals(object _Obj) => _Obj is AttributeBound other && Equals(other);

        public override int GetHashCode() => kind switch
        {
            AttributeBoundKind.Constant => HashCode.Combine(kind, constant),
            AttributeBoundKind.Attribute => HashCode.Combine(kind, registry, id),
            _ => 0,
        };

        public static bool operator ==(AttributeBound _A, AttributeBound _B) => _A.Equals(_B);

        public static bool operator !=(AttributeBound _A, AttributeBound _B) => !_A.Equals(_B);

        /// <summary>
        /// "-", the constant in the invariant culture, or "@&lt;registry&gt;:&lt;attribute&gt;".
        /// </summary>
        public override string ToString() => kind switch
        {
            AttributeBoundKind.Constant => constant.ToString("R", CultureInfo.InvariantCulture),
            AttributeBoundKind.Attribute => $"@{registry}:{id}",
            _ => "-",
        };
    }



    /// <summary>
    /// An attribute's own data in a <see cref="GameplayAttributeDatabase"/>, stored parallel to the registry rows.
    /// The attribute's ID, name and registry are the row's ID, name and registry.
    /// </summary>
    [Serializable]
    public struct AttributeRow
    {
        [SerializeField] internal float defaultValue;
        [SerializeField] internal AttributeBound min;
        [SerializeField] internal AttributeBound max;
        [SerializeField] internal AttributeMaxChange onMaxChange;
        [SerializeField] internal AttributeFlags flags;

        /// <summary>
        /// The base value a unit starts with when its set definition gives none.
        /// </summary>
        public float DefaultValue => defaultValue;

        public AttributeBound Min => min;

        public AttributeBound Max => max;

        public AttributeMaxChange OnMaxChange => onMaxChange;

        public AttributeFlags Flags => flags;

        public AttributeRow(float _DefaultValue, AttributeBound _Min = default, AttributeBound _Max = default,
                            AttributeMaxChange _OnMaxChange = AttributeMaxChange.Clamp, AttributeFlags _Flags = AttributeFlags.None)
        {
            defaultValue = _DefaultValue;
            min = _Min;
            max = _Max;
            onMaxChange = _OnMaxChange;
            flags = _Flags;
        }
    }



    /// <summary>
    /// A clamp bound as the runtime uses it: none, a constant, or a live attribute's handle. An attribute bound that
    /// doesn't resolve (a deleted attribute, an uninstalled package) is ignored, so it reads as none.
    /// </summary>
    public readonly struct AttributeLimit : IEquatable<AttributeLimit>
    {
        public static readonly AttributeLimit None = default;

        public readonly AttributeBoundKind Kind;

        /// <summary>
        /// The value of a constant limit.
        /// </summary>
        public readonly float Constant;

        /// <summary>
        /// The bounding attribute of an attribute limit, <see cref="AttributeHandle.None"/> otherwise.
        /// </summary>
        public AttributeHandle Handle => Kind == AttributeBoundKind.Attribute ? new AttributeHandle(handle) : AttributeHandle.None;

        // Stored as is, so default(AttributeLimit) is None with no handle.
        private readonly int handle;

        public bool IsNone => Kind == AttributeBoundKind.None;

        private AttributeLimit(AttributeBoundKind _Kind, float _Constant, int _Handle)
        {
            Kind = _Kind;
            Constant = _Constant;
            handle = _Handle;
        }

        public static AttributeLimit FromConstant(float _Value) => new(AttributeBoundKind.Constant, _Value, -1);

        public static AttributeLimit FromAttribute(AttributeHandle _Handle)
            => _Handle.IsValid ? new AttributeLimit(AttributeBoundKind.Attribute, 0f, _Handle.Index) : None;

        public bool Equals(AttributeLimit _Other) => Kind == _Other.Kind && Constant.Equals(_Other.Constant) && handle == _Other.handle;

        public override bool Equals(object _Obj) => _Obj is AttributeLimit other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Constant, handle);

        public override string ToString() => Kind switch
        {
            AttributeBoundKind.Constant => Constant.ToString("R", CultureInfo.InvariantCulture),
            AttributeBoundKind.Attribute => $"@{handle}",
            _ => "None",
        };
    }
}
