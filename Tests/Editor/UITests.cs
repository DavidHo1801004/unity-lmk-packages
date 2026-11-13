using System.Linq;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Runtime.Registry;

using Object = UnityEngine.Object;

namespace LMK.Editor.Registry.Tests
{
    public sealed class UITests : KindTestBase
    {
        private static RegistryResult Add<TAsset>(TreeKindBase<TAsset> _Kind, string _Segment) where TAsset : RegistryAsset<TreeRow>
            => RegistryEdit.Apply(_Kind, "Add", data =>
            {
                var id = RegistryGuid.NewGuid();
                data.rows.Add(new RegistryRow<TreeRow>(id, new TreeRow(RegistryGuid.Zero, _Segment)));
                return RegistryResult.Ok(id);
            });



        #region Toolbar and window
        [Test]
        public void Toolbar_SaveAndDiscardFollowPendingChanges_LockShowsItsProvider()
        {
            var toolbar = new RegistryToolbar();
            toolbar.AddControl(new Button { name = "domain-control" });
            Assert.IsFalse(toolbar.SaveButton.enabledSelf);
            Assert.IsFalse(toolbar.DiscardButton.enabledSelf);
            Assert.AreEqual(DisplayStyle.None, toolbar.LockLabel.style.display.value, "no lock: nothing to show");
            Assert.IsNotNull(toolbar.Q<Button>("domain-control"));

            Add(b, "Fire");
            RegistryLockProvider.Current = new TestLockProvider();
            toolbar.Refresh();

            Assert.IsTrue(toolbar.SaveButton.enabledSelf, "edits of any kind count");
            Assert.IsTrue(toolbar.DiscardButton.enabledSelf);
            StringAssert.Contains("Test Lock", toolbar.LockLabel.text);
        }

        [Test]
        public void Window_FollowsTheSession_AndKeepsQuittingToItself()
        {
            var window = ScriptableObject.CreateInstance<TestRegistryWindow>();
            try
            {
                Assert.IsFalse(window.hasUnsavedChanges);
                Assert.IsTrue(RegistrySession.QuitPromptSuppressors.Any(e => e()), "an open registry window asks before quitting");

                Add(a, "Fire");

                Assert.IsTrue(window.hasUnsavedChanges);
                StringAssert.Contains(a.DisplayName, window.saveChangesMessage);
                Assert.Greater(window.stateChanges, 0);

                RegistrySession.Save();
                Assert.IsFalse(window.hasUnsavedChanges);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }
        #endregion



        #region Markers
        [Test]
        public void MarkerField_StagesTheColour_AndClearRemovesIt()
        {
            var registry = a.MainRegistryId;
            var row = Add(a, "Fire").Id;
            var field = new RegistryMarkerField(a, registry, row);
            Assert.AreEqual(DisplayStyle.None, field.ClearButton.style.display.value);

            field.SetColor(new Color(1f, 0f, 0f, 0f));

            Assert.IsTrue(a.TryGetMarker(registry, row, out var color));
            Assert.AreEqual(1f, color.a, "a picked colour is opaque");
            Assert.AreEqual(DisplayStyle.Flex, field.ClearButton.style.display.value);

            field.ClearMarker();

            Assert.IsFalse(a.TryGetMarker(registry, row, out _));
            Assert.AreEqual(DisplayStyle.None, field.ClearButton.style.display.value);
        }
        #endregion



        #region Drawers
        [Test]
        public void Drawer_RemovesMissingReferences_FromFieldsAndLists()
        {
            var mainA = a.MainRegistryId;
            var fire = Add(a, "Fire").Id;
            RegistrySession.Save();

            string path = CreateHolder(CreateFolder("Drawers"), "Holder", h =>
            {
                h.single = new TreeRef(mainA, RegistryGuid.NewGuid(), "Gone");
                h.list.Add(new TreeRef(mainA, RegistryGuid.NewGuid(), "GoneA"));
                h.list.Add(new TreeRef(mainA, fire, "Fire"));
                h.list.Add(new TreeRef(mainA, RegistryGuid.NewGuid(), "GoneB"));
            });
            var holder = AssetDatabase.LoadAssetAtPath<ReferenceHolder>(path);
            var targets = new Object[] { holder };

            Assert.AreEqual(1, RegistryDrawerHelper.CountMissing(targets, "single", false));
            Assert.AreEqual(2, RegistryDrawerHelper.CountMissing(targets, "list", true));

            RegistryDrawerHelper.RemoveMissing(targets, "single", false, "Clear");
            RegistryDrawerHelper.RemoveMissing(targets, "list", true, "Remove");

            Assert.IsTrue(holder.single.Id.IsZero);
            Assert.AreEqual(1, holder.list.Count, "outside prefab assets, missing entries are removed");
            Assert.AreEqual(fire, holder.list[0].Id);

            Undo.PerformUndo();
            Assert.AreEqual(3, holder.list.Count, "one undo per removal");
        }

        [Test]
        public void Drawer_InPrefabAssets_KeepsListIndices_UntilCompacted()
        {
            var mainA = a.MainRegistryId;
            var fire = Add(a, "Fire").Id;
            RegistrySession.Save();

            var gameObject = new GameObject("Holder");
            var behaviour = gameObject.AddComponent<ReferenceHolderBehaviour>();
            behaviour.list.Add(new TreeRef(mainA, RegistryGuid.NewGuid(), "Gone"));
            behaviour.list.Add(new TreeRef(mainA, fire, "Fire"));
            string path = $"{CreateFolder("Drawers")}/Holder.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(gameObject, path);
            Object.DestroyImmediate(gameObject);

            var component = prefab.GetComponent<ReferenceHolderBehaviour>();
            var targets = new Object[] { component };
            Assert.IsTrue(RegistryDrawerHelper.IsPrefabAssetContext(component));

            RegistryDrawerHelper.RemoveMissing(targets, "list", true, "Remove");

            Assert.AreEqual(2, component.list.Count, "indices stay: the missing entry becomes None");
            Assert.IsTrue(component.list[0].Id.IsZero);
            using (var serializedObject = new SerializedObject(component))
                Assert.IsTrue(RegistryDrawerHelper.HasNoneEntries(serializedObject.FindProperty("list")));

            RegistryDrawerHelper.Compact(targets, "list", "Compact");

            Assert.AreEqual(1, component.list.Count);
            Assert.AreEqual(fire, component.list[0].Id);
        }

        [Test]
        public void Drawer_MissingLabelsNameTheRow()
        {
            var reference = new RegistryReference(a, RegistryGuid.NewGuid(), RegistryGuid.NewGuid(), "Status.Stun");

            Assert.AreEqual("Status.Stun (deleted)", RegistryDrawerHelper.GetMissingLabel(reference));
            StringAssert.Contains("is not loaded", RegistryDrawerHelper.GetMissingTooltip(reference, RegistryStatus.MissingRegistry));
            StringAssert.Contains("tag was deleted", RegistryDrawerHelper.GetMissingTooltip(reference, RegistryStatus.Deleted));
            Assert.AreEqual("Unknown tag (deleted)", RegistryDrawerHelper.GetMissingLabel(new RegistryReference(a, RegistryGuid.Zero, RegistryGuid.NewGuid(), null)));
        }
        #endregion



        #region Inspector, settings and pickers
        [Test]
        public void ImporterInspector_ShowsTheRegistry_AndTheCodeToggleOnlyWithCode()
        {
            Add(a, "Fire");
            RegistrySession.Save();

            var importer = AssetImporter.GetAtPath(a.MainRegistryPath);
            var editor = UnityEditor.Editor.CreateEditor(importer);
            try
            {
                var root = editor.CreateInspectorGUI();
                var texts = root.Query<Label>().ToList().Select(e => e.text).ToList();

                CollectionAssert.Contains(texts, "Main");
                CollectionAssert.Contains(texts, a.MainRegistryId.ToString());
                CollectionAssert.Contains(texts, "1");
                Assert.IsNull(root.Q<Toggle>("generate-code-toggle"), "a kind without generated code has no toggle");
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void Settings_LockProviderIsChosenByType()
        {
            var types = RegistrySettings.LockProviderTypes;
            Assert.AreEqual(typeof(NoneLockProvider), types[0]);
            CollectionAssert.Contains(types, typeof(TestLockProvider));

            try
            {
                RegistrySettings.instance.SetLockProvider(typeof(TestLockProvider), false);

                Assert.IsInstanceOf<TestLockProvider>(RegistryLockProvider.Current);
                Assert.AreEqual(typeof(TestLockProvider), RegistrySettings.instance.LockProviderType);
            }
            finally
            {
                RegistrySettings.instance.SetLockProvider(null, false);
            }

            Assert.IsInstanceOf<NoneLockProvider>(RegistryLockProvider.Current);
        }

        [Test]
        public void EnumerateRows_ListsEveryLiveRowWithItsRegistry()
        {
            var fire = Add(a, "Fire").Id;

            var rows = a.EnumerateRows().ToList();

            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual((a.MainRegistryId, "Main", fire, "Fire"), rows[0]);
        }

        [Test]
        public void DescribeReferences_ListsTheReferencingAssets()
        {
            var mainA = a.MainRegistryId;
            var fire = Add(a, "Fire").Id;
            RegistrySession.Save();
            string path = CreateHolder(CreateFolder("Commands"), "Holder", h => h.single = new TreeRef(mainA, fire, "Fire"));

            string message = RegistryCommands.DescribeReferences(a, new[] { (mainA, fire) }, out var assets, out bool complete);

            Assert.IsTrue(complete);
            CollectionAssert.AreEqual(new[] { path }, assets);
            StringAssert.Contains(path, message);
            StringAssert.Contains("Relink Missing References", message);
        }
        #endregion
    }
}
