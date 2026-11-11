using System.Collections.Generic;

using UnityEngine;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Imported content of a registry file. Read-only: registry files are only written by
    /// <see cref="TagOperations"/> and explicit commands, never through this asset.
    /// </summary>
    internal sealed class TagRegistryAsset : ScriptableObject
    {
        [SerializeField]
        private TagRegistryData data = new();

        [SerializeField]
        private bool isValid;

        [SerializeField]
        private bool checksumMismatch;

        [SerializeField]
        private string fileHash = string.Empty;

        [SerializeField]
        private List<string> errors = new();

        [SerializeField]
        private List<string> warnings = new();

        /// <summary>
        /// Parsed content, or <see langword="null"/> if the file was rejected.
        /// </summary>
        public TagRegistryData Data => isValid ? data : null;

        public bool IsValid => isValid;

        public bool ChecksumMismatch => checksumMismatch;

        /// <summary>
        /// <see cref="TagRegistryFile.HashText"/> of the file that was imported.
        /// </summary>
        public string FileHash => fileHash;

        public IReadOnlyList<string> Errors => errors;

        public IReadOnlyList<string> Warnings => warnings;



        internal void Initialize(TagRegistryParseResult _Result, string _FileHash)
        {
            data = _Result.Data ?? new TagRegistryData();
            isValid = _Result.IsValid;
            checksumMismatch = _Result.ChecksumMismatch;
            fileHash = _FileHash ?? string.Empty;
            errors = new List<string>(_Result.Errors);
            warnings = new List<string>(_Result.Warnings);
        }
    }
}
