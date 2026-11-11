using System.IO;

using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

using LMK.Editor.Utilities;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Imports registry files (<c>*.tagregistry</c>). Parses and validates them, and reports problems.
    /// Never writes anything.
    /// </summary>
    /// <remarks>
    /// Imported early, so registries are ready before assets that reference tags are processed.
    /// </remarks>
    [ScriptedImporter(1, TagRegistryFile.EXTENSION, -1000)]
    internal sealed class TagRegistryImporter : ScriptedImporter
    {
        private const string ICON_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/Sprites/tag-multiple_white-64.png";

        public override void OnImportAsset(AssetImportContext _Context)
        {
            string text = File.ReadAllText(_Context.assetPath);
            var result = TagRegistryFile.Parse(text);

            var asset = ScriptableObject.CreateInstance<TagRegistryAsset>();
            asset.Initialize(result, TagRegistryFile.HashText(text));

            foreach (string error in result.Errors)
                _Context.LogImportError($"{_Context.assetPath}: {error}");

            foreach (string warning in result.Warnings)
                _Context.LogImportWarning($"{_Context.assetPath}: {warning}");

            _Context.AddObjectToAsset("main", asset, LMKAssetUtility.Load<Texture2D>(ICON_PATH));
            _Context.SetMainObject(asset);
        }
    }



    /// <summary>
    /// Reloads the registries when a registry file changes. Read-only.
    /// </summary>
    internal sealed class TagRegistryPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] _ImportedAssets,
            string[] _DeletedAssets,
            string[] _MovedAssets,
            string[] _MovedFromAssetPaths,
            bool _DidDomainReload)
        {
            if (!ContainsRegistry(_ImportedAssets)
                && !ContainsRegistry(_DeletedAssets)
                && !ContainsRegistry(_MovedAssets)
                && !ContainsRegistry(_MovedFromAssetPaths))
                return;

            TagRegistryService.Reload();

            // A main registry changed by something else than TagOperations (a pull, a revert):
            // its undo history no longer matches the file.
            foreach (string path in _ImportedAssets)
                if (path == TagRegistryService.MainRegistryPath)
                    TagOperations.OnMainRegistryImported();
        }

        private static bool ContainsRegistry(string[] _Paths)
        {
            foreach (string path in _Paths)
                if (path.EndsWith("." + TagRegistryFile.EXTENSION, System.StringComparison.OrdinalIgnoreCase))
                    return true;

            return false;
        }
    }
}
