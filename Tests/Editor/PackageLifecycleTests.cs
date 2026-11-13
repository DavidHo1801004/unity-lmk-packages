using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine.TestTools;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

using static LMK.Editor.GameplayTags.Tests.TestIds;

using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// A real package with its own registry, installed through the Package Manager from a local folder: it
    /// installs, updates, uninstalls and reinstalls without writing to the project. The project's manifest is restored afterwards.
    /// </summary>
    public sealed class PackageLifecycleTests
    {
        private const string PACKAGE = "com.lmk.gameplaytags.testpackage";
        private const string MANIFEST = "Packages/manifest.json";
        private const string LOCK = "Packages/packages-lock.json";

        private static readonly RegistryGuid PackageRegistry = RegistryGuid.Parse("d0000000000000000000000000000001");

        private string folder;
        private string registryPath;
        private string manifest;
        private string packagesLock;

        private static GameplayTag Stun => GameplayTag.FromId(PackageRegistry, Id(2), "TestPackage.Stun");
        private static GameplayTag Burn => GameplayTag.FromId(PackageRegistry, Id(3), "TestPackage.Burn");

        [SetUp]
        public void CreatePackage()
        {
            manifest = File.ReadAllText(MANIFEST);
            packagesLock = File.Exists(LOCK) ? File.ReadAllText(LOCK) : null;

            folder = Path.GetFullPath("Temp/__GameplayTagTestPackage").Replace('\\', '/');
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);

            Directory.CreateDirectory(folder + "/GameplayTags");
            File.WriteAllText(folder + "/package.json",
                "{\n  \"name\": \"" + PACKAGE + "\",\n  \"version\": \"1.0.0\",\n  \"displayName\": \"Gameplay Tags Test Package\"\n}\n");

            registryPath = folder + "/GameplayTags/TestPackage.tagregistry";
            WriteRegistry(true);
        }

        [UnityTearDown]
        public IEnumerator RemovePackage()
        {
            if (PackageInfo.GetAllRegisteredPackages().Any(e => e.name == PACKAGE))
                yield return Run(Client.Remove(PACKAGE), false);

            // Leave the manifest exactly as it was, whatever happened.
            if (File.ReadAllText(MANIFEST) != manifest || (packagesLock != null && File.ReadAllText(LOCK) != packagesLock))
            {
                File.WriteAllText(MANIFEST, manifest);
                if (packagesLock != null)
                    File.WriteAllText(LOCK, packagesLock);

                Client.Resolve();
            }

            if (Directory.Exists(folder))
                Directory.Delete(folder, true);

            TagRegistryService.Reload();
        }

        private void WriteRegistry(bool _WithBurn)
        {
            var data = new RegistryData<TagRegistryRow>
            {
                registryId = PackageRegistry,
                name = "Test Package",
                origin = RegistryOrigin.Package,
                rows = new List<RegistryRow<TagRegistryRow>>
                {
                    TagRegistryRow.Create(Id(1), RegistryGuid.Zero, "TestPackage", true),
                    TagRegistryRow.Create(Id(2), Id(1), "Stun", false),
                },
            };

            if (_WithBurn)
                data.rows.Add(TagRegistryRow.Create(Id(3), Id(1), "Burn", false));

            File.WriteAllText(registryPath, TagRegistryFile.Write(data));
        }

        private static IEnumerator Run(Request _Request, bool _MustSucceed = true)
        {
            while (!_Request.IsCompleted)
                yield return null;

            if (_MustSucceed)
                Assert.AreEqual(StatusCode.Success, _Request.Status, _Request.Error?.message);
        }

        /// <summary>
        /// Wait until the tag has the status, refreshing the asset database while waiting.
        /// </summary>
        private static IEnumerator WaitFor(GameplayTag _Tag, TagStatus _Status)
        {
            for (int frame = 0; frame < 600 && _Tag.Resolve().Status != _Status; frame++)
            {
                if (frame % 30 == 0)
                    AssetDatabase.Refresh();

                yield return null;
            }

            Assert.AreEqual(_Status, _Tag.Resolve().Status, $"\"{_Tag.LastKnownName}\"");
        }

        /// <summary>
        /// The project files the tag system could write: tag folders, settings and generated code.
        /// </summary>
        private static Dictionary<string, string> ProjectFiles()
        {
            var files = new Dictionary<string, string>();
            if (Directory.Exists(TagRegistryService.ROOT_FOLDER))
                foreach (string path in Directory.GetFiles(TagRegistryService.ROOT_FOLDER, "*", SearchOption.AllDirectories))
                    files[path.Replace('\\', '/')] = File.ReadAllText(path);

            const string SETTINGS = "ProjectSettings/LMKGameplayTagSettings.asset";
            if (File.Exists(SETTINGS))
                files[SETTINGS] = File.ReadAllText(SETTINGS);

            return files;
        }

        [UnityTest]
        public IEnumerator PackageWithARegistry_InstallsUpdatesUninstallsAndReinstalls_WritingNothing()
        {
            var before = ProjectFiles();
            bool wasPending = TagCodeGen.IsPending;

            // Install. Its references resolve.
            yield return Run(Client.Add("file:" + folder));
            yield return WaitFor(Stun, TagStatus.Live);
            Assert.AreEqual(TagStatus.Live, Burn.Resolve().Status);
            Assert.AreEqual("TestPackage.Stun", Stun.Path);

            var registry = TagRegistryService.Snapshot.Find(PackageRegistry);
            Assert.IsTrue(registry.IsReadOnly);
            StringAssert.StartsWith("Packages/" + PACKAGE + "/", registry.AssetPath);

            // An update removes a tag. Its references show as deleted.
            WriteRegistry(false);
            yield return WaitFor(Burn, TagStatus.Deleted);
            Assert.AreEqual(TagStatus.Live, Stun.Resolve().Status);

            // Uninstall, then reinstall.
            yield return Run(Client.Remove(PACKAGE));
            yield return WaitFor(Stun, TagStatus.MissingRegistry);

            yield return Run(Client.Add("file:" + folder));
            yield return WaitFor(Stun, TagStatus.Live);

            // Nothing was written to the project.
            var after = ProjectFiles();
            CollectionAssert.AreEquivalent(before.Keys, after.Keys);
            foreach (var (path, text) in before)
                Assert.AreEqual(text, after[path], $"\"{path}\" was written");
            Assert.AreEqual(wasPending, TagCodeGen.IsPending, "no code generation");
        }
    }



    /// <summary>
    /// A real <c>.unitypackage</c> round trip: exported from this project with references to another project's
    /// registry, then imported, through the import events.
    /// </summary>
    public sealed class UnityPackageImportTests : TagFieldTestBase
    {
        private static readonly RegistryGuid OtherProject = RegistryGuid.Parse("c0000000000000000000000000000077");

        private string packagePath;
        private string previousLogFolder;

        [SetUp]
        public void UseTestLogs()
        {
            previousLogFolder = ProjectAssetImport.LogFolder;
            ProjectAssetImport.LogFolder = CODE_FOLDER + "/Logs";
            packagePath = Path.GetFullPath(CODE_FOLDER + "/Incoming.unitypackage");
        }

        [TearDown]
        public void RestoreLogs()
        {
            ProjectAssetImport.LogFolder = previousLogFolder;
            ProjectAssetImport.OnImportCancelled(null);
        }

        [UnityTest]
        public IEnumerator ImportedPackage_IsMapped_WhenTheImportCompletes()
        {
            const string ASSET = FOLDER + "/Incoming.asset";
            AssetDatabase.CreateAsset(CreateScriptableObject(
                GameplayTag.FromId(OtherProject, new RegistryGuid(7, 1), "A"),
                GameplayTag.FromId(OtherProject, new RegistryGuid(7, 2), "Imported.Tag")), ASSET);
            AssetDatabase.SaveAssets();

            Directory.CreateDirectory(CODE_FOLDER);
            AssetDatabase.ExportPackage(ASSET, packagePath);
            AssetDatabase.DeleteAsset(ASSET);

            bool completed = false;
            void OnCompleted(string _) => completed = true;
            AssetDatabase.importPackageCompleted += OnCompleted;
            try
            {
                AssetDatabase.ImportPackage(packagePath, false);
                for (int frame = 0; frame < 600 && !completed; frame++)
                    yield return null;
            }
            finally
            {
                AssetDatabase.importPackageCompleted -= OnCompleted;
            }

            Assert.IsTrue(completed, "the import finished");
            Assert.IsFalse(ProjectAssetImport.IsCollecting);

            var tags = GetList(AssetDatabase.LoadAssetAtPath<TagSerializationFixture>(ASSET));
            Assert.AreEqual(A, tags[0], "an existing name maps to the main tag");
            Assert.IsTrue(Main.TryFind("Imported.Tag", out var added));
            Assert.AreEqual(new RegistryGuid(7, 2), added, "a new name keeps its incoming ID");
            Assert.IsTrue(tags.All(e => e.IsLive));
        }
    }
}
