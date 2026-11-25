using System.IO;

using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Imports tag files (<c>*.tags</c>) so the Project window and the inspector recognize them. Problems in the
    /// file are reported as warnings. Stores no tags and never writes anything: importing a tag file into the
    /// main registry is a separate, explicit command.
    /// </summary>
    [ScriptedImporter(3, TagFile.EXTENSION)]
    internal sealed class TagFileImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext _Context)
        {
            string text = File.ReadAllText(_Context.assetPath);
            var result = TagFile.Parse(text, Path.GetFileName(_Context.assetPath));

            // Warnings, not errors: a tag file is import data, and a bad line only blocks importing it.
            foreach (string error in result.Errors)
                _Context.LogImportWarning(error);

            foreach (string warning in result.Warnings)
                _Context.LogImportWarning(warning);

            // A dependency, so the icon is there whatever imports first, and follows changes to the texture.
            _Context.DependsOnArtifact(TagFileAsset.ICON_PATH);
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(TagFileAsset.ICON_PATH);
            if (icon == null)
                icon = EditorGUIUtility.IconContent("TextAsset Icon").image as Texture2D;

            var asset = ScriptableObject.CreateInstance<TagFileAsset>();
            _Context.AddObjectToAsset("main", asset, icon);
            _Context.SetMainObject(asset);
        }
    }
}
