using System.Collections.Generic;
using System.IO;

using UnityEditor.AssetImporters;
using UnityEngine;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Imported content of a registry file. Read-only: registry files are only written by the registry editor
    /// and explicit commands, never through this asset.
    /// </summary>
    /// <remarks>
    /// A kind derives a non-generic class from it, in its own file, for Unity to create.
    /// </remarks>
    public abstract class RegistryAsset<TRow> : ScriptableObject where TRow : struct
    {
        [SerializeField]
        private RegistryData<TRow> data = new();

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
        public RegistryData<TRow> Data => isValid ? data : null;

        public bool IsValid => isValid;

        public bool ChecksumMismatch => checksumMismatch;

        /// <summary>
        /// <see cref="RegistryFile.HashText"/> of the file that was imported.
        /// </summary>
        public string FileHash => fileHash;

        public IReadOnlyList<string> Errors => errors;

        public IReadOnlyList<string> Warnings => warnings;



        internal void Initialize(RegistryParseResult<TRow> _Result, string _FileHash)
        {
            data = _Result.Data ?? new RegistryData<TRow>();
            isValid = _Result.IsValid;
            checksumMismatch = _Result.ChecksumMismatch;
            fileHash = _FileHash ?? string.Empty;
            errors = new List<string>(_Result.Errors);
            warnings = new List<string>(_Result.Warnings);
        }
    }



    /// <summary>
    /// Import order of every registry kind: before the assets that reference rows.
    /// </summary>
    public static class RegistryImport
    {
        /// <summary>
        /// The import order to pass to a registry importer's <see cref="ScriptedImporterAttribute"/>.
        /// </summary>
        public const int IMPORT_ORDER = -1000;
    }



    /// <summary>
    /// Imports a kind's registry files: parses and validates them, and reports problems. Never writes anything.
    /// </summary>
    /// <remarks>
    /// A kind derives a non-generic importer and marks it
    /// <c>[ScriptedImporter(version, "&lt;extension&gt;", RegistryImport.IMPORT_ORDER)]</c>.
    /// </remarks>
    public abstract class RegistryImporter<TRow, TAsset> : ScriptedImporter
        where TRow : struct
        where TAsset : RegistryAsset<TRow>
    {
        /// <summary>
        /// The kind's file format.
        /// </summary>
        protected abstract IRegistryFormat<TRow> Format { get; }

        /// <summary>
        /// Icon of the imported asset, or <see langword="null"/> for the default.
        /// </summary>
        protected virtual Texture2D Icon => null;

        public override void OnImportAsset(AssetImportContext _Context)
        {
            string text = File.ReadAllText(_Context.assetPath);
            var result = RegistryFile.Parse(text, Format);

            var asset = ScriptableObject.CreateInstance<TAsset>();
            asset.Initialize(result, RegistryFile.HashText(text));

            foreach (string error in result.Errors)
                _Context.LogImportError($"{_Context.assetPath}: {error}");

            foreach (string warning in result.Warnings)
                _Context.LogImportWarning($"{_Context.assetPath}: {warning}");

            _Context.AddObjectToAsset("main", asset, Icon);
            _Context.SetMainObject(asset);
        }
    }
}
