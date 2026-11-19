using System;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using LMK.Runtime.AbilitySystem;

using Object = UnityEngine.Object;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// Every asset type of the package shows its own icon in the Project window, and attribute registries in their
    /// Inspector header too.
    /// </summary>
    public sealed class AssetIconTests : AttributeKindTestBase
    {
        private const string ICONS = "Packages/com.lmk.abilitysystem/Editor/Assets/Icons/";

        private static readonly string[] ICON_FILES =
        {
            "AttributeSet", "AttributeInitialValues", "AttributeRegistry", "GameplayEffect", "GameplayAbility",
            "GameplayCueSet", "GameplayCueNotify", "AbilitySystemSettings",
        };

        private static IEnumerable<TestCaseData> AssetTypes()
        {
            yield return new TestCaseData(typeof(AttributeSetDefinition), "AttributeSet");
            yield return new TestCaseData(typeof(AttributeInitialValues), "AttributeInitialValues");
            yield return new TestCaseData(typeof(GameplayEffect), "GameplayEffect");
            yield return new TestCaseData(typeof(GameplayAbility), "GameplayAbility");
            yield return new TestCaseData(typeof(GameplayCueSet), "GameplayCueSet");
            yield return new TestCaseData(typeof(GameplayCueNotifyBurst), "GameplayCueNotify");
            yield return new TestCaseData(typeof(AbilitySystemSettings), "AbilitySystemSettings");
        }

        [TestCaseSource(nameof(ICON_FILES))]
        public void IconFile_IsAnImportedTexture(string _Name)
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(ICONS + _Name + ".png"),
                             $"{_Name}.png is not imported: check its .meta in the Console.");
        }

        [TestCaseSource(nameof(AssetTypes))]
        public void Asset_ShowsItsTypeIcon(Type _Type, string _Icon)
        {
            string path = $"{FOLDER}/{_Type.Name}.asset";
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance(_Type), path);

            Assert.AreEqual(ICONS + _Icon + ".png", AssetDatabase.GetAssetPath(AssetDatabase.GetCachedIcon(path)));
        }

        [Test]
        public void AttributeRegistry_ShowsItsIcon()
        {
            string path = Attributes.MainRegistryPath;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            // The importer stores a copy of its icon with the import result, so it is compared by image.
            var expected = AssetDatabase.LoadAssetAtPath<Texture2D>(ICONS + "AttributeRegistry.png");
            Assert.IsNotNull(expected, "AttributeRegistry.png is not imported.");

            var shown = AssetDatabase.GetCachedIcon(path);
            Assert.IsNotNull(shown, "no icon");
            Assert.IsTrue(Pixels(shown).SequenceEqual(Pixels(expected)),
                          $"\"{path}\" shows \"{shown.name}\" ({AssetDatabase.GetAssetPath(shown)}), not AttributeRegistry.png.");
        }

        [Test]
        public void AttributeRegistry_InspectorHeaderShowsItsIcon()
        {
            string path = Attributes.MainRegistryPath;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            // An importer's Inspector header shows the imported object's mini thumbnail, from its class's icon.
            var expected = AssetDatabase.LoadAssetAtPath<Texture2D>(ICONS + "AttributeRegistry.png");
            Assert.IsNotNull(expected, "AttributeRegistry.png is not imported.");

            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            Assert.IsNotNull(asset, $"\"{path}\" is not imported.");

            var shown = AssetPreview.GetMiniThumbnail(asset);
            Assert.IsNotNull(shown, "no header icon");
            Assert.IsTrue(Pixels(shown).SequenceEqual(Pixels(expected)),
                          $"The Inspector header of \"{path}\" shows \"{shown.name}\", not AttributeRegistry.png.");
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
