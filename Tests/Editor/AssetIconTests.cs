using System.IO;
using System.Linq;

using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// Tag registries and tag files show their own icons in the Project window and in their Inspector header.
    /// </summary>
    public sealed class AssetIconTests : OperationTestBase
    {
        private const string ICONS = "Packages/com.lmk.gameplaytags/Editor/Assets/Icons/";

        [TestCase("TagRegistry")]
        [TestCase("TagFile")]
        public void IconFile_IsAnImportedTexture(string _Name)
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(ICONS + _Name + ".png"),
                             $"{_Name}.png is not imported: check its .meta in the Console.");
        }

        [Test]
        public void TagRegistry_ShowsItsIcon()
        {
            AssetDatabase.ImportAsset(MAIN, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            AssertShowsIcon(MAIN, "TagRegistry");
        }

        [Test]
        public void TagFile_ShowsItsIcon()
        {
            AssertShowsIcon(CreateTagFile(), "TagFile");
        }

        [Test]
        public void TagRegistry_InspectorHeaderShowsItsIcon()
        {
            AssetDatabase.ImportAsset(MAIN, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            AssertHeaderShowsIcon(MAIN, "TagRegistry");
        }

        [Test]
        public void TagFile_InspectorHeaderShowsItsIcon()
        {
            AssertHeaderShowsIcon(CreateTagFile(), "TagFile");
        }

        private static string CreateTagFile()
        {
            string path = $"{FOLDER}/Icons.{TagFile.EXTENSION}";
            File.WriteAllText(path, "Ability.Fire ; Fire abilities\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        // Importers store a copy of their icon with the import result, so the Project window's icon is compared by image.
        private static void AssertShowsIcon(string _AssetPath, string _Icon)
        {
            var expected = AssetDatabase.LoadAssetAtPath<Texture2D>(ICONS + _Icon + ".png");
            Assert.IsNotNull(expected, $"{_Icon}.png is not imported.");

            var shown = AssetDatabase.GetCachedIcon(_AssetPath);
            Assert.IsNotNull(shown, "no icon");
            Assert.IsTrue(Pixels(shown).SequenceEqual(Pixels(expected)),
                          $"\"{_AssetPath}\" shows \"{shown.name}\" ({AssetDatabase.GetAssetPath(shown)}), not {_Icon}.png.");
        }

        // An importer's Inspector header shows the imported object's mini thumbnail, which comes from its class's icon.
        private static void AssertHeaderShowsIcon(string _AssetPath, string _Icon)
        {
            var expected = AssetDatabase.LoadAssetAtPath<Texture2D>(ICONS + _Icon + ".png");
            Assert.IsNotNull(expected, $"{_Icon}.png is not imported.");

            var asset = AssetDatabase.LoadMainAssetAtPath(_AssetPath);
            Assert.IsNotNull(asset, $"\"{_AssetPath}\" is not imported.");

            var shown = AssetPreview.GetMiniThumbnail(asset);
            Assert.IsNotNull(shown, "no header icon");
            Assert.IsTrue(Pixels(shown).SequenceEqual(Pixels(expected)),
                          $"The Inspector header of \"{_AssetPath}\" shows \"{shown.name}\", not {_Icon}.png.");
        }

        private static Color32[] Pixels(Texture _Texture)
        {
            var target = RenderTexture.GetTemporary(_Texture.width, _Texture.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(_Texture, target);
                RenderTexture.active = target;
                var copy = new Texture2D(_Texture.width, _Texture.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, _Texture.width, _Texture.height), 0, 0);
                var pixels = copy.GetPixels32();
                Object.DestroyImmediate(copy);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
