using System;

using LMK.Editor.Registry;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// One attribute row of a registry file, after its ID: the columns name, default, min, max, onMaxChange, flags
    /// and description. The content of a file is a <see cref="RegistryData{TRow}"/> of these rows.
    /// </summary>
    [Serializable]
    internal struct AttributeRegistryRow
    {
        /// <summary>
        /// The full name, e.g. "Vitals.Health": the last segment is the attribute, the rest its group.
        /// </summary>
        public string name;

        public float defaultValue;

        public AttributeBound min;

        public AttributeBound max;

        public AttributeMaxChange onMaxChange;

        public AttributeFlags flags;

        public string description;

        public bool IsMeta => (flags & AttributeFlags.Meta) != 0;

        public AttributeRegistryRow(string _Name, float _DefaultValue = 0f, AttributeBound _Min = default, AttributeBound _Max = default,
                                    AttributeMaxChange _OnMaxChange = AttributeMaxChange.Clamp, AttributeFlags _Flags = AttributeFlags.None,
                                    string _Description = null)
        {
            name = _Name;
            defaultValue = _DefaultValue;
            min = _Min;
            max = _Max;
            onMaxChange = _OnMaxChange;
            flags = _Flags;
            description = _Description ?? string.Empty;
        }

        /// <summary>
        /// The row's data in the runtime database.
        /// </summary>
        public AttributeRow ToRuntime() => new(defaultValue, min, max, onMaxChange, flags);

        /// <summary>
        /// A registry row with its ID.
        /// </summary>
        public static RegistryRow<AttributeRegistryRow> Create(RegistryGuid _Id, string _Name, float _DefaultValue = 0f,
                                                               AttributeBound _Min = default, AttributeBound _Max = default,
                                                               AttributeMaxChange _OnMaxChange = AttributeMaxChange.Clamp,
                                                               AttributeFlags _Flags = AttributeFlags.None, string _Description = null)
            => new(_Id, new AttributeRegistryRow(_Name, _DefaultValue, _Min, _Max, _OnMaxChange, _Flags, _Description));
    }



    /// <summary>
    /// Attribute names: segments of letters, digits and '_', separated by '.'. The last segment is the attribute;
    /// the rest is its group, a display prefix with no meaning of its own.
    /// </summary>
    internal static class AttributeName
    {
        public static bool IsValid(string _Name, out string _Error)
        {
            _Error = null;
            if (string.IsNullOrEmpty(_Name))
            {
                _Error = "An attribute needs a name.";
                return false;
            }

            foreach (string segment in _Name.Split(GameplayAttribute.SEPARATOR))
            {
                if (segment.Length == 0)
                {
                    _Error = $"\"{_Name}\" has an empty segment. Separate segments with a single '.'.";
                    return false;
                }

                foreach (char c in segment)
                {
                    if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_') continue;

                    _Error = $"\"{_Name}\" contains '{c}'. Use letters, digits, '_' and '.' only.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The group of a name, e.g. "Vitals" for "Vitals.Health", or empty.
        /// </summary>
        public static string GetGroup(string _Name)
        {
            int index = _Name?.LastIndexOf(GameplayAttribute.SEPARATOR) ?? -1;
            return index > 0 ? _Name[..index] : string.Empty;
        }

        /// <summary>
        /// The last segment of a name, e.g. "Health" for "Vitals.Health".
        /// </summary>
        public static string GetShortName(string _Name)
        {
            if (string.IsNullOrEmpty(_Name)) return string.Empty;

            int index = _Name.LastIndexOf(GameplayAttribute.SEPARATOR);
            return index >= 0 ? _Name[(index + 1)..] : _Name;
        }
    }
}
