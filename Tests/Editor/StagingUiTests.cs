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
                new[] { "Is Explicit", "Parent", "Description", "Markers" },
                inspector.Query<FieldRow>().ToList().Select(e => e.Label));
            Assert.AreEqual("Yes", inspector.Q<Label>("explicit-value").text);
            Assert.IsNull(inspector.Q("make-explicit-button"));
            Assert.AreEqual("A", inspector.Q<LinkLabel>("parent-value").text);
            Assert.AreEqual("Text", inspector.Q<TextField>("description-field").value);
            Assert.IsNotNull(inspector.Q<TagMarkerField>("marker-field").Q<Label>("marker-empty"), "no markers by default: None");
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
            Assert.IsTrue(inspector.Q<TagMarkerField>("marker-field").AddButton.enabledSelf, "markers are the user's own: any tag can have them");
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

        [Test]
        public void MovePreview_UnderABranch_IsSortedAmongTheChildren()
        {
            Add("T.A");
            Add("T.C");
            Add("M.B.Leaf");
            var tree = new GameplayTagTreeElement(TagRegistryService.Snapshot.Root, new GameplayTagTreeOptions());
            var target = tree.Find($"{Main.Id}:{IdOf("T")}");

            target.ShowMovePreview(NodeOf("M.B"));

            var preview = target.MovePreview;
            Assert.IsNotNull(preview);
            Assert.IsTrue(preview.ClassListContains(GameplayTagNodeElement.USS_CLASS_MOVE_PREVIEW));
            Assert.IsFalse(preview.ClassListContains(GameplayTagNodeElement.USS_CLASS_MOVE_PREVIEW_NESTED));
            Assert.AreEqual(PickingMode.Ignore, preview.pickingMode);

            var shown = preview.parent.Children().OfType<GameplayTagNodeElement>().Select(e => e.TagNode.ShortTag);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, shown);
            Assert.IsTrue(preview.ChildTagElements.Any(e => e.TagNode.ShortTag == "Leaf"), "the subtree moves with it");
            Assert.AreNotSame(preview, tree.Find(NodeOf("M.B").Key), "Find skips the preview");

            target.HideMovePreview();
            Assert.IsNull(target.MovePreview);
            Assert.AreEqual(2, target.ChildTagElements.Count());
        }

        [Test]
        public void MovePreview_UnderALeaf_IsNested()
        {
            Add("Leaf");
            Add("M.B");
            var tree = new GameplayTagTreeElement(TagRegistryService.Snapshot.Root, new GameplayTagTreeOptions());
            var target = tree.Find($"{Main.Id}:{IdOf("Leaf")}");

            target.ShowMovePreview(NodeOf("M.B"));

            Assert.AreSame(target, target.MovePreview.parent);
            Assert.IsTrue(target.MovePreview.ClassListContains(GameplayTagNodeElement.USS_CLASS_MOVE_PREVIEW_NESTED));

            target.HideMovePreview();
            Assert.IsNull(target.Q(className: GameplayTagNodeElement.USS_CLASS_MOVE_PREVIEW));
        }

        [Test]
        public void MoveSource_ShowsAsASingleLeaf_UntilTheDragEnds()
        {
            Add("A.B");
            var tree = new GameplayTagTreeElement(TagRegistryService.Snapshot.Root, new GameplayTagTreeOptions());
            var a = tree.Find($"{Main.Id}:{IdOf("A")}");
            var label = a.Q<Label>("leaf-tag");

            a.SetMoveSource(true);

            Assert.IsTrue(a.ClassListContains(GameplayTagNodeElement.USS_CLASS_MOVE_SOURCE));
            Assert.AreEqual("A" + GameplayTagNodeElement.MOVE_SOURCE_SUFFIX, label.text);

            a.SetMoveSource(false);

            Assert.IsFalse(a.ClassListContains(GameplayTagNodeElement.USS_CLASS_MOVE_SOURCE));
            Assert.AreEqual("A", label.text);
        }

        [Test]
        public void MoveManipulator_FindsTheRowOwner_NotItsParent()
        {
            Add("A.B");
            var tree = new GameplayTagTreeElement(TagRegistryService.Snapshot.Root, new GameplayTagTreeOptions());
            var b = tree.Find($"{Main.Id}:{IdOf("A.B")}");

            Assert.AreSame(b, GameplayTagMoveManipulator.FindRowOwner(b.Row));
            Assert.AreSame(b, GameplayTagMoveManipulator.FindRowOwner(b.Row.Q<Label>()));
            Assert.IsNull(GameplayTagMoveManipulator.FindRowOwner(b), "between rows");
        }

        private static GameplayTagNode NodeOf(string _Path)
            => TagRegistryService.Snapshot.Root.Find(Main.Id, IdOf(_Path));
    }
}
