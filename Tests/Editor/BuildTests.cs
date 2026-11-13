using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.TestTools;

using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;
using LMK.GameplayTags.Tests;

namespace LMK.Editor.GameplayTags.Tests
{
    public sealed class BuildTests : TagFieldTestBase
    {
        private const string DATABASE_PATH = FOLDER + "/Resources/GameplayTagDatabase.asset";
        private const string SCENE_PATH = FOLDER + "/BuildScene.unity";

        [Test]
        public void Bake_ContainsEveryLoadedRegistry_AndKeepsItsGuid()
        {
            var database = TagBuildProcessor.Bake(DATABASE_PATH);
            string assetGuid = AssetDatabase.AssetPathToGUID(DATABASE_PATH);

            Assert.AreEqual(TagRegistryService.Snapshot.Registries.Count, database.RegistryCount);
            Assert.AreEqual(TagStatus.Live, database.Resolve(A.RegistryId, A.Id).Status);
            Assert.AreEqual(TagStatus.MissingRegistry, database.Resolve(RegistryGuid.Parse("d0000000000000000000000000000004"), A.Id).Status);
            Assert.AreEqual("A", database.GetPath(database.Resolve(A.RegistryId, A.Id).Handle));

            string hash = database.ContentHash;
            Assert.IsTrue(TagOperations.Rename(A.Id, "Renamed").Success);
            Assert.AreEqual(hash, TagBuildProcessor.Bake(DATABASE_PATH).ContentHash, "unsaved edits are not baked");
            Save();
            database = TagBuildProcessor.Bake(DATABASE_PATH);

            Assert.AreNotEqual(hash, database.ContentHash);
            Assert.AreEqual(assetGuid, AssetDatabase.AssetPathToGUID(DATABASE_PATH));
            Assert.AreEqual("Renamed", database.GetPath(database.Resolve(A.RegistryId, A.Id).Handle));
        }

        [Test]
        public void Bake_WithoutChanges_DoesNotRewriteTheAsset()
        {
            TagBuildProcessor.Bake(DATABASE_PATH);
            string before = File.ReadAllText(DATABASE_PATH);
            var written = File.GetLastWriteTimeUtc(DATABASE_PATH);

            TagBuildProcessor.Bake(DATABASE_PATH);

            Assert.AreEqual(before, File.ReadAllText(DATABASE_PATH));
            Assert.AreEqual(written, File.GetLastWriteTimeUtc(DATABASE_PATH));
        }

        [Test]
        public void SceneProcessing_WritesNoneInTheBuildCopy_SourceSceneUnchanged()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("Root");
            var holder = root.AddComponent<TagHolderBehaviour>();
            holder.single = B;
            holder.container = new GameplayTagContainer(A, B, C);
            holder.entries.Add(new TagEntry { tags = new GameplayTagContainer(B) });
            holder.managed = new TagManagedEntry { tag = B };

            var inactive = new GameObject("Inactive");
            inactive.transform.SetParent(root.transform);
            inactive.SetActive(false);
            inactive.AddComponent<TagHolderBehaviour>().single = B;

            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            string source = File.ReadAllText(SCENE_PATH);
            DeleteB();

            var issues = TagBuildProcessor.ProcessScene(scene, true);

            Assert.AreEqual(5, issues.Count, string.Join("\n", issues));
            Assert.IsTrue(issues.All(e => e.Status == TagStatus.Deleted && e.Tag.LastKnownName == "B"));
            Assert.IsTrue(issues.Any(e => e.PropertyPath.StartsWith("managed")), "managed references are walked");
            Assert.IsTrue(issues.Any(e => e.PropertyPath.StartsWith("entries")), "lists of structs are walked");
            Assert.IsTrue(issues.Any(e => e.Location.Contains("Root/Inactive")), "inactive objects are walked");

            Assert.IsTrue(holder.single.IsNone);
            Assert.IsTrue(holder.managed.tag.IsNone);
            Assert.AreEqual(2, holder.container.Count);
            Assert.IsTrue(holder.container.HasTagExact(A) && holder.container.HasTagExact(C));
            Assert.AreEqual(source, File.ReadAllText(SCENE_PATH), "the source scene is never written");
        }

        [Test]
        public void SceneValidation_WithoutWrite_OnlyReports()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var holder = new GameObject("Root").AddComponent<TagHolderBehaviour>();
            holder.single = B;
            DeleteB();

            var issues = TagBuildProcessor.ProcessScene(scene, false);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(B, holder.single);
        }

        [Test]
        public void FailPolicy_FailsTheBuild_WarnPolicyLogs()
        {
            var holder = new GameObject("Root").AddComponent<TagHolderBehaviour>();
            holder.single = B;
            DeleteB();
            var issues = TagBuildProcessor.ProcessScene(holder.gameObject.scene, false);

            var exception = Assert.Throws<BuildFailedException>(() => TagBuildProcessor.Report(issues, "test", MissingTagBuildPolicy.Fail));
            StringAssert.Contains("\"B\"", exception.Message);

            LogAssert.Expect(LogType.Warning, new Regex("don't resolve"));
            TagBuildProcessor.Report(issues, "test", MissingTagBuildPolicy.Warn);

            Assert.DoesNotThrow(() => TagBuildProcessor.Report(new List<MissingTagIssue>(), "test", MissingTagBuildPolicy.Fail));
        }

        [Test]
        public void AssetValidation_FindsPrefabReferences_WithoutChangingThem()
        {
            var gameObject = new GameObject("Holder");
            gameObject.AddComponent<TagHolderBehaviour>().container = new GameplayTagContainer(A, B);
            PrefabUtility.SaveAsPrefabAsset(gameObject, FOLDER + "/Holder.prefab");
            Object.DestroyImmediate(gameObject);
            DeleteB();
            string before = File.ReadAllText(FOLDER + "/Holder.prefab");

            var issues = TagBuildProcessor.ValidateAssets(new[] { FOLDER + "/Holder.prefab" });

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(FOLDER + "/Holder.prefab", issues[0].Location);
            Assert.AreEqual(before, File.ReadAllText(FOLDER + "/Holder.prefab"));
        }

        [Test]
        public void BinarySerialization_RoundTripsTheFrozenLayout()
        {
            string path = Path.GetFullPath("Temp/GameplayTagBinaryFixture.asset");
            var fixture = ScriptableObject.CreateInstance<TagSerializationFixture>();
            fixture.single = A;
            fixture.container = new GameplayTagContainer(C, D);

            InternalEditorUtility.SaveToSerializedFileAndForget(new Object[] { fixture }, path, false);
            Object.DestroyImmediate(fixture);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.AreNotEqual("%YAML", System.Text.Encoding.ASCII.GetString(bytes, 0, 5), "written in binary");

            var loaded = InternalEditorUtility.LoadSerializedFileAndForget(path).OfType<TagSerializationFixture>().Single();

            Assert.AreEqual(A, loaded.single);
            Assert.AreEqual("A", loaded.single.LastKnownName);
            Assert.IsTrue(loaded.container.HasTagExact(C) && loaded.container.HasTagExact(D));

            // Renames still reach references read from a binary file.
            Assert.IsTrue(TagOperations.Rename(A.Id, "Renamed").Success);
            Assert.AreEqual("Renamed", loaded.single.Path);

            Object.DestroyImmediate(loaded);
            File.Delete(path);
        }
    }
}
