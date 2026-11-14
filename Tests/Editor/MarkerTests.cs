using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Registry;
using LMK.Editor.Utilities;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// Tag markers: named colours the user defines and assigns to tags of any registry, written to a file under
    /// <c>Temp/</c> here at every change. They are not staged.
    /// </summary>
    public sealed class MarkerTests : OperationTestBase
    {
        private static readonly Color RED = new(1f, 0f, 0f, 1f);
        private static readonly Color BLUE = new(0f, 0f, 1f, 1f);

        #region Definitions
        [Test]
        public void AddDefinition_NeedsAUniqueShortName()
        {
            var wip = AddMarker("WIP", RED);

            Assert.IsFalse(TagMarkers.AddDefinition("", RED).Success);
            Assert.IsFalse(TagMarkers.AddDefinition("   ", RED).Success, "trimmed: still empty");
            Assert.IsFalse(TagMarkers.AddDefinition(new string('x', TagMarkers.MAX_TEXT_LENGTH + 1), RED).Success);
            StringAssert.Contains("already named", TagMarkers.AddDefinition("wip", BLUE).Error, "case-insensitive");

            Assert.IsTrue(TagMarkers.EditDefinition(wip, "Wip", RED).Success, "a marker may keep its own name");
            var other = AddMarker("Other", BLUE);
            Assert.IsFalse(TagMarkers.EditDefinition(other, "WIP", BLUE).Success);

            Assert.IsTrue(TagMarkers.TryGetDefinition(wip, out var definition));
            Assert.AreEqual("Wip", definition.text);
        }

        [Test]
        public void AddDefinition_TrimsTheName_AndStoresAnOpaqueColour()
        {
            var id = AddMarker("  Combat  ", new Color(1f, 0f, 0f, 0.25f));

            Assert.IsTrue(TagMarkers.TryGetDefinition(id, out var definition));
            Assert.AreEqual("Combat", definition.text);
            Assert.AreEqual(1f, definition.color.a);
        }

        [Test]
        public void SeveralMarkers_ComeInDefinitionOrder_AndMoveReorders()
        {
            var a = Add("A");
            var first = AddMarker("First", RED);
            var second = AddMarker("Second", BLUE);

            // Assigned in the other order.
            Assert.IsTrue(TagMarkers.Assign(Main.Id, a, second).Success);
            Assert.IsTrue(TagMarkers.Assign(Main.Id, a, first).Success);
            Assert.IsTrue(TagMarkers.Assign(Main.Id, a, first).Success, "assigning twice changes nothing");

            CollectionAssert.AreEqual(new[] { "First", "Second" }, TagMarkers.GetMarkers(Main.Id, a).Select(e => e.text));

            Assert.IsTrue(TagMarkers.MoveDefinition(second, 0).Success);
            CollectionAssert.AreEqual(new[] { "Second", "First" }, TagMarkers.GetMarkers(Main.Id, a).Select(e => e.text));

            Assert.IsTrue(TagMarkers.Unassign(Main.Id, a, second).Success);
            CollectionAssert.AreEqual(new[] { "First" }, TagMarkers.GetMarkers(Main.Id, a).Select(e => e.text));
            Assert.IsTrue(TagMarkers.HasMarker(Main.Id, a, first));
            Assert.AreEqual("First", TagMarkers.GetSearchText(Main.Id, a));
        }

        [Test]
        public void Assign_UnknownMarkerOrNoTag_IsRejected()
        {
            var a = Add("A");

            Assert.IsFalse(TagMarkers.Assign(Main.Id, a, TestIds.Id(99)).Success);
            Assert.IsFalse(TagMarkers.Assign(Main.Id, RegistryGuid.Zero, AddMarker("M", RED)).Success);
        }

        [Test]
        public void DeleteDefinition_RemovesItFromEveryTag_OneUndoRestoresBoth()
        {
            var a = Add("A");
            var b = Add("B");
            var wip = AddMarker("WIP", RED);
            TagMarkers.Assign(Main.Id, a, wip);
            TagMarkers.Assign(Main.Id, b, wip);
            Assert.AreEqual(2, TagMarkers.CountUses(wip));

            Assert.IsTrue(TagMarkers.DeleteDefinition(wip).Success);

            Assert.IsFalse(TagMarkers.TryGetDefinition(wip, out _));
            Assert.AreEqual(0, TagMarkers.GetMarkers(Main.Id, a).Count);

            Undo.PerformUndo();

            Assert.IsTrue(TagMarkers.TryGetDefinition(wip, out _));
            Assert.AreEqual(2, TagMarkers.CountUses(wip));
        }

        [Test]
        public void EditsWithinOneEvent_AreOneUndoStep()
        {
            var wip = AddMarker("WIP", RED);

            // Like dragging in the colour picker.
            Undo.IncrementCurrentGroup();
            TagMarkers.EditDefinition(wip, "WIP", new Color(0.2f, 0f, 0f));
            TagMarkers.EditDefinition(wip, "WIP", new Color(0.4f, 0f, 0f));
            TagMarkers.EditDefinition(wip, "WIP", BLUE);

            Undo.PerformUndo();

            Assert.IsTrue(TagMarkers.TryGetDefinition(wip, out var definition));
            Assert.IsTrue(TagMarkerSet.SameColor(RED, definition.color), "back to the colour before the drag");
        }
        #endregion



        #region Writing
        [Test]
        public void Changes_AreWrittenAtOnce_NotStaged_AndUndoRewritesTheFile()
        {
            var a = Add("A");
            Save();

            var wip = AddMarker("WIP", RED);
            Assert.IsTrue(TagMarkers.Assign(Main.Id, a, wip).Success);

            Assert.IsTrue(File.Exists(MARKERS), "written at once");
            Assert.IsFalse(TagOperations.HasPendingChanges, "markers are not staged");
            Assert.AreEqual(1, TagMarkerStore.Load(MARKERS).assignments.Count);

            Undo.PerformUndo();
            Assert.AreEqual(0, TagMarkerStore.Load(MARKERS).assignments.Count, "undo rewrites the file");

            Undo.PerformRedo();
            Assert.AreEqual(1, TagMarkerStore.Load(MARKERS).assignments.Count);

            TagOperations.Discard();
            Assert.AreEqual(1, TagMarkers.GetMarkers(Main.Id, a).Count, "discard doesn't touch markers");
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

            var wip = AddMarker("WIP", RED);
            Assert.IsTrue(TagMarkers.Assign(vendor.registryId, TestIds.Id(1), wip).Success);

            Assert.IsTrue(TagMarkerStore.Load(MARKERS).assignments.Single().Is(vendor.registryId, TestIds.Id(1)));
        }

        [Test]
        public void Store_RoundTrip()
        {
            var set = new TagMarkerSet();
            set.definitions.Add(new TagMarkerDefinition(TestIds.Id(10), "WIP", RED));
            set.definitions.Add(new TagMarkerDefinition(TestIds.Id(11), "Combat", BLUE));
            set.assignments.Add(new TagMarkerAssignment(Main.Id, TestIds.Id(1), TestIds.Id(11)));

            TagMarkerStore.Save(MARKERS, set);

            Assert.IsTrue(TagMarkerStore.Load(MARKERS).ContentEquals(set));
            StringAssert.Contains("\"version\": 2", File.ReadAllText(MARKERS));

            TagMarkerStore.Save(MARKERS, new TagMarkerSet());
            Assert.IsFalse(File.Exists(MARKERS), "no markers: no file");
        }

        [Test]
        public void Store_Version1_BecomesNamedMarkers_AndIsRewritten()
        {
            Directory.CreateDirectory(CODE_FOLDER);
            string registry = Main.Id.ToString();
            File.WriteAllText(MARKERS, "{ \"markers\": [" +
                $"{{ \"registry\": \"{registry}\", \"tag\": \"{TestIds.Id(1)}\", \"color\": \"FF0000FF\" }}," +
                $"{{ \"registry\": \"{registry}\", \"tag\": \"{TestIds.Id(2)}\", \"color\": \"0000FFFF\" }}," +
                $"{{ \"registry\": \"{registry}\", \"tag\": \"{TestIds.Id(3)}\", \"color\": \"FF0000FF\" }}" +
                "] }");
            TagMarkers.Reload();

            CollectionAssert.AreEqual(new[] { "Marker 1", "Marker 2" }, TagMarkers.Definitions.Select(e => e.text));
            Assert.AreEqual("Marker 1", TagMarkers.GetMarkers(Main.Id, TestIds.Id(1)).Single().text);
            Assert.AreEqual("Marker 2", TagMarkers.GetMarkers(Main.Id, TestIds.Id(2)).Single().text);
            Assert.AreEqual("Marker 1", TagMarkers.GetMarkers(Main.Id, TestIds.Id(3)).Single().text, "same colour, same marker");
            Assert.IsTrue(TagMarkerSet.SameColor(RED, TagMarkers.Definitions[0].color));

            StringAssert.Contains("\"version\": 2", File.ReadAllText(MARKERS), "rewritten as version 2");
        }

        [Test]
        public void Store_SkipsUnknownMarkers_AndRenamesDuplicates()
        {
            Directory.CreateDirectory(CODE_FOLDER);
            File.WriteAllText(MARKERS, "{ \"version\": 2, \"definitions\": [" +
                $"{{ \"id\": \"{TestIds.Id(10)}\", \"text\": \"WIP\", \"color\": \"FF0000\" }}," +
                $"{{ \"id\": \"{TestIds.Id(11)}\", \"text\": \"wip\", \"color\": \"0000FF\" }}" +
                "], \"assignments\": [" +
                $"{{ \"registry\": \"{Main.Id}\", \"tag\": \"{TestIds.Id(1)}\", \"marker\": \"{TestIds.Id(99)}\" }}" +
                "] }");

            var set = TagMarkerStore.Load(MARKERS);

            CollectionAssert.AreEqual(new[] { "WIP", "wip (2)" }, set.definitions.Select(e => e.text));
            Assert.AreEqual(0, set.assignments.Count, "the marker doesn't exist");
        }
        #endregion



        #region UI
        [Test]
        public void MarkerField_ShowsAChipPerMarker()
        {
            var a = Add("A");
            var field = new TagMarkerField(Main.Id, a);
            Assert.AreEqual(0, field.Query(className: TagMarkerField.USS_CLASS_CHIP).ToList().Count);

            TagMarkers.Assign(Main.Id, a, AddMarker("WIP", RED));
            TagMarkers.Assign(Main.Id, a, AddMarker("Combat", BLUE));
            field.Refresh();

            var chips = field.Query(className: TagMarkerField.USS_CLASS_CHIP).ToList();
            CollectionAssert.AreEqual(new[] { "WIP", "Combat" }, chips.Select(e => e.Q<Label>().text));
            Assert.IsTrue(TagMarkerSet.SameColor(RED, chips[0].Q<Label>().style.backgroundColor.value));
        }

        [Test]
        public void ManageMarkers_ARowPerMarker_UpdatedInPlace()
        {
            var first = AddMarker("First", RED);
            var second = AddMarker("Second", BLUE);
            var view = new TagMarkerListView();

            CollectionAssert.AreEqual(new[] { first, second }, view.RowIds);
            var rows = view.Query(className: TagMarkerListView.USS_CLASS_ROW).ToList();
            Assert.AreEqual("First", rows[0].Q<TextField>("marker-text").value);

            Assert.IsTrue(view.Recolor(first, BLUE).Success);
            view.Refresh();
            Assert.AreSame(rows[0], view.Query(className: TagMarkerListView.USS_CLASS_ROW).First(), "same markers: the row is kept, with an open colour picker");

            Assert.IsTrue(view.Move(second, 0).Success);
            view.Refresh();
            CollectionAssert.AreEqual(new[] { second, first }, view.RowIds);
        }

        [Test]
        public void ManageMarkers_Add_NamesItAndPicksTheNextColour()
        {
            AddMarker("Marker 2", RED);
            var view = new TagMarkerListView();

            var result = view.Add();

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsTrue(TagMarkers.TryGetDefinition(result.Id, out var added));
            Assert.AreEqual("Marker 3", added.text, "skips a taken name");
            Assert.IsTrue(TagMarkerSet.SameColor(TagMarkerListView.PALETTE[1], added.color));
        }

        [Test]
        public void ManageMarkers_RenameToATakenName_Fails()
        {
            AddMarker("WIP", RED);
            var other = AddMarker("Other", BLUE);
            var view = new TagMarkerListView();

            Assert.IsFalse(view.Rename(other, "wip").Success);
            Assert.IsTrue(TagMarkers.TryGetDefinition(other, out var definition));
            Assert.AreEqual("Other", definition.text);
        }

        [Test]
        public void ManageMarkers_DeletingAUsedMarker_Asks()
        {
            var a = Add("A");
            var wip = AddMarker("WIP", RED);
            var unused = AddMarker("Unused", BLUE);
            TagMarkers.Assign(Main.Id, a, wip);

            string asked = null;
            var view = new TagMarkerListView { Confirm = (_, message) => { asked = message; return false; } };

            Assert.IsFalse(view.Delete(wip), "cancelled");
            StringAssert.Contains("1 tag", asked);
            Assert.IsTrue(TagMarkers.TryGetDefinition(wip, out _));

            asked = null;
            Assert.IsTrue(view.Delete(unused));
            Assert.IsNull(asked, "an unused marker is deleted without asking");

            view.Confirm = (_, _) => true;
            Assert.IsTrue(view.Delete(wip));
            Assert.AreEqual(0, TagMarkers.GetMarkers(Main.Id, a).Count);
        }

        [Test]
        public void Search_AtTermsMatchMarkerNames()
        {
            var a = Add("A");
            Add("B");
            var c = Add("C");
            TagMarkers.Assign(Main.Id, a, AddMarker("WIP", RED));
            TagMarkers.Assign(Main.Id, c, AddMarker("Combat", BLUE));
            var tree = new GameplayTagTreeElement(TagRegistryService.Snapshot.Root, new GameplayTagTreeOptions());

            Assert.AreEqual("wip", tree.Find(Key("A")).GetSearchField(SearchField.Namespace), "normalized");

            tree.SetFilter("@wip");
            CollectionAssert.AreEqual(new[] { "A" }, Shown(tree));

            tree.SetFilter("-@wip");
            CollectionAssert.AreEqual(new[] { "B", "C" }, Shown(tree));

            tree.SetFilter("@comb|@wip");
            CollectionAssert.AreEqual(new[] { "A", "C" }, Shown(tree));

            tree.SetFilter("@wip a");
            CollectionAssert.AreEqual(new[] { "A" }, Shown(tree), "with a name term");
        }

        [Test]
        public void Search_FollowsMarkerChanges()
        {
            Add("A");
            var b = Add("B");
            var wip = AddMarker("WIP", RED);
            var panel = new GameplayTagControlPanel();
            panel.SetFilter("@wip");
            CollectionAssert.IsEmpty(Shown(panel.TagTree));

            TagMarkers.Assign(Main.Id, b, wip);
            panel.OnMarkersChanged();

            CollectionAssert.AreEqual(new[] { "B" }, Shown(panel.TagTree));
        }

        [Test]
        public void ContrastText_DarkOnLight_WhiteOnDark()
        {
            Assert.AreEqual(Color.white, TagMarkerField.ContrastText(new Color(0.1f, 0.1f, 0.3f)));
            Assert.AreNotEqual(Color.white, TagMarkerField.ContrastText(new Color(0.95f, 0.9f, 0.6f)));
        }

        [Test]
        public void MarkerStrip_IsMadeForTagsWithMarkers_InDefinitionOrder()
        {
            var a = Add("A");
            var tree = new GameplayTagTreeElement(TagRegistryService.Snapshot.Root, new GameplayTagTreeOptions());
            var element = tree.Find($"{Main.Id}:{a}");
            Assert.IsNull(element.MarkerStrip, "no markers: no strip");

            var red = AddMarker("Red", RED);
            var blue = AddMarker("Blue", BLUE);
            TagMarkers.Assign(Main.Id, a, blue);
            TagMarkers.Assign(Main.Id, a, red);
            tree.SyncMarkers();

            var strip = element.MarkerStrip;
            Assert.IsNotNull(strip);
            Assert.AreSame(element.Row, strip.parent, "at the end of the tag's own row");
            Assert.AreEqual(2, strip.Items.Count);
            Assert.IsTrue(TagMarkerSet.SameColor(RED, strip.Items[0].style.backgroundColor.value), "the first in definition order");
            Assert.AreEqual("Blue", strip.Items[1].tooltip);

            TagMarkers.Unassign(Main.Id, a, red);
            TagMarkers.Unassign(Main.Id, a, blue);
            tree.SyncMarkers();

            Assert.IsNull(element.MarkerStrip);
            Assert.IsNull(strip.parent, "removed from the row");
        }

        [Test]
        public void MarkerStrip_ModeFollowsTheRoom()
        {
            var a = Add("A");
            TagMarkers.Assign(Main.Id, a, AddMarker("Red", RED));
            TagMarkers.Assign(Main.Id, a, AddMarker("Blue", BLUE));
            var tree = new GameplayTagTreeElement(TagRegistryService.Snapshot.Root, new GameplayTagTreeOptions());
            var strip = tree.Find($"{Main.Id}:{a}").MarkerStrip;

            strip.ApplyLayout(500f);
            Assert.AreEqual(TagMarkerLayoutMode.Labelled, strip.Mode);
            Assert.IsFalse(strip.Items[0].ClassListContains(TagMarkerStrip.USS_CLASS_ITEM_STRIP));

            float strips = 2 * TagMarkerLayout.STRIP_WIDTH + TagMarkerLayout.GAP;
            strip.ApplyLayout(strips + 1f);
            Assert.AreEqual(TagMarkerLayoutMode.Strips, strip.Mode);
            Assert.IsTrue(strip.Items[0].ClassListContains(TagMarkerStrip.USS_CLASS_ITEM_STRIP));

            strip.ApplyLayout(strips - 1f);
            Assert.AreEqual(TagMarkerLayoutMode.Scroll, strip.Mode);

            strip.ApplyLayout(1f);
            Assert.AreEqual(TagMarkerLayoutMode.Hidden, strip.Mode);
            Assert.AreEqual(DisplayStyle.None, strip.style.display.value);
        }

        [Test]
        public void MarkerStrip_OnlyStripsExpand()
        {
            var a = Add("A");
            TagMarkers.Assign(Main.Id, a, AddMarker("Red", RED));
            var tree = new GameplayTagTreeElement(TagRegistryService.Snapshot.Root, new GameplayTagTreeOptions());
            var strip = tree.Find($"{Main.Id}:{a}").MarkerStrip;

            strip.ApplyLayout(500f);
            strip.Expand(0);
            Assert.AreEqual(-1, strip.ExpandedIndex, "a chip already shows its name");

            strip.ApplyLayout(TagMarkerLayout.STRIP_WIDTH + 1f);
            strip.Expand(0);
            Assert.AreEqual(0, strip.ExpandedIndex);

            strip.ApplyLayout(500f);
            Assert.AreEqual(-1, strip.ExpandedIndex, "room for chips collapses it");
        }

        [Test]
        public void Layout_Fit()
        {
            var widths = new[] { 30f, 40f };
            float labelled = 30f + TagMarkerLayout.GAP + 40f;
            float strips = 2 * TagMarkerLayout.STRIP_WIDTH + TagMarkerLayout.GAP;

            Assert.AreEqual(TagMarkerLayoutMode.Labelled, TagMarkerLayout.Fit(widths, labelled, out float width, out float content));
            Assert.AreEqual(labelled, width);
            Assert.AreEqual(labelled, content);

            Assert.AreEqual(TagMarkerLayoutMode.Strips, TagMarkerLayout.Fit(widths, labelled - 1f, out width, out content));
            Assert.AreEqual(strips, width);

            Assert.AreEqual(TagMarkerLayoutMode.Scroll, TagMarkerLayout.Fit(widths, strips - 1f, out width, out content));
            Assert.AreEqual(strips - 1f, width, "scrolls in the room there is");
            Assert.AreEqual(strips, content);

            Assert.AreEqual(TagMarkerLayoutMode.Hidden, TagMarkerLayout.Fit(widths, TagMarkerLayout.STRIP_WIDTH - 1f, out _, out _));
            Assert.AreEqual(TagMarkerLayoutMode.Hidden, TagMarkerLayout.Fit(new float[0], 500f, out _, out _));
            Assert.AreEqual(TagMarkerLayoutMode.Hidden, TagMarkerLayout.Fit(widths, float.NaN, out _, out _));
        }

        [Test]
        public void Layout_ExpandedMarkerStaysInTheRow_AndScrollStaysInTheContent()
        {
            Assert.AreEqual(40f, TagMarkerLayout.ExpandedLeft(50f, 20f, 0f, 100f), "centred");
            Assert.AreEqual(70f, TagMarkerLayout.ExpandedLeft(95f, 30f, 0f, 100f), "moved left at the right edge");
            Assert.AreEqual(0f, TagMarkerLayout.ExpandedLeft(5f, 30f, 0f, 100f), "moved right at the left edge");
            Assert.AreEqual(0f, TagMarkerLayout.ExpandedLeft(50f, 200f, 0f, 100f), "wider than the row: starts at its left edge");

            Assert.AreEqual(0f, TagMarkerLayout.ClampOffset(-5f, 100f, 40f));
            Assert.AreEqual(60f, TagMarkerLayout.ClampOffset(80f, 100f, 40f));
            Assert.AreEqual(0f, TagMarkerLayout.ClampOffset(10f, 30f, 40f), "nothing to scroll");
        }
        #endregion



        private static string Key(string _Path) => $"{Main.Id}:{IdOf(_Path)}";

        /// <summary>
        /// The main registry's tags the filter shows, by name.
        /// </summary>
        private static IEnumerable<string> Shown(GameplayTagTreeElement _Tree)
        {
            var header = _Tree.TagElements.First(e => e.TagNode.RegistryId.Equals(Main.Id));
            return header.ChildTagElements
                .Where(e => e.style.display.value != DisplayStyle.None)
                .Select(e => e.TagNode.ShortTag)
                .ToList();
        }

        private static RegistryGuid AddMarker(string _Text, Color _Color)
        {
            var result = TagMarkers.AddDefinition(_Text, _Color);
            Assert.IsTrue(result.Success, result.Error);
            return result.Id;
        }
    }
}
