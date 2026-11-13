using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry.Tests
{
    public sealed class BuildTests : KindTestBase
    {
        [SetUp]
        public void UseTestDatabaseFolder()
        {
            a.DatabaseFolder = $"{FOLDER}/Generated/Resources";
        }

        private static RegistryGuid Add<TAsset>(TreeKindBase<TAsset> _Kind, string _Segment, bool _Save) where TAsset : RegistryAsset<TreeRow>
        {
            var result = RegistryEdit.Apply(_Kind, "Add", data =>
            {
                var id = RegistryGuid.NewGuid();
                data.rows.Add(new RegistryRow<TreeRow>(id, new TreeRow(RegistryGuid.Zero, _Segment)));
                return RegistryResult.Ok(id);
            });

            if (_Save) Assert.IsTrue(RegistrySession.Save().Success);
            return result.Id;
        }



        #region Bake
        [Test]
        public void Bake_HoldsSavedRegistriesOnly_AndKeepsItsGuid()
        {
            var fire = Add(a, "Fire", true);
            var database = RegistryBuildProcessor.Bake(a);
            string path = RegistryBuildProcessor.DatabasePath(a, typeof(TestDatabase));
            string guid = AssetDatabase.AssetPathToGUID(path);

            Assert.AreEqual(path, AssetDatabase.GetAssetPath(database));
            Assert.AreEqual(nameof(TestDatabase), database.name);
            Assert.IsTrue(database.Resolve(a.MainRegistryId, fire).IsLive);

            var ice = Add(a, "Ice", false);
            database = RegistryBuildProcessor.Bake(a);
            Assert.IsFalse(database.Resolve(a.MainRegistryId, ice).IsLive, "unsaved edits are not baked");

            RegistrySession.Save();
            database = RegistryBuildProcessor.Bake(a);
            Assert.IsTrue(database.Resolve(a.MainRegistryId, ice).IsLive);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(database)), "the asset keeps its GUID");
        }

        [Test]
        public void Bake_WithoutChanges_DoesNotRewriteTheAsset()
        {
            Add(a, "Fire", true);
            RegistryBuildProcessor.Bake(a);
            string path = RegistryBuildProcessor.DatabasePath(a, typeof(TestDatabase));
            var written = File.GetLastWriteTimeUtc(path);

            RegistryBuildProcessor.Bake(a);

            Assert.AreEqual(written, File.GetLastWriteTimeUtc(path));
        }

        [Test]
        public void EnsurePreloaded_KeepsOneDatabasePerType()
        {
            var original = PlayerSettings.GetPreloadedAssets();
            try
            {
                var first = RegistryBuildProcessor.Bake(a);
                RegistryBuildProcessor.EnsurePreloaded(first);
                RegistryBuildProcessor.EnsurePreloaded(first);

                Assert.AreEqual(1, PlayerSettings.GetPreloadedAssets().Count(e => e is TestDatabase));
                CollectionAssert.Contains(PlayerSettings.GetPreloadedAssets(), first);
            }
            finally
            {
                PlayerSettings.SetPreloadedAssets(original);
            }
        }
        #endregion



        #region Validation
        [Test]
        public void SceneProcessing_WritesNoneInTheBuildCopy_ForEveryKind()
        {
            var fire = Add(a, "Fire", true);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var holder = new GameObject("Holder").AddComponent<ReferenceHolderBehaviour>();
                SceneManager.MoveGameObjectToScene(holder.gameObject, scene);
                holder.single = new TreeRef(a.MainRegistryId, RegistryGuid.NewGuid(), "Gone");
                holder.list.Add(new TreeRef(a.MainRegistryId, fire, "Fire"));
                holder.other = new TreeRefB(RegistryGuid.NewGuid(), RegistryGuid.NewGuid(), "Elsewhere");

                var reported = RegistryBuildProcessor.ProcessScene(scene, false);
                Assert.AreEqual(2, reported.Count);
                Assert.AreEqual("Gone", holder.single.LastKnownName, "validation alone changes nothing");

                var issues = RegistryBuildProcessor.ProcessScene(scene, true);

                Assert.AreEqual(2, issues.Count);
                Assert.AreEqual(RegistryStatus.Deleted, issues.Single(e => e.Reference.Kind == a).Status);
                Assert.AreEqual(RegistryStatus.MissingRegistry, issues.Single(e => e.Reference.Kind == b).Status);
                Assert.IsTrue(holder.single.Id.IsZero && holder.other.Id.IsZero, "missing references become None");
                Assert.AreEqual(fire, holder.list[0].Id, "live references stay");
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void AssetValidation_ReportsWithoutChangingTheAsset()
        {
            string path = CreateHolder(CreateFolder("Build"), "Holder", h => h.single = new TreeRef(a.MainRegistryId, RegistryGuid.NewGuid(), "Gone"));
            string before = File.ReadAllText(path);

            var issues = RegistryBuildProcessor.ValidateAssets(new[] { path });

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains(path, issues[0].ToString());
            Assert.AreEqual(before, File.ReadAllText(path));
        }

        [Test]
        public void Report_FailsOnlyForKindsWhosePolicyFails()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var holder = new GameObject("Holder").AddComponent<ReferenceHolderBehaviour>();
                SceneManager.MoveGameObjectToScene(holder.gameObject, scene);
                holder.single = new TreeRef(a.MainRegistryId, RegistryGuid.NewGuid(), "GoneA");
                holder.other = new TreeRefB(b.MainRegistryId, RegistryGuid.NewGuid(), "GoneB");
                var issues = RegistryBuildProcessor.ProcessScene(scene, false);

                MissingReferencePolicy Policy(RegistryKind _Kind) => _Kind == a ? MissingReferencePolicy.Fail : MissingReferencePolicy.Warn;

                LogAssert.Expect(LogType.Warning, new Regex("GoneB"));
                var exception = Assert.Throws<BuildFailedException>(() => RegistryBuildProcessor.Report(issues, "test", Policy));
                StringAssert.Contains("GoneA", exception.Message);
                StringAssert.DoesNotContain("GoneB", exception.Message);

                LogAssert.Expect(LogType.Warning, new Regex("GoneA"));
                Assert.DoesNotThrow(() => RegistryBuildProcessor.Report(issues, "test", _ => MissingReferencePolicy.Warn));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void Settings_KindPolicyOverridesTheProjectPolicy()
        {
            var settings = RegistrySettings.instance;
            var previous = settings.MissingReferencesInBuilds;
            try
            {
                settings.SetPolicy(a.Id, MissingReferencePolicy.Fail, false);

                Assert.AreEqual(MissingReferencePolicy.Fail, settings.GetPolicy(a.Id));
                Assert.AreEqual(previous, settings.GetPolicy(b.Id));
                Assert.IsTrue(settings.HasOwnPolicy(a.Id));
            }
            finally
            {
                settings.SetPolicy(a.Id, null, false);
            }
        }
        #endregion



        #region Project Checks
        [Test]
        public void IntegrityCheck_ReportsAHandEditedRegistry()
        {
            Add(a, "Fire", true);
            CollectionAssert.IsEmpty(RegistryProjectChecks.CheckRegistryIntegrity());

            File.WriteAllText(a.MainRegistryPath, FileText(a).Replace("| Fire", "| Fyre"));
            AssetDatabase.ImportAsset(a.MainRegistryPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var failures = RegistryProjectChecks.CheckRegistryIntegrity();

            Assert.AreEqual(1, failures.Count, string.Join("\n", failures));
            StringAssert.Contains(a.MainRegistryPath, failures[0]);
            StringAssert.Contains("checksum", failures[0]);
        }
        #endregion
    }
}
