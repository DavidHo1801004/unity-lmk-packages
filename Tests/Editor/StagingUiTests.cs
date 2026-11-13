using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;
using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// Tag markers: per user, staged like registry edits, saved to a file under <c>Temp/</c> here.
    /// </summary>
    public sealed class MarkerTests : OperationTestBase
    {
        private static readonly Color RED = new(1f, 0f, 0f, 1f);

        [Test]
        public void SetMarker_IsStaged_SaveWritesTheFile()
        {
            var a = Add("A");
            Save();

            TagOperations.SetMarker(Main.Id, a, RED);

            Assert.IsTrue(TagOperations.TryGetMarker(Main.Id, a, out var color));
            Assert.AreEqual(RED, color);
            Assert.IsTrue(TagOperations.HasPendingChanges);
            Assert.IsFalse(File.Exists(MARKERS), "staged: nothing written");

            Save();

            var saved = TagMarkerStore.Load();
            Assert.AreEqual(1, saved.Count);
            Assert.IsTrue(saved[0].Is(Main.Id, a));
            Assert.AreEqual(RED, saved[0].color);
        }

        [Test]
        public void Discard_DropsStagedMarkers()
        {
            var a = Add("A");
            Save();

            TagOperations.SetMarker(Main.Id, a, RED);
            TagOperations.Discard();

            Assert.IsFalse(TagOperations.TryGetMarker(Main.Id, a, out _));
            Assert.IsFalse(TagOperations.HasPendingChanges);
        }

        [Test]
        public void TransparentColour_RemovesTheMarker_UndoRestoresIt()
        {
            var a = Add("A");
            TagOperations.SetMarker(Main.Id, a, RED);
            Save();

            Undo.IncrementCurrentGroup();
            TagOperations.SetMarker(Main.Id, a, Color.clear);
            Assert.IsFalse(TagOperations.TryGetMarker(Main.Id, a, out _));

            Undo.PerformUndo();

            Assert.IsTrue(TagOperations.TryGetMarker(Main.Id, a, out var color));
            Assert.AreEqual(RED, color);
            Assert.IsFalse(TagOperations.HasPendingChanges, "back to the saved markers");
        }

        [Test]
        public void Markers_OnReadOnlyTags_NeedNoLock()
        {
            var vendor = new RegistryData<TagRegistryRow>
            {
                registryId = RegistryGuid.Parse("b0000000000000000000000000000002"),
                name = "Vendor",
                origin = RegistryOrigin.Package,
                rows = new List<RegistryRow<TagRegistryRow>> { TagRegistryRow.Create(TestIds.Id(1), RegistryGuid.Zero, "Stun", false) },
            };
            RegistryServiceTests.WriteAndImport(FOLDER + "/Vendor.tagregistry", TagRegistryFile.Write(vendor));
            RegistryLockProvider.Current = new RefusingLockProvider();

            TagOperations.SetMarker(vendor.registryId, TestIds.Id(1), RED);
            Assert.IsTrue(TagOperations.Save().Success, "only markers changed: the registry lock isn't needed");

            Assert.IsTrue(TagMarkerStore.Load().Single().Is(vendor.registryId, TestIds.Id(1)));
        }

        [Test]
        public void MarkerStrip_ShowsTheColour()
        {
            var a = Add("A");
            var tree = new GameplayTagTreeElement(TagRegistryService.Snapshot.Root, new GameplayTagTreeOptions());
            var marker = tree.Find($"{Main.Id}:{a}").Row.Q("marker");
            Assert.AreEqual(DisplayStyle.None, marker.style.display.value, "no marker by default");

            TagOperations.SetMarker(Main.Id, a, RED);
            tree.SyncMarkers();

            Assert.AreEqual(DisplayStyle.Flex, marker.style.display.value);
            Assert.AreEqual(RED, marker.style.backgroundColor.value);
        }
    }



    /// <summary>
    /// The Gameplay Tag Manager's toolbar, tree selection and inspector panel, built without a window.
    /// </summary>
    public sealed class ManagerUiTests : OperationTestBase
    {
        [Test]
        public void Toolbar_SaveAndDiscard_FollowPendingChanges()
        {
            var toolbar = new GameplayTagManagerToolbar(new GameplayTagControlPanel());
            var save = toolbar.Q<Button>("save-button");
            var discard = toolbar.Q<Button>("discard-button");
            Assert.IsFalse(save.enabledSelf);
            Assert.IsFalse(discard.enabledSelf);

            Add("A");
            toolbar.Refresh();
            Assert.IsTrue(save.enabledSelf);
            Assert.IsTrue(discard.enabledSelf);

            Save();
            toolbar.Refresh();
            Assert.IsFalse(save.enabledSelf);
        }

        [Test]
        public void Toolbar_ItemsInOrder()
        {
            var toolbar = new GameplayTagManagerToolbar(new GameplayTagControlPanel());
            var items = new[] { "save-button", "discard-button", "search-field", "add-tag-button", "options-button" };

            // Since 1.1.0 the tag controls sit in the registry toolbar, after Save and Discard.
            CollectionAssert.AreEqual(items, toolbar.Query<VisualElement>().ToList().Select(e => e.name).Where(items.Contains));
            Assert.IsInstanceOf<ToolbarSearchField>(toolbar.Q("search-field"));
        }

        [Test]
        public void Selection_FollowsTheTagAcrossRebuilds()
        {
            var a = Add("A.B");
            var panel = new GameplayTagControlPanel { RowSelection = true };
            panel.RefreshTagTree();

            GameplayTagNode selected = null;
            panel.SelectionChanged += e => selected = e;

            Assert.IsTrue(panel.Select(Main.Id, a));
            Assert.AreEqual("A.B", selected.FullTag);

            TagOperations.Rename(a, "C");
            panel.RefreshTagTree();

            Assert.AreEqual("A.C", selected.FullTag, "the same tag, rebuilt with its new name");
            Assert.AreSame(selected, panel.SelectedNode);
        }

        [Test]
        public void Inspector_ShowsTheTagRowsInOrder()
        {
            Assert.IsTrue(TagOperations.Add("A.B", "Text").Success);
            var inspector = new GameplayTagInspectorPanel();

            inspector.SetNode(NodeOf("A.B"));

            Assert.AreEqual("B", inspector.Q<EditableLabel>("tag-title").value);
            Assert.AreEqual(Main.Name, inspector.Q<LinkLabel>("registry-link").text);
            CollectionAssert.AreEqual(
                new[] { "Is Explicit", "Parent", "Description", "Marker" },
                inspector.Query<FieldRow>().ToList().Select(e => e.Label));
            Assert.AreEqual("Yes", inspector.Q<Label>("explicit-value").text);
            Assert.IsNull(inspector.Q("make-explicit-button"));
            Assert.AreEqual("A", inspector.Q<LinkLabel>("parent-value").text);
            Assert.AreEqual("Text", inspector.Q<TextField>("description-field").value);
            Assert.AreEqual(Color.clear, inspector.Q<ColorField>("marker-field").value, "no marker: empty");
        }

        [Test]
        public void Inspector_ImplicitTag_OffersMakeExplicit()
        {
            var b = Add("A.B");
            var inspector = new GameplayTagInspectorPanel();

            inspector.SetNode(NodeOf("A"));

            Assert.AreEqual("No", inspector.Q<Label>("explicit-value").text);
            Assert.IsNotNull(inspector.Q("make-explicit-button"));
            Assert.AreEqual("parent-value", inspector.Q<Label>("parent-value").name, "a top level tag has no parent link");
            Assert.IsNull(inspector.Q<LinkLabel>("parent-value"));

            Assert.IsTrue(TagOperations.MakeExplicit(IdOf("A")).Success);
            Assert.IsTrue(Main.TryGetRow(IdOf("A"), out var row) && !row.isImplicit);
            Assert.IsFalse(TagOperations.MakeExplicit(IdOf("A")).Success, "already explicit");
            Assert.IsTrue(Main.TryFind("A.B", out var stillB) && stillB.Equals(b));
        }

        [Test]
        public void Inspector_ReadOnlyTag_OnlyMarkerIsEditable()
        {
            var vendor = new RegistryData<TagRegistryRow>
            {
                registryId = RegistryGuid.Parse("b0000000000000000000000000000002"),
                name = "Vendor",
                origin = RegistryOrigin.Package,
                rows = new List<RegistryRow<TagRegistryRow>> { TagRegistryRow.Create(TestIds.Id(1), RegistryGuid.Zero, "Stun", false) },
            };
            RegistryServiceTests.WriteAndImport(FOLDER + "/Vendor.tagregistry", TagRegistryFile.Write(vendor));
            var inspector = new GameplayTagInspectorPanel();

            inspector.SetNode(TagRegistryService.Snapshot.Root.Find(vendor.registryId, TestIds.Id(1)));

            Assert.IsFalse(inspector.Q<EditableLabel>("tag-title").Editable);
            Assert.IsFalse(inspector.Q<TextField>("description-field").enabledSelf);
            Assert.IsTrue(inspector.Q<ColorField>("marker-field").enabledSelf);
        }

        [Test]
        public void Inspector_NothingOrRegistrySelected_ShowsAPlaceholder()
        {
            Add("A");
            var inspector = new GameplayTagInspectorPanel();

            inspector.SetNode(null);
            Assert.IsNull(inspector.Q<EditableLabel>());

            inspector.SetNode(TagRegistryService.Snapshot.Root.Children.First(e => e.RegistryId.Equals(Main.Id)));
            Assert.IsNull(inspector.Q<EditableLabel>());
        }

        private static GameplayTagNode NodeOf(string _Path)
            => TagRegistryService.Snapshot.Root.Find(Main.Id, IdOf(_Path));
    }
}
