using System;
using System.Collections.Generic;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// One tag row of a registry file.
    /// </summary>
    [Serializable]
    internal struct TagRegistryRow
    {
        public TagGuid id;

        /// <summary>
        /// Parent row ID, or zero for a top level tag. Never changes once the row exists.
        /// </summary>
        public TagGuid parent;

        public string segment;

        /// <summary>
        /// Whether the row was only created as the parent of an added tag.
        /// </summary>
        public bool isImplicit;

        public string description;

        public TagRegistryRow(TagGuid _Id, TagGuid _Parent, string _Segment, bool _IsImplicit, string _Description = null)
        {
            id = _Id;
            parent = _Parent;
            segment = _Segment;
            isImplicit = _IsImplicit;
            description = _Description ?? string.Empty;
        }
    }



    /// <summary>
    /// Content of one registry file: its header and its rows.
    /// </summary>
    [Serializable]
    internal sealed class TagRegistryData
    {
        public TagGuid registryId;

        /// <summary>
        /// Display name, e.g. "Main" or the package name.
        /// </summary>
        public string name = string.Empty;

        public TagRegistryKind kind;

        /// <summary>
        /// For <see cref="TagRegistryKind.Generated"/> registries: the package they were generated for. The
        /// registry is loaded only while that package is installed.
        /// </summary>
        public string package = string.Empty;

        /// <summary>
        /// For <see cref="TagRegistryKind.Generated"/> registries: the package version they were generated from.
        /// Another installed version offers to regenerate.
        /// </summary>
        public string version = string.Empty;

        public List<TagRegistryRow> rows = new();

        public TagRegistryData Clone()
        {
            return new TagRegistryData
            {
                registryId = registryId,
                name = name,
                kind = kind,
                package = package,
                version = version,
                rows = new List<TagRegistryRow>(rows),
            };
        }

        public int IndexOf(TagGuid _Id)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].id.Equals(_Id))
                    return i;

            return -1;
        }
    }
}
