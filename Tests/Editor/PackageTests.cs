using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// Shared setup for package and import tests: installed packages are faked, generated registries go to the test
    /// folder, and postponed imports and logs stay out of the project.
    /// </summary>
    public abstract class PackageTestBase : TagFieldTestBase
    {
        protected const string GENERATED = FOLDER + "/Packages";
        protected const string VENDOR = "com.vendor.combat";
        protected const string DEPENDENCY = "com.vendor.core";

        protected static readonly RegistryGuid ForeignRegistry = RegistryGuid.Parse("f0000000000000000000000000000001");
        protected static readonly RegistryGuid VendorRegistry = RegistryGuid.Parse("f0000000000000000000000000000002");
        protected static readonly RegistryGuid DependencyRegistry = RegistryGuid.Parse("f0000000000000000000000000000003");

        protected static RegistryGuid G(int _N) => new(0xF000_0000_0000_0000UL, (ulong)_N);

        protected readonly Dictionary<string, string> Installed = new();

        private string previousGeneratedFolder;
        private string previousPendingPath;
        private string previousLogFolder;

        [SetUp]
        public void FakePackages()
        {
            previousGeneratedFolder = PackageRegistryService.GeneratedFolder;
            PackageRegistryService.GeneratedFolder = GENERATED;
            PackageRegistryService.InstalledPackagesOverride = () => Installed;

            previousPendingPath = ProjectAssetImport.PendingPath;
            ProjectAssetImport.PendingPath = CODE_FOLDER + "/PendingImports";
            previousLogFolder = ProjectAssetImport.LogFolder;
            ProjectAssetImport.LogFolder = CODE_FOLDER + "/Logs";
            ProjectAssetImport.OnImportCancelled(null);

            Installed.Clear();
            TagRegistryService.Reload();
        }

        [TearDown]
        public void RestorePackages()
        {
            ProjectAssetImport.OnImportCancelled(null);
            ProjectAssetImport.ClearPending();
            ProjectAssetImport.PendingPath = previousPendingPath;
            ProjectAssetImport.LogFolder = previousLogFolder;

            PackageRegistryService.InstalledPackagesOverride = null;
            PackageRegistryService.GeneratedFolder = previousGeneratedFolder;
        }



        #region Helpers
        protected static string CreateFolder(string _Name)
        {
            string path = $"{FOLDER}/{_Name}";
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(FOLDER, _Name);

            return path;
        }

        protected static string CreateAsset(string _Folder, string _Name, params GameplayTag[] _Tags)
        {
            string path = $"{_Folder}/{_Name}.asset";
            AssetDatabase.CreateAsset(CreateScriptableObject(_Tags), path);
            AssetDatabase.SaveAssets();
            return path;
        }

        protected static TagSerializationFixture Load(string _Path) => AssetDatabase.LoadAssetAtPath<TagSerializationFixture>(_Path);

        protected static GameplayTag Foreign(int _N, string _Name) => GameplayTag.FromId(ForeignRegistry, G(_N), _Name);

        private protected static void WriteRegistry(string _Path, RegistryData<TagRegistryRow> _Data)
            => RegistryServiceTests.WriteAndImport(_Path, TagRegistryFile.Write(_Data));

        private protected static RegistryData<TagRegistryRow> Registry(RegistryGuid _Id, RegistryOrigin _Kind, string _Package, params (int Id, int Parent, string Segment)[] _Rows) => new()
        {
            registryId = _Id,
            name = _Package ?? "Registry",
            origin = _Kind,
            package = _Kind == RegistryOrigin.Generated ? _Package : string.Empty,
            version = _Kind == RegistryOrigin.Generated ? "1.0.0" : string.Empty,
            rows = _Rows.Select(e => TagRegistryRow.Create(G(e.Id), e.Parent == 0 ? RegistryGuid.Zero : G(e.Parent), e.Segment, false)).ToList(),
        };

        /// <summary>
        /// Every file under a folder, with its content, to check that nothing was written.
        /// </summary>
        protected static Dictionary<string, string> Files(string _Folder)
            => Directory.Exists(_Folder)
                ? Directory.GetFiles(_Folder, "*", SearchOption.AllDirectories).ToDictionary(e => e.Replace('\\', '/'), File.ReadAllText)
                : new Dictionary<string, string>();

        protected static void AssertUnchanged(Dictionary<string, string> _Before, string _Folder)
        {
            var after = Files(_Folder);
            CollectionAssert.AreEquivalent(_Before.Keys, after.Keys, "no file was added or removed");
            foreach (var (path, text) in _Before)
                Assert.AreEqual(text, after[path], $"\"{path}\" was written");
        }
        #endregion
    }



    public sealed class PackageRegistryTests : PackageTestBase
    {
        private RegistryPackage Vendor(string _Version = "1.0.0") => new()
        {
            Name = VENDOR,
            Version = _Version,
            DisplayName = "Vendor Combat",
            Root = CreateFolder("VendorCombat"),
        };

        private static TagRegistrySnapshot.Registry GeneratedFor(string _Package)
            => TagRegistryService.Snapshot.Registries.Single(e => e.Kind == TagRegistryKind.Generated && e.Data.package == _Package);



        #region Generated registries
        [Test]
        public void GeneratedRegistry_LoadsOnlyWhileItsPackageIsInstalled()
        {
            string path = GENERATED + "/" + VENDOR + ".tagregistry";
            CreateFolder("Packages");
            WriteRegistry(path, Registry(VendorRegistry, RegistryOrigin.Generated, VENDOR, (1, 0, "Stun")));
            string text = File.ReadAllText(path);
            var stun = GameplayTag.FromId(VendorRegistry, G(1), "Stun");

            Assert.AreEqual(TagStatus.MissingRegistry, stun.Resolve().Status, "the package isn't installed");
            Assert.IsTrue(TagRegistryService.InactiveRegistries.Any(e => e.Contains(VENDOR)));

            Installed[VENDOR] = "1.0.0";
            PackageRegistryService.OnPackagesChanged(new[] { VENDOR });
            Assert.AreEqual(TagStatus.Live, stun.Resolve().Status);

            Installed.Remove(VENDOR);
            PackageRegistryService.OnPackagesChanged(new[] { VENDOR });
            Assert.AreEqual(TagStatus.MissingRegistry, stun.Resolve().Status);

            Assert.AreEqual(text, File.ReadAllText(path), "nothing is written");
        }

        [Test]
        public void Generate_TakesTheReferencedRegistryAndIds_WithoutRewritingThePackage()
        {
            var package = Vendor();
            Installed[VENDOR] = "1.0.0";

            string first = CreateAsset(package.Root, "First", Foreign(2, "Vendor.Stun"), Foreign(3, "Vendor.Burn"), A);
            string second = CreateAsset(package.Root, "Second", Foreign(2, "Vendor.Stun"), Foreign(1, "Vendor"));
            File.WriteAllText(package.Root + "/Combat.tags", "Vendor.Freeze ; Frozen solid\nVendor.Stun\n");
            AssetDatabase.ImportAsset(package.Root + "/Combat.tags");
            var before = Files(package.Root);

            var result = PackageRegistryService.Generate(package, false);

            Assert.IsTrue(result.Success, result.Error);
            CollectionAssert.AreEqual(new[] { GENERATED + "/" + VENDOR + ".tagregistry" }, result.Written);
            AssertUnchanged(before, package.Root);

            var registry = GeneratedFor(VENDOR);
            Assert.AreEqual(ForeignRegistry, registry.Id, "the referenced registry ID");
            Assert.AreEqual("1.0.0", registry.Data.version);
            Assert.AreEqual(G(1), IdIn(registry, "Vendor"), "a referenced parent keeps its ID");
            Assert.AreEqual(G(2), IdIn(registry, "Vendor.Stun"));
            Assert.AreEqual(G(3), IdIn(registry, "Vendor.Burn"));
            Assert.IsTrue(registry.TryFind("Vendor.Freeze", out var freeze), "tag file entries are added");
            Assert.IsTrue(registry.TryGetRow(freeze, out var row) && row.description == "Frozen solid");
            Assert.IsFalse(registry.Contains(A.Id), "references to loaded registries need nothing");

            foreach (var tag in Load(first).container.Concat(Load(second).container))
                Assert.IsTrue(tag.IsLive, $"{tag.LastKnownName} resolves");
        }

        [Test]
        public void Generate_NothingToDo_WhenEveryReferenceResolves()
        {
            var package = Vendor();
            CreateAsset(package.Root, "Known", A, C);

            var result = PackageRegistryService.Generate(package, false);

            Assert.IsTrue(result.Success);
            CollectionAssert.IsEmpty(result.Written);
            Assert.IsFalse(AssetDatabase.IsValidFolder(GENERATED));
        }

        [Test]
        public void PackageUpdate_OnlyPrompts_AndRegenerateKeepsRows()
        {
            var package = Vendor();
            Installed[VENDOR] = "1.0.0";
            string asset = CreateAsset(package.Root, "Tags", Foreign(2, "Vendor.Stun"));
            Assert.IsTrue(PackageRegistryService.Generate(package, false).Success);
            string path = GeneratedFor(VENDOR).AssetPath;
            string text = File.ReadAllText(path);

            // The update references a new tag instead.
            SetList(Load(asset), Foreign(4, "Vendor.Slow"));
            AssetDatabase.SaveAssets();
            Installed[VENDOR] = "1.1.0";

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Regenerate"));
            var prompts = PackageRegistryService.OnPackagesChanged(new[] { VENDOR });

            Assert.AreEqual(1, prompts.Count);
            Assert.AreEqual(text, File.ReadAllText(path), "only a prompt");
            Assert.AreEqual(1, PackageRegistryService.FindOutdated(TagRegistryService.Snapshot).Count);

            Assert.IsTrue(PackageRegistryService.Generate(Vendor("1.1.0"), false).Success);
            var registry = GeneratedFor(VENDOR);
            Assert.AreEqual(path, registry.AssetPath, "regenerated in place");
            Assert.AreEqual(G(2), IdIn(registry, "Vendor.Stun"), "existing rows are kept");
            Assert.AreEqual(G(4), IdIn(registry, "Vendor.Slow"));
            Assert.AreEqual("1.1.0", registry.Data.version);
            CollectionAssert.IsEmpty(PackageRegistryService.FindOutdated(TagRegistryService.Snapshot));

            Assert.IsTrue(PackageRegistryService.Generate(Vendor("1.1.0"), true).Success);
            registry = GeneratedFor(VENDOR);
            Assert.IsFalse(registry.Contains(G(2)), "removed on request");
            Assert.IsTrue(registry.Contains(G(4)));
            Assert.IsTrue(registry.TryFind("Vendor", out _), "parents of kept rows stay");
        }

        [Test]
        public void PackageEvents_WriteNothing()
        {
            Installed[VENDOR] = "1.0.0";
            var package = Vendor();
            CreateAsset(package.Root, "Tags", Foreign(2, "Vendor.Stun"));
            Assert.IsTrue(PackageRegistryService.Generate(package, false).Success);
            TagCodeGen.CancelPending();

            var before = Files(FOLDER);

            // A teammate's pull updates one package and removes another.
            Installed[VENDOR] = "2.0.0";
            Installed.Remove("com.other");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Regenerate"));
            PackageRegistryService.OnPackagesChanged(new[] { VENDOR, "com.other" });

            AssertUnchanged(before, FOLDER);
            Assert.IsFalse(TagCodeGen.IsPending, "no code generation");
        }

        [Test]
        public void Generate_WithoutTheLock_IsRefused()
        {
            var package = Vendor();
            CreateAsset(package.Root, "Tags", Foreign(2, "Vendor.Stun"));
            RegistryLockProvider.Current = new RefusingLockProvider();

            var result = PackageRegistryService.Generate(package, false);

            Assert.IsFalse(result.Success);
            Assert.IsFalse(AssetDatabase.IsValidFolder(GENERATED));
        }
        #endregion



        #region Pack validation
        private RegistryPackage Authored(params string[] _Dependencies)
        {
            var package = new RegistryPackage
            {
                Name = "com.me.abilities",
                Version = "1.0.0",
                Root = CreateFolder("MyAbilities"),
                Dependencies = _Dependencies.ToList(),
            };

            WriteRegistry(package.Root + "/Abilities.tagregistry",
                Registry(VendorRegistry, RegistryOrigin.Package, null, (1, 0, "Abilities"), (2, 1, "Dash")));

            return package;
        }

        private static GameplayTag Own(int _N, string _Name) => GameplayTag.FromId(VendorRegistry, G(_N), _Name);

        [Test]
        public void PackValidation_PassesForOwnTags()
        {
            var package = Authored();
            CreateAsset(package.Root, "Ok", Own(2, "Abilities.Dash"), Own(1, "Abilities"));

            CollectionAssert.IsEmpty(TagPackValidation.Validate(package, RegistryGuid.Zero));
            CollectionAssert.IsEmpty(TagPackValidation.Validate(package, VendorRegistry), "same ID as the last pack");
        }

        [Test]
        public void PackValidation_FailsOnAReferenceToTheMainRegistry()
        {
            var package = Authored();
            CreateAsset(package.Root, "UsesMain", Own(2, "Abilities.Dash"), A);

            var errors = TagPackValidation.Validate(package, RegistryGuid.Zero);

            Assert.AreEqual(1, errors.Count, string.Join("\n", errors));
            StringAssert.Contains("main registry", errors[0]);
        }

        [Test]
        public void PackValidation_FailsOnUnknownIdUndeclaredDependencyAndChangedId()
        {
            var package = Authored();
            Installed[DEPENDENCY] = "1.0.0";
            CreateFolder("Packages");
            WriteRegistry(GENERATED + "/" + DEPENDENCY + ".tagregistry",
                Registry(DependencyRegistry, RegistryOrigin.Generated, DEPENDENCY, (7, 0, "Core")));
            var core = GameplayTag.FromId(DependencyRegistry, G(7), "Core");

            CreateAsset(package.Root, "Unknown", Own(99, "Abilities.Gone"));
            CreateAsset(package.Root, "Dependency", core);

            var errors = TagPackValidation.Validate(package, RegistryGuid.Parse("e0000000000000000000000000000009"));

            Assert.IsTrue(errors.Any(e => e.Contains("Abilities.Gone") && e.Contains("doesn't exist")), "unknown ID");
            Assert.IsTrue(errors.Any(e => e.Contains("\"Core\"") && e.Contains("dependencies")), "undeclared dependency");
            Assert.IsTrue(errors.Any(e => e.Contains("registry ID changed")), "changed registry ID");

            // Declaring the dependency makes its tags valid.
            package.Dependencies.Add(DEPENDENCY);
            Assert.IsFalse(TagPackValidation.Validate(package, RegistryGuid.Zero).Any(e => e.Contains("\"Core\"")));
        }

        [Test]
        public void PackValidation_NeedsExactlyOnePackageRegistryWithAValidChecksum()
        {
            var package = Authored();

            File.WriteAllText(package.Root + "/Abilities.tagregistry", File.ReadAllText(package.Root + "/Abilities.tagregistry").Replace("Dash", "Sprint"));
            AssetDatabase.ImportAsset(package.Root + "/Abilities.tagregistry");
            Assert.IsTrue(TagPackValidation.Validate(package, RegistryGuid.Zero).Any(e => e.Contains("checksum")));

            WriteRegistry(package.Root + "/Second.tagregistry",
                Registry(RegistryGuid.Parse("e0000000000000000000000000000001"), RegistryOrigin.Package, null, (5, 0, "Other")));
            Assert.IsTrue(TagPackValidation.Validate(package, RegistryGuid.Zero).Any(e => e.Contains("2 registries")));
        }
        #endregion

        private static RegistryGuid IdIn(TagRegistrySnapshot.Registry _Registry, string _Path)
        {
            Assert.IsTrue(_Registry.TryFind(_Path, out var id), $"\"{_Path}\" not found");
            return id;
        }
    }



    public sealed class ProjectAssetImportTests : PackageTestBase
    {
        private string incoming;

        [SetUp]
        public void CreateIncomingFolder() => incoming = CreateFolder("Incoming");

        private static TagImportResult Map(params string[] _Paths)
        {
            var result = ProjectAssetImport.Map(_Paths, "test import", false);
            Assert.IsTrue(result.Success, result.Error);
            return result;
        }

        [Test]
        public void ExistingNames_AreRewrittenToMainTags_ProjectAssetsUntouched()
        {
            string asset = CreateAsset(incoming, "Incoming", Foreign(1, "A"), Foreign(2, "C"), D);
            string project = CreateAsset(FOLDER, "Project", Foreign(1, "A"));
            string projectText = File.ReadAllText(project);

            var result = Map(asset);

            AssertList(Load(asset), A, C, D);
            Assert.AreEqual("A", GetList(Load(asset))[0].LastKnownName);
            Assert.AreEqual(2, result.Rewritten.Count);
            CollectionAssert.IsEmpty(result.AddedTags);
            Assert.AreEqual(projectText, File.ReadAllText(project), "only the incoming assets are rewritten");
            Assert.IsTrue(File.Exists(result.LogPath), "an import log is written");
        }

        [Test]
        public void NewNames_AreAddedWithTheIncomingIdIfUnused()
        {
            string asset = CreateAsset(incoming, "Incoming", Foreign(1, "New.Tag"), GameplayTag.FromId(ForeignRegistry, A.Id, "Other"));

            var result = Map(asset);

            Assert.IsTrue(Main.TryFind("New.Tag", out var newTag));
            Assert.AreEqual(G(1), newTag, "the incoming ID is free");
            Assert.IsTrue(Main.TryFind("Other", out var other));
            Assert.AreNotEqual(A.Id, other, "the incoming ID is A's, so a new one is used");
            Assert.IsTrue(Main.TryFind("New", out _), "implicit parents are created");
            Assert.IsFalse(TagOperations.HasPendingChanges, "the added tags are saved");
            Assert.AreEqual(2, result.AddedTags.Count);

            AssertList(Load(asset), GameplayTag.FromId(Main.Id, G(1)), GameplayTag.FromId(Main.Id, other));
            Assert.IsTrue(GetList(Load(asset)).All(e => e.IsLive));
        }

        [Test]
        public void PackageRegistryReferences_AreUnchanged()
        {
            WriteRegistry(FOLDER + "/Vendor.tagregistry", Registry(VendorRegistry, RegistryOrigin.Package, null, (1, 0, "Vendor")));
            var vendor = GameplayTag.FromId(VendorRegistry, G(1), "Vendor");
            string asset = CreateAsset(incoming, "Incoming", vendor, A);
            string text = File.ReadAllText(asset);

            var result = Map(asset);

            CollectionAssert.IsEmpty(result.Rewritten);
            Assert.AreEqual(text, File.ReadAllText(asset));
        }

        [Test]
        public void ReferencesWithoutAName_AreReported()
        {
            string asset = CreateAsset(incoming, "Incoming", Foreign(1, string.Empty));
            string text = File.ReadAllText(asset);

            var result = Map(asset);

            Assert.AreEqual(1, result.Unmapped.Count);
            Assert.AreEqual(text, File.ReadAllText(asset));
        }

        [Test]
        public void PackageImportEvents_MapOnlyWhenTheImportCompletes()
        {
            string asset = CreateAsset(incoming, "Incoming", Foreign(1, "A"));
            string text = File.ReadAllText(asset);
            string registry = File.ReadAllText(MAIN);

            ProjectAssetImport.OnImportStarted("Cancelled.unitypackage");
            ProjectAssetImport.OnAssetsImported(new[] { asset });
            ProjectAssetImport.OnImportCancelled("Cancelled.unitypackage");
            Assert.IsNull(ProjectAssetImport.OnImportCompleted("Cancelled.unitypackage"));

            Assert.AreEqual(text, File.ReadAllText(asset), "no rewrite");
            Assert.AreEqual(registry, File.ReadAllText(MAIN), "no registry change");

            ProjectAssetImport.OnImportStarted("Done.unitypackage");
            ProjectAssetImport.OnAssetsImported(new[] { asset });
            var result = ProjectAssetImport.OnImportCompleted("Done.unitypackage");

            Assert.IsNotNull(result);
            AssertList(Load(asset), A);
        }

        [Test]
        public void ChangesOutsideAnImport_AreNotMapped()
        {
            string asset = CreateAsset(incoming, "Pulled", Foreign(1, "A"));
            string text = File.ReadAllText(asset);

            // A pull: assets are imported, but no package import is under way.
            ProjectAssetImport.OnAssetsImported(new[] { asset });
            Assert.IsFalse(ProjectAssetImport.IsCollecting);

            Assert.AreEqual(text, File.ReadAllText(asset));
            Assert.AreEqual(TagStatus.MissingRegistry, GetList(Load(asset))[0].Resolve().Status);
        }

        [Test]
        public void WithoutTheLock_MappingIsPostponed_ThenOffered()
        {
            string asset = CreateAsset(incoming, "Incoming", Foreign(1, "A"), Foreign(2, "Later"));
            string text = File.ReadAllText(asset);
            RegistryLockProvider.Current = new RefusingLockProvider();

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not mapped"));
            var postponed = ProjectAssetImport.Map(new[] { asset }, "test import", false);

            Assert.IsTrue(postponed.Postponed);
            Assert.AreEqual(text, File.ReadAllText(asset));
            CollectionAssert.AreEqual(new[] { asset }, ProjectAssetImport.PendingAssets);

            RegistryLockProvider.Current = null;
            var result = ProjectAssetImport.MapPending(false);

            Assert.IsTrue(result.Success && !result.Postponed, result.Message);
            Assert.IsFalse(ProjectAssetImport.HasPending);
            Assert.IsTrue(GetList(Load(asset)).All(e => e.IsLive));
        }

        [Test, Explicit("Creates 5,000 assets: run on demand to record the time.")]
        public void LargeImport_FinishesWithACompleteLog()
        {
            const int COUNT = 5000;
            var paths = new List<string>();

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < COUNT; i++)
                {
                    string path = $"{incoming}/Incoming{i:0000}.asset";
                    AssetDatabase.CreateAsset(CreateScriptableObject(Foreign(1, "A"), Foreign(2 + i % 50, $"Bulk.Tag{i % 50}")), path);
                    paths.Add(path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            var result = Map(paths.ToArray());

            Debug.Log($"Large import: mapped {COUNT} assets in {result.Seconds:0.0} s.");
            Assert.AreEqual(COUNT * 2, result.Rewritten.Count);
            Assert.AreEqual(50, result.AddedTags.Count);
            Assert.AreEqual(COUNT * 2, File.ReadAllLines(result.LogPath).Count(e => e.Contains(" -> main ")), "the log lists every rewrite");
        }
    }
}
