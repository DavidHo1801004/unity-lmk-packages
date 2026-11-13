using System;

using LMK.Editor.Registry;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// One tag row of a registry file, after its ID: the columns parent, segment, flags and description.
    /// The content of a file is a <see cref="RegistryData{TRow}"/> of these rows.
    /// </summary>
    [Serializable]
    internal struct TagRegistryRow
    {
        /// <summary>
        /// Parent row ID, or zero for a top level tag. Never changes once the row exists.
        /// </summary>
        public RegistryGuid parent;

        public string segment;

        /// <summary>
        /// Whether the row was only created as the parent of an added tag.
        /// </summary>
        public bool isImplicit;

        public string description;

        public TagRegistryRow(RegistryGuid _Parent, string _Segment, bool _IsImplicit, string _Description = null)
        {
            parent = _Parent;
            segment = _Segment;
            isImplicit = _IsImplicit;
            description = _Description ?? string.Empty;
        }

        /// <summary>
        /// A registry row with its ID.
        /// </summary>
        public static RegistryRow<TagRegistryRow> Create(RegistryGuid _Id, RegistryGuid _Parent, string _Segment, bool _IsImplicit, string _Description = null)
            => new(_Id, new TagRegistryRow(_Parent, _Segment, _IsImplicit, _Description));
    }
}
