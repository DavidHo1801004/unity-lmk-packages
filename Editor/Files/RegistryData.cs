using System;
using System.Collections.Generic;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// One row of a registry file: the ID the core owns, and the kind's data.
    /// </summary>
    [Serializable]
    public struct RegistryRow<TRow> where TRow : struct
    {
        public RegistryGuid id;
        public TRow data;

        public RegistryRow(RegistryGuid _Id, TRow _Data)
        {
            id = _Id;
            data = _Data;
        }
    }



    /// <summary>
    /// Content of one registry file: its header and its rows.
    /// </summary>
    [Serializable]
    public sealed class RegistryData<TRow> where TRow : struct
    {
        public RegistryGuid registryId;

        /// <summary>
        /// Display name, e.g. "Main" or the package name.
        /// </summary>
        public string name = string.Empty;

        public RegistryOrigin origin;

        /// <summary>
        /// For <see cref="RegistryOrigin.Generated"/> registries: the package they were generated for. The
        /// registry is loaded only while that package is installed.
        /// </summary>
        public string package = string.Empty;

        /// <summary>
        /// For <see cref="RegistryOrigin.Generated"/> registries: the package version they were generated from.
        /// Another installed version offers to regenerate.
        /// </summary>
        public string version = string.Empty;

        public List<RegistryRow<TRow>> rows = new();

        /// <summary>
        /// A copy that shares no list with this one. Row data is copied by value.
        /// </summary>
        public RegistryData<TRow> Clone()
        {
            return new RegistryData<TRow>
            {
                registryId = registryId,
                name = name,
                origin = origin,
                package = package,
                version = version,
                rows = new List<RegistryRow<TRow>>(rows),
            };
        }

        /// <summary>
        /// Index of the row with ID <paramref name="_Id"/> in <see cref="rows"/>, or -1.
        /// </summary>
        public int IndexOf(RegistryGuid _Id)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].id.Equals(_Id))
                    return i;

            return -1;
        }
    }



    /// <summary>
    /// Result of parsing a registry file.
    /// </summary>
    public sealed class RegistryParseResult<TRow> where TRow : struct
    {
        /// <summary>
        /// Parsed content. Kept even when the file is rejected, for diagnostics.
        /// </summary>
        public RegistryData<TRow> Data;

        /// <summary>
        /// Problems that make the file unusable. The registry is rejected if there is any.
        /// </summary>
        public readonly List<string> Errors = new();

        /// <summary>
        /// Problems that don't stop the registry from loading, e.g. a checksum mismatch.
        /// </summary>
        public readonly List<string> Warnings = new();

        /// <summary>
        /// Whether the declared checksum differs from the rows, i.e. the file was edited outside the editor.
        /// </summary>
        public bool ChecksumMismatch;

        public bool IsValid => Data != null && Errors.Count == 0;
    }
}
