using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Runtime.GameplayTags;
using LMK.GameplayTags.Tests;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// Shared setup: tags A, B, C, D, E in a temporary main registry, saved. B is deleted once the test data exists.
    /// </summary>
    public abstract class TagFieldTestBase : OperationTestBase
    {
        protected const string CONTAINER = "container";
        protected const string SINGLE = "single";

        protected GameplayTag A, B, C, D, E;

        [SetUp]
        public void CreateTags()
        {
            foreach (string path in new[] { "A", "B", "C", "D", "E" })
                Add(path);
            Save();

            A = Ref("A");
            B = Ref("B");
            C = Ref("C");
            D = Ref("D");
            E = Ref("E");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void CloseScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        protected void DeleteB()
        {
            Assert.IsTrue(TagOperations.Delete(B.Id, true).Success);
            Save();
        }

        protected static void SetList(Object _Target, params GameplayTag[] _Tags)
        {
            using var serializedObject = new SerializedObject(_Target);
            var tags = serializedObject.FindProperty(CONTAINER + "." + GameplayTagDrawerHelper.CONTAINER_TAGS_PROPERTY);

            tags.arraySize = _Tags.Length;
            for (int i = 0; i < _Tags.Length; i++)
                GameplayTagDrawerHelper.Write(tags.GetArrayElementAtIndex(i), _Tags[i]);

            serializedObject.ApplyModifiedProperties();
        }

        protected static void SetEntry(Object _Target, int _Index, GameplayTag _Tag)
        {
            using var serializedObject = new SerializedObject(_Target);
            var tags = serializedObject.FindProperty(CONTAINER + "." + GameplayTagDrawerHelper.CONTAINER_TAGS_PROPERTY);

            GameplayTagDrawerHelper.Write(tags.GetArrayElementAtIndex(_Index), _Tag);
            serializedObject.ApplyModifiedProperties();
        }

        protected static List<GameplayTag> GetList(Object _Target)
        {
            using var serializedObject = new SerializedObject(_Target);
            return GameplayTagDrawerHelper.ReadList(serializedObject.FindProperty(CONTAINER + "." + GameplayTagDrawerHelper.CONTAINER_TAGS_PROPERTY));
        }

        protected static void AssertList(Object _Target, params GameplayTag[] _Expected)
        {
            var actual = GetList(_Target);
            CollectionAssert.AreEqual(_Expected, actual, $"was [{string.Join(", ", actual.Select(e => e.IsNone ? "None" : e.LastKnownName))}]");
        }

        protected static TagSerializationFixture CreateScriptableObject(params GameplayTag[] _Tags)
        {
            var fixture = ScriptableObject.CreateInstance<TagSerializationFixture>();
            SetList(fixture, _Tags);
            return fixture;
        }
    }



    public sealed class DrawerTests : TagFieldTestBase
    {
        [Test]
        public void Write_StoresRegistryIdAndCurrentName()
        {
            var fixture = ScriptableObject.CreateInstance<TagSerializationFixture>();
            using (var serializedObject = new SerializedObject(fixture))
            {
                GameplayTagDrawerHelper.Write(serializedObject.FindProperty(SINGLE), GameplayTag.FromId(A.RegistryId, A.Id, "Stale.Name"));
                serializedObject.ApplyModifiedProperties();
            }

            Assert.AreEqual(A.RegistryId, fixture.single.RegistryId);
            Assert.AreEqual(A.Id, fixture.single.Id);
            Assert.AreEqual("A", fixture.single.LastKnownName, "the current path is written, not the stale one");
            Object.DestroyImmediate(fixture);
        }

        [Test]
        public void ContainerWithMissingEntries_OffersOneRemoval_ThenNothingIsMissing()
        {
            var fixture = CreateScriptableObject(A, B, C);
            DeleteB();
            var targets = new Object[] { fixture };

            Assert.AreEqual(1, GameplayTagDrawerHelper.CountMissing(targets, CONTAINER, true));
            Assert.AreEqual("Remove 1 missing tag(s)", GameplayTagDrawerHelper.GetRemoveMissingLabel(true, 1));

            GameplayTagDrawerHelper.RemoveMissing(targets, CONTAINER, true);

            Assert.AreEqual(0, GameplayTagDrawerHelper.CountMissing(targets, CONTAINER, true));
            AssertList(fixture, A, C);
            Object.DestroyImmediate(fixture);
        }

        [Test]
        public void SingleFieldWithMissingTag_IsCleared()
        {
            var fixture = ScriptableObject.CreateInstance<TagSerializationFixture>();
            fixture.single = B;
            DeleteB();
            var targets = new Object[] { fixture };

            Assert.AreEqual(1, GameplayTagDrawerHelper.CountMissing(targets, SINGLE, false));
            Assert.AreEqual("Clear missing tag", GameplayTagDrawerHelper.GetRemoveMissingLabel(false, 1));

            GameplayTagDrawerHelper.RemoveMissing(targets, SINGLE, false);

            Assert.IsTrue(fixture.single.IsNone);
            Object.DestroyImmediate(fixture);
        }

        [Test]
        public void MultiSelect_RemovesPerTarget_InOneUndo()
        {
            var clean = CreateScriptableObject(A, C);
            var dirty = CreateScriptableObject(B, A, B);
            DeleteB();
            var targets = new Object[] { clean, dirty };

            Assert.AreEqual(2, GameplayTagDrawerHelper.CountMissing(targets, CONTAINER, true));

            GameplayTagDrawerHelper.RemoveMissing(targets, CONTAINER, true);
            AssertList(clean, A, C);
            AssertList(dirty, A);

            Undo.PerformUndo();
            AssertList(dirty, B, A, B);
            AssertList(clean, A, C);

            Object.DestroyImmediate(clean);
            Object.DestroyImmediate(dirty);
        }

        [Test]
        public void ZeroGuid_IsNotMissing()
        {
            var fixture = CreateScriptableObject(A, GameplayTag.None);
            Assert.AreEqual(0, GameplayTagDrawerHelper.CountMissing(new Object[] { fixture }, CONTAINER, true));
            Object.DestroyImmediate(fixture);
        }

        [Test]
        public void Display_ShowsEachState()
        {
            Add("Parent.Child");
            var implicitParent = Ref("Parent");
            var label = new Label();

            GameplayTagDrawerHelper.ApplyDisplay(label, A);
            Assert.AreEqual("A", label.text);
            Assert.IsFalse(label.ClassListContains(GameplayTagDrawerHelper.USS_CLASS_MISSING));

            GameplayTagDrawerHelper.ApplyDisplay(label, implicitParent);
            Assert.IsTrue(label.ClassListContains(GameplayTagDrawerHelper.USS_CLASS_IMPLICIT));

            GameplayTagDrawerHelper.ApplyDisplay(label, GameplayTag.None);
            Assert.AreEqual("None", label.text);
            Assert.IsTrue(label.ClassListContains(GameplayTagDrawerHelper.USS_CLASS_NONE));

            DeleteB();
            GameplayTagDrawerHelper.ApplyDisplay(label, B);
            Assert.AreEqual("B (deleted)", label.text);
            Assert.IsTrue(label.ClassListContains(GameplayTagDrawerHelper.USS_CLASS_MISSING));
            Assert.IsFalse(label.ClassListContains(GameplayTagDrawerHelper.USS_CLASS_IMPLICIT));

            GameplayTagDrawerHelper.ApplyDisplay(label, GameplayTag.FromId(TagGuid.Parse("c0000000000000000000000000000003"), A.Id, "Pkg.Tag"));
            Assert.AreEqual("Pkg.Tag (deleted)", label.text);
            StringAssert.Contains("not loaded", label.tooltip);
        }
    }



    public sealed class PrefabOverrideTests : TagFieldTestBase
    {
        private const string BASE_PATH = FOLDER + "/Base.prefab";

        private static GameObject SavePrefab(string _Path, params GameplayTag[] _Tags)
        {
            var gameObject = new GameObject("Holder");
            SetList(gameObject.AddComponent<TagHolderBehaviour>(), _Tags);

            var prefab = PrefabUtility.SaveAsPrefabAsset(gameObject, _Path);
            Object.DestroyImmediate(gameObject);

            return prefab;
        }

        private static TagHolderBehaviour Holder(GameObject _GameObject) => _GameObject.GetComponent<TagHolderBehaviour>();

        private static GameObject Instantiate(GameObject _Prefab) => (GameObject)PrefabUtility.InstantiatePrefab(_Prefab);

        private static void RemoveMissing(params Object[] _Targets)
        {
            GameplayTagDrawerHelper.RemoveMissing(_Targets, CONTAINER, true);
            AssetDatabase.SaveAssets();
        }



        [Test]
        public void RemoveMissingOnBase_KeepsInstanceOverridesInPlace()
        {
            var prefab = SavePrefab(BASE_PATH, A, B, C);
            var instance = Instantiate(prefab);
            SetEntry(Holder(instance), 2, D);
            DeleteB();

            Assert.IsTrue(GameplayTagDrawerHelper.IsPrefabAssetContext(Holder(prefab)));
            RemoveMissing(Holder(prefab));

            AssertList(Holder(prefab), A, GameplayTag.None, C);
            AssertList(Holder(instance), A, GameplayTag.None, D);

            // At runtime the None entry is ignored. Only A and D match.
            var runtime = Holder(instance).container;
            Assert.AreEqual(2, runtime.Count);
            Assert.IsTrue(runtime.HasTagExact(A));
            Assert.IsTrue(runtime.HasTagExact(D));
            Assert.IsFalse(runtime.HasTag(C));
            Assert.IsFalse(runtime.HasTag(B));
        }

        [Test]
        public void CompactBaseWithoutOverrides_InstancesMatch()
        {
            var prefab = SavePrefab(BASE_PATH, A, GameplayTag.None, C);
            var instance = Instantiate(prefab);

            GameplayTagDrawerHelper.Compact(new Object[] { Holder(prefab) }, CONTAINER);
            AssetDatabase.SaveAssets();

            AssertList(Holder(prefab), A, C);
            AssertList(Holder(instance), A, C);
        }

        [Test]
        public void CompactOnPrefabAsset_IsFlaggedForWarning()
        {
            var prefab = SavePrefab(BASE_PATH, A, GameplayTag.None, C);
            var instance = Instantiate(prefab);
            var fixture = CreateScriptableObject(A, GameplayTag.None);

            Assert.IsTrue(GameplayTagDrawerHelper.IsPrefabAssetContext(Holder(prefab)));
            Assert.IsFalse(GameplayTagDrawerHelper.IsPrefabAssetContext(Holder(instance)));
            Assert.IsFalse(GameplayTagDrawerHelper.IsPrefabAssetContext(fixture));
            Object.DestroyImmediate(fixture);
        }

        [Test]
        public void VariantChain_IndicesStayStableAtEveryLevel()
        {
            var prefab = SavePrefab(BASE_PATH, A, B, C);

            var variantSource = Instantiate(prefab);
            SetEntry(Holder(variantSource), 2, D);
            var variant = PrefabUtility.SaveAsPrefabAsset(variantSource, FOLDER + "/Variant.prefab");
            Object.DestroyImmediate(variantSource);

            var instance = Instantiate(variant);
            SetEntry(Holder(instance), 0, E);
            DeleteB();

            Assert.IsTrue(GameplayTagDrawerHelper.IsPrefabAssetContext(Holder(variant)));
            RemoveMissing(Holder(prefab));

            AssertList(Holder(prefab), A, GameplayTag.None, C);
            AssertList(Holder(variant), A, GameplayTag.None, D);
            AssertList(Holder(instance), E, GameplayTag.None, D);
        }

        [Test]
        public void NestedPrefabOverride_StaysInPlace()
        {
            var inner = SavePrefab(BASE_PATH, A, B, C);

            var outerSource = new GameObject("Outer");
            var nested = Instantiate(inner);
            nested.transform.SetParent(outerSource.transform);
            SetEntry(Holder(nested), 2, D);
            var outer = PrefabUtility.SaveAsPrefabAsset(outerSource, FOLDER + "/Outer.prefab");
            Object.DestroyImmediate(outerSource);
            DeleteB();

            RemoveMissing(Holder(inner));

            AssertList(Holder(inner), A, GameplayTag.None, C);
            AssertList(outer.GetComponentInChildren<TagHolderBehaviour>(), A, GameplayTag.None, D);
        }

        [Test]
        public void RemoveMissingOnSceneInstance_IsAnInstanceOverride()
        {
            var prefab = SavePrefab(BASE_PATH, A, B, C);
            var instance = Instantiate(prefab);
            DeleteB();

            Assert.IsFalse(GameplayTagDrawerHelper.IsPrefabAssetContext(Holder(instance)));
            RemoveMissing(Holder(instance));

            AssertList(Holder(instance), A, C);
            AssertList(Holder(prefab), A, B, C);
            Assert.IsTrue(PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false));
        }

        [Test]
        public void MixedMultiSelect_OneUndoRestoresEverything()
        {
            var prefab = SavePrefab(BASE_PATH, A, B, C);
            var otherPrefab = SavePrefab(FOLDER + "/Other.prefab", B, D);
            var instance = Instantiate(otherPrefab);
            var fixture = CreateScriptableObject(C, B);
            DeleteB();

            RemoveMissing(Holder(prefab), Holder(instance), fixture);

            AssertList(Holder(prefab), A, GameplayTag.None, C);
            AssertList(Holder(instance), D);
            AssertList(fixture, C);

            Undo.PerformUndo();
            AssetDatabase.SaveAssets();

            AssertList(Holder(prefab), A, B, C);
            AssertList(Holder(instance), B, D);
            AssertList(fixture, C, B);
            Object.DestroyImmediate(fixture);
        }

        [Test]
        public void UndoAfterRemoveOnBase_RestoresBaseAndInstance()
        {
            var prefab = SavePrefab(BASE_PATH, A, B, C);
            var instance = Instantiate(prefab);
            SetEntry(Holder(instance), 2, D);
            DeleteB();

            RemoveMissing(Holder(prefab));
            Undo.PerformUndo();
            AssetDatabase.SaveAssets();

            AssertList(Holder(prefab), A, B, C);
            AssertList(Holder(instance), A, B, D);
        }
    }
}
