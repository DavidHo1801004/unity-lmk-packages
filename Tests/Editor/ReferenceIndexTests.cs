using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;
using LMK.GameplayTags.Tests;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// The reference index and the tools built on it (RI). Uses its own index file under <c>Library/</c>, so the
    /// project's index is left alone.
    /// </summary>
    public sealed class ReferenceIndexTests : TagFieldTestBase
    {
        private const string INDEX_FOLDER = "Library/GameplayTags/__Tests";
        private const string INDEX = INDEX_FOLDER + "/RefIndex";

        private string previousIndexPath;

        [SetUp]
        public void UseTestIndex()
        {
            previousIndexPath = TagReferenceIndex.IndexPath;
            TagReferenceIndex.IndexPath = INDEX;
            TagReferenceIndex.ProgressOverride = null;
            TagReferenceIndex.Clear();
        }

        [TearDown]
        public void RestoreIndex()
        {
            TagReferenceIndex.ProgressOverride = null;
            TagReferenceIndex.Clear();
            TagReferenceIndex.IndexPath = previousIndexPath;

            if (Directory.Exists(INDEX_FOLDER))
                Directory.Delete(INDEX_FOLDER, true);
        }



        #region Helpers
        private static string CreateAsset(string _Name, params GameplayTag[] _Tags)
        {
            string path = $"{FOLDER}/{_Name}.asset";
            AssetDatabase.CreateAsset(CreateScriptableObject(_Tags), path);
            return path;
        }

        private static TagSerializationFixture Load(string _Path) => AssetDatabase.LoadAssetAtPath<TagSerializationFixture>(_Path);

        private static string SaveScene(string _Name, GameplayTag _Tag)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Holder").AddComponent<TagHolderBehaviour>().single = _Tag;

            string path = $"{FOLDER}/{_Name}.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, path));
            return path;
        }

        private static List<string> Find(params GameplayTag[] _Tags)
        {
            Assert.IsTrue(TagReferenceIndex.TryFindAssets(_Tags, false, out var paths));
            return paths;
        }

        private static int IndexedAssetCount
            => AssetDatabase.GetAllAssetPaths().Count(e => TagReferenceIndex.IsIndexed(e) && !AssetDatabase.IsValidFolder(e));
        #endregion



        #region Index
        [Test]
        public void SavingAnAsset_UpdatesTheIndexWithoutAScan()
        {
            string path = CreateAsset("Saved", A);
            TagReferenceIndex.Refresh(false);
            CollectionAssert.Contains(Find(A), path);

            SetList(Load(path), C);
            AssetDatabase.SaveAssets();

            var found = Find(C);
            Assert.AreEqual(0, TagReferenceIndex.LastScanCount, "the save updated the entry");
            CollectionAssert.Contains(found, path);
            CollectionAssert.DoesNotContain(Find(A), path);
        }

        [Test]
        public void ChangesFromOutside_AreScannedBeforeTheQuery()
        {
            const int COUNT = 200;
            var paths = new List<string>();

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < COUNT; i++)
                    paths.Add(CreateAsset($"Pulled{i:000}", A));
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            CollectionAssert.IsSubsetOf(paths, Find(A));

            // A pull: every file now references C instead of A.
            foreach (string path in paths)
            {
                string text = File.ReadAllText(path)
                    .Replace($": {A.Id.Hi}", $": {C.Id.Hi}")
                    .Replace($": {A.Id.Lo}", $": {C.Id.Lo}");
                File.WriteAllText(path, text);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var found = Find(C);
            Assert.AreEqual(COUNT, TagReferenceIndex.LastScanCount, "only the changed assets are scanned");
            CollectionAssert.IsSubsetOf(paths, found);
            CollectionAssert.IsEmpty(Find(A).Intersect(paths));
        }

        [Test]
        public void AfterALibraryWipe_TheFirstQueryScansEverythingWithProgress()
        {
            string path = CreateAsset("Cold", D);
            TagReferenceIndex.Refresh(false);

            TagReferenceIndex.Clear();
            Assert.IsFalse(File.Exists(INDEX));

            int reports = 0;
            TagReferenceIndex.ProgressOverride = (_, _) =>
            {
                reports++;
                return false;
            };

            Assert.IsTrue(TagReferenceIndex.TryFindAssets(new[] { D }, true, out var found));

            Assert.AreEqual(IndexedAssetCount, TagReferenceIndex.LastScanCount);
            Assert.AreEqual(IndexedAssetCount, reports);
            CollectionAssert.AreEqual(new[] { path }, found);
        }

        [Test]
        public void CancelledScan_IsReportedIncomplete()
        {
            CreateAsset("Cancelled", D);
            TagReferenceIndex.ProgressOverride = (_, _) => true;

            Assert.IsFalse(TagReferenceIndex.TryFindAssets(new[] { D }, true, out _));

            // The next query picks up what was left.
            TagReferenceIndex.ProgressOverride = null;
            Assert.AreEqual(1, Find(D).Count);
        }

        [Test]
        public void IndexFiles_AreOnlyUnderLibrary()
        {
            StringAssert.StartsWith("Library/", TagReferenceIndex.DEFAULT_PATH);

            CreateAsset("Indexed", A);
            var before = AssetDatabase.GetAllAssetPaths().OrderBy(e => e).ToList();

            TagReferenceIndex.Refresh(false);
            TagReferenceIndex.Flush();

            Assert.IsTrue(File.Exists(INDEX));
            CollectionAssert.AreEqual(before, AssetDatabase.GetAllAssetPaths().OrderBy(e => e).ToList(), "no asset was added");
            CollectionAssert.IsEmpty(Directory.GetFiles("Assets", "RefIndex*", SearchOption.AllDirectories));
        }

        [Test]
        public void Index_SurvivesReloadingFromItsFile()
        {
            string path = CreateAsset("Persisted", E);
            CollectionAssert.Contains(Find(E), path);
            TagReferenceIndex.Flush();

            // Load another index, then this one again: its entries are read back, so nothing is scanned.
            TagReferenceIndex.IndexPath = INDEX + ".other";
            CollectionAssert.IsEmpty(TagReferenceIndex.GetTags(path));
            TagReferenceIndex.IndexPath = INDEX;

            CollectionAssert.Contains(Find(E), path);
            Assert.AreEqual(0, TagReferenceIndex.LastScanCount);
        }

        [Test]
        public void TextScan_ReadsBlockAndFlowStyles_AndInstanceOverrides()
        {
            const string YAML = @"%YAML 1.1
--- !u!114 &1
MonoBehaviour:
  single:
    _registry:
      _hi: 1
      _lo: 2
    _id:
      _hi: 3
      _lo: 4
    _lastKnownName: Block
  container:
    _tags:
    - _registry: {_hi: 1, _lo: 2}
      _id: {_hi: 5, _lo: 6}
      _lastKnownName: Flow
    - _registry: {_hi: 0, _lo: 0}
      _id: {_hi: 0, _lo: 0}
      _lastKnownName:
--- !u!1001 &2
PrefabInstance:
  m_Modification:
    m_Modifications:
    - target: {fileID: 11, guid: abc, type: 3}
      propertyPath: single._id._hi
      value: 7
      objectReference: {fileID: 0}
    - target: {fileID: 11, guid: abc, type: 3}
      propertyPath: single._id._lo
      value: 8
      objectReference: {fileID: 0}
    - target: {fileID: 11, guid: abc, type: 3}
      propertyPath: single._lastKnownName
      value: Overridden.Tag
      objectReference: {fileID: 0}
    - target: {fileID: 11, guid: abc, type: 3}
      propertyPath: container._tags.Array.data[0]._registry._hi
      value: 9
      objectReference: {fileID: 0}
    - target: {fileID: 11, guid: abc, type: 3}
      propertyPath: container._tags.Array.data[0]._registry._lo
      value: 10
      objectReference: {fileID: 0}
    - target: {fileID: 11, guid: abc, type: 3}
      propertyPath: container._tags.Array.data[0]._id._hi
      value: 11
      objectReference: {fileID: 0}
    - target: {fileID: 11, guid: abc, type: 3}
      propertyPath: container._tags.Array.data[0]._id._lo
      value: 12
      objectReference: {fileID: 0}
";
            var tags = new List<GameplayTag>();
            TagAssetScanner.ScanText(YAML, tags);

            CollectionAssert.AreEquivalent(new[]
            {
                new TagKey(new RegistryGuid(1, 2), new RegistryGuid(3, 4)),
                new TagKey(new RegistryGuid(1, 2), new RegistryGuid(5, 6)),
                new TagKey(RegistryGuid.Zero, new RegistryGuid(7, 8)),   // registry not overridden: unknown
                new TagKey(new RegistryGuid(9, 10), new RegistryGuid(11, 12)),
            }, tags.Select(TagKey.Of));

            CollectionAssert.AreEquivalent(new[] { "Block", "Flow", "Overridden.Tag", "" }, tags.Select(e => e.LastKnownName));

            Assert.IsTrue(new TagKey(RegistryGuid.Zero, new RegistryGuid(7, 8)).Matches(new RegistryGuid(1, 2), new RegistryGuid(7, 8)));
        }

        [Test]
        public void PrefabInstanceOverride_IsListedForTheScene_InheritedValuesForThePrefab()
        {
            string prefabPath = FOLDER + "/Holder.prefab";
            var source = new GameObject("Holder");
            source.AddComponent<TagHolderBehaviour>().single = A;
            var prefab = PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            Object.DestroyImmediate(source);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            using (var serializedObject = new SerializedObject(instance.GetComponent<TagHolderBehaviour>()))
            {
                GameplayTagDrawerHelper.Write(serializedObject.FindProperty(SINGLE), C);
                serializedObject.ApplyModifiedProperties();
            }

            string scenePath = FOLDER + "/Override.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, scenePath));

            CollectionAssert.AreEqual(new[] { prefabPath }, Find(A), "the instance inherits A from the prefab");
            CollectionAssert.AreEqual(new[] { scenePath }, Find(C));
        }
        #endregion



        #region Delete
        [Test]
        public void DeletingAReferencedTag_ListsTheAssets_AndDeleteAnywayWorks()
        {
            Add("A.Child");
            Save();

            string direct = CreateAsset("UsesA", A);
            string viaChild = CreateAsset("UsesChild", Ref("A.Child"));
            CreateAsset("UsesC", C);
            string scene = SaveScene("UsesA", A);

            var plan = GameplayTagCommands.PlanDelete(A.Id, false);

            Assert.IsTrue(plan.ReferenceCheckComplete);
            CollectionAssert.AreEquivalent(new[] { direct, viaChild, scene }, plan.ReferencingAssets);
            CollectionAssert.AreEqual(new[] { "A.Child" }, plan.SubTags);
            StringAssert.Contains(scene, plan.Message);

            // Delete anyway.
            Assert.IsTrue(TagOperations.Delete(A.Id, true).Success);
            Assert.IsTrue(A.Resolve().IsMissing);
        }

        [Test]
        public void DeletingAnUnreferencedTag_SaysSo()
        {
            CreateAsset("UsesC", C);

            var plan = GameplayTagCommands.PlanDelete(D.Id, false);

            CollectionAssert.IsEmpty(plan.ReferencingAssets);
            StringAssert.Contains("No saved asset references it", plan.Message);
        }
        #endregion



        #region Relink And Replace
        [Test]
        public void Relink_PreviewsMatchingNames_AndRewritesOnlyConfirmed()
        {
            string first = CreateAsset("RelinkFirst", B, A);
            string second = CreateAsset("RelinkSecond", B);
            CreateAsset("RelinkUnmatched", D);

            DeleteB();
            Assert.IsTrue(TagOperations.Delete(D.Id, true).Success);
            Add("B");
            Save();
            var newB = Ref("B");
            Assert.AreNotEqual(B.Id, newB.Id, "a re-added name is a new tag");

            Assert.IsTrue(TagReferenceTools.PlanRelink(false, out var changes));

            CollectionAssert.AreEquivalent(new[] { first, second }, changes.Select(e => e.AssetPath));
            Assert.IsTrue(changes.All(e => e.From.Equals(B) && e.To.Equals(newB)), "D has no live name to relink to");

            changes.Single(e => e.AssetPath == second).Selected = false;
            var result = TagReferenceTools.Apply(changes, TagReferenceTools.RELINK_UNDO);

            Assert.AreEqual(1, result.Rewritten);
            Assert.AreEqual(0, result.Skipped);
            AssertList(Load(first), newB, A);
            Assert.AreEqual("B", GetList(Load(first))[0].LastKnownName);
            AssertList(Load(second), B);
            Assert.IsTrue(GetList(Load(second))[0].Resolve().IsMissing, "unconfirmed references stay (deleted)");
        }

        [Test]
        public void Relink_InAClosedScene_IsSaved()
        {
            string scene = SaveScene("RelinkScene", B);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            DeleteB();
            Add("B");
            Save();
            var newB = Ref("B");

            Assert.IsTrue(TagReferenceTools.PlanRelink(false, out var changes));
            Assert.AreEqual(1, changes.Count);

            var result = TagReferenceTools.Apply(changes, TagReferenceTools.RELINK_UNDO);
            Assert.AreEqual(1, result.Rewritten);
            CollectionAssert.IsEmpty(result.UnsavedScenes);

            CollectionAssert.AreEqual(new[] { scene }, Find(newB));
            Assert.AreEqual(1, EditorSceneManager.sceneCount, "the scene was closed again");
        }

        [Test]
        public void Replace_PreviewsThenRewrites_AndKeepsTheOldTag()
        {
            string path = CreateAsset("Replace", A, C, A);
            string other = CreateAsset("ReplaceOther", C);

            Assert.IsTrue(TagReferenceTools.PlanReplace(A, E, false, out var changes));

            Assert.AreEqual(2, changes.Count);
            Assert.IsTrue(changes.All(e => e.AssetPath == path && e.From.Equals(A) && e.To.Equals(E)));

            var result = TagReferenceTools.Apply(changes, TagReferenceTools.REPLACE_UNDO);

            Assert.AreEqual(2, result.Rewritten);
            AssertList(Load(path), E, C, E);
            AssertList(Load(other), C);
            Assert.IsTrue(Main.Contains(A.Id), "A is not deleted");
            CollectionAssert.DoesNotContain(Find(A), path);
        }

        [Test]
        public void Apply_SkipsReferencesChangedSinceThePreview()
        {
            string path = CreateAsset("Changed", A);
            Assert.IsTrue(TagReferenceTools.PlanReplace(A, E, false, out var changes));

            SetList(Load(path), C);
            var result = TagReferenceTools.Apply(changes, TagReferenceTools.REPLACE_UNDO);

            Assert.AreEqual(0, result.Rewritten);
            Assert.AreEqual(1, result.Skipped);
            AssertList(Load(path), C);
        }
        #endregion
    }
}
