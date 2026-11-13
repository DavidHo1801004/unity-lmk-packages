using System.IO;

using NUnit.Framework;

using LMK.Editor.Registry;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// The 1.0.0 settings file is read once into the registry settings (MG-10).
    /// </summary>
    public sealed class SettingsCarryOverTests
    {
        private const string PATH = "Temp/LMKGameplayTagSettings_1_0.asset";

        private static readonly RegistryGuid NO_CODE = RegistryGuid.Parse("c0000000000000000000000000000001");
        private static readonly RegistryGuid PUBLISHED = RegistryGuid.Parse("c0000000000000000000000000000002");

        // A settings file as gameplay tags 1.0.0 saved it.
        private const string SETTINGS_1_0 =
            "%YAML 1.1\n" +
            "%TAG !u! tag:unity3d.com,2011:\n" +
            "--- !u!114 &1\n" +
            "MonoBehaviour:\n" +
            "  m_ObjectHideFlags: 53\n" +
            "  m_CorrespondingSourceObject: {fileID: 0}\n" +
            "  m_PrefabInstance: {fileID: 0}\n" +
            "  m_PrefabAsset: {fileID: 0}\n" +
            "  m_GameObject: {fileID: 0}\n" +
            "  m_Enabled: 1\n" +
            "  m_EditorHideFlags: 0\n" +
            "  m_Script: {fileID: 11500000, guid: 357424f491e83724282fb65d5c0b42ff, type: 3}\n" +
            "  m_Name: \n" +
            "  m_EditorClassIdentifier: \n" +
            "  buildPolicyOnMissingTags: 1\n" +
            "  lockProvider: 0\n" +
            "  codeNamespace: My.Game\n" +
            "  codeGenDisabledRegistries:\n" +
            "  - c0000000000000000000000000000001\n" +
            "  publishedPackageRegistries:\n" +
            "  - com.vendor.combat=c0000000000000000000000000000002\n";

        [TearDown]
        public void Restore()
        {
            var settings = RegistrySettings.instance;
            settings.SetPolicy(TagKind.ID, null, false);
            settings.SetCodeNamespace(TagKind.Instance, string.Empty, false);
            settings.SetCodeGenerated(TagKind.Instance, NO_CODE, true, false);
            settings.SetPublishedRegistry("com.vendor.combat", TagKind.ID, RegistryGuid.Zero, false);

            if (File.Exists(PATH))
                File.Delete(PATH);
        }

        [Test]
        public void Settings10_AreCarriedOverToTheRegistrySettings()
        {
            File.WriteAllText(PATH, SETTINGS_1_0);

            Assert.IsTrue(GameplayTagSettings.CarryOver(PATH, false));

            var settings = RegistrySettings.instance;
            Assert.AreEqual(MissingReferencePolicy.Fail, settings.GetPolicy(TagKind.ID));
            Assert.AreEqual("My.Game", settings.GetCodeNamespace(TagKind.Instance));
            Assert.IsFalse(settings.IsCodeGenerated(TagKind.ID, NO_CODE));
            Assert.IsTrue(settings.TryGetPublishedRegistry("com.vendor.combat", TagKind.ID, out var published));
            Assert.AreEqual(PUBLISHED, published);

            Assert.AreEqual(MissingTagBuildPolicy.Fail, GameplayTagSettings.instance.BuildPolicyOnMissingTags, "the tag settings read them");
            Assert.AreEqual("My.Game", GameplayTagSettings.instance.CodeNamespace);
            Assert.AreEqual(SETTINGS_1_0, File.ReadAllText(PATH), "the 1.0.0 file is only read");
        }

        [Test]
        public void MissingFile_CarriesNothingOver()
        {
            Assert.IsFalse(GameplayTagSettings.CarryOver(PATH, false));
        }
    }
}
