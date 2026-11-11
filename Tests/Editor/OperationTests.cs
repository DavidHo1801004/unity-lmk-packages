using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// A lock that is always held by someone else.
    /// </summary>
    internal sealed class RefusingLockProvider : TagLockProvider
    {
        public override string Name => "Test";

        public override bool IsHeld(IReadOnlyList<string> _Paths) => false;

        public override bool TryAcquire(IReadOnlyList<string> _Paths, out string _Error)
        {
            _Error = "Held by a teammate.";
            return false;
        }
    }



    /// <summary>
    /// Runs every test against a main registry in a temporary folder, never the project's own. Generated code and
    /// markers go to <c>Temp/</c>.
    /// </summary>
    public abstract class OperationTestBase
    {
        protected const string FOLDER = "Assets/__GameplayTagTests";
        protected const string MAIN = FOLDER + "/Main.tagregistry";

        // Outside Assets/, so writing generated code never triggers a script compile during the run.
        protected const string CODE_FOLDER = "Temp/__GameplayTagTests";
        protected const string CODE = CODE_FOLDER + "/GameplayTags.g.cs";
        protected const string MARKERS = CODE_FOLDER + "/Markers.json";

        private string previousMainPath;
        private string previousCodePath;
        private string previousMarkersPath;

        [SetUp]
        public void CreateMainRegistry()
        {
            previousMainPath = TagRegistryService.MainRegistryPath;
            TagRegistryService.MainRegistryPath = MAIN;
            previousCodePath = TagCodeGen.OutputPath;
            TagCodeGen.OutputPath = CODE;
            previousMarkersPath = TagMarkerStore.FilePath;
            TagMarkerStore.FilePath = MARKERS;
            TagLockProvider.Current = null;

            if (!AssetDatabase.IsValidFolder(FOLDER))
                AssetDatabase.CreateFolder("Assets", "__GameplayTagTests");

            Undo.ClearAll();

            var result = TagOperations.CreateMainRegistry();
            Assert.IsTrue(result.Success, result.Error);
        }

        [TearDown]
        public void DeleteMainRegistry()
        {
            TagLockProvider.Current = null;
            TagOperations.Discard();
            TagRegistryService.MainRegistryPath = previousMainPath;
            TagCodeGen.CancelPending();
            TagCodeGen.OutputPath = previousCodePath;
            TagMarkerStore.FilePath = previousMarkersPath;

            if (Directory.Exists(CODE_FOLDER))
                Directory.Delete(CODE_FOLDER, true);

            AssetDatabase.DeleteAsset(FOLDER);
            TagRegistryService.Reload();
            Undo.ClearAll();
        }

        private protected static TagRegistrySnapshot.Registry Main => TagRegistryService.Snapshot.Main;

        protected static TagGuid Add(string _Path)
        {
            var result = TagOperations.Add(_Path);
            Assert.IsTrue(result.Success, result.Error);
            return result.Id;
        }

        /// <summary>
        /// Save the staged edits, which must succeed.
        /// </summary>
        protected static void Save()
        {
            var result = TagOperations.Save();
            Assert.IsTrue(result.Success, result.Error);
            Assert.IsFalse(TagOperations.HasPendingChanges);
        }

        protected static TagGuid IdOf(string _Path)
        {
            Assert.IsTrue(Main.TryFind(_Path, out var id), $"\"{_Path}\" not found");
            return id;
        }

        /// <summary>
        /// A reference as the drawer writes it.
        /// </summary>
        protected static GameplayTag Ref(string _Path)
            => GameplayTag.FromId(Main.Id, IdOf(_Path), _Path);

        protected static string FileText => File.ReadAllText(MAIN);

        protected static void AssertFileValid()
        {
            var parsed = TagRegistryFile.Parse(FileText);
            Assert.IsTrue(parsed.IsValid, string.Join("\n", parsed.Errors));
            Assert.IsFalse(parsed.ChecksumMismatch, "the manager always writes a valid checksum");
        }
    }



    public sealed class OperationTests : OperationTestBase
    {
        [Test]
        public void Rename_ReferencesFollow_AndNoAssetIsModified()
        {
            var reference = Ref(PathOf(Add("A")));

            var fixture = ScriptableObject.CreateInstance<TagSerializationFixture>();
            fixture.single = reference;
            AssetDatabase.CreateAsset(fixture, FOLDER + "/Holder.asset");
            AssetDatabase.SaveAssets();
            string before = File.ReadAllText(FOLDER + "/Holder.asset");

            Assert.IsTrue(TagOperations.Rename(reference.Id, "B").Success);
            Save();
            AssetDatabase.SaveAssets();

            Assert.AreEqual("B", reference.Path);
            Assert.AreEqual(before, File.ReadAllText(FOLDER + "/Holder.asset"));
            AssertFileValid();
        }

        [Test]
        public void RenameParent_DescendantsFollow()
        {
            Add("A.B.C");
            var c = Ref("A.B.C");

            Assert.IsTrue(TagOperations.Rename(IdOf("A"), "X").Success);

            Assert.AreEqual("X.B.C", c.Path);
            Assert.AreEqual(c.Id, IdOf("X.B.C"));
        }

        [Test]
        public void RenameToSiblingName_IsRejected()
        {
            Add("A.B");
            Add("A.C");
            string before = FileText;

            var result = TagOperations.Rename(IdOf("A.B"), "C");

            Assert.IsFalse(result.Success);
            Assert.AreEqual(before, FileText);
        }

        [Test]
        public void RenameToInvalidSegment_IsRejected()
        {
            Add("A");
            Assert.IsFalse(TagOperations.Rename(IdOf("A"), "B.C").Success);
            Assert.IsFalse(TagOperations.Rename(IdOf("A"), "").Success);
        }

        [Test]
        public void Delete_RemovesRow_ReferencesAreDeleted()
        {
            var a = Ref(PathOf(Add("A")));

            Assert.IsTrue(TagOperations.Delete(a.Id, true).Success);

            Assert.IsFalse(Main.Contains(a.Id));
            Assert.AreEqual(TagStatus.Deleted, a.Resolve().Status);
            Assert.AreEqual("A", a.Path);
        }

        [Test]
        public void DeleteThenAdd_GetsNewId()
        {
            var a = Ref(PathOf(Add("A")));
            TagOperations.Delete(a.Id, true);

            var newId = Add("A");

            Assert.AreNotEqual(a.Id, newId);
            Assert.AreEqual(TagStatus.Deleted, a.Resolve().Status);
        }

        [Test]
        public void RenameThenAdd_OldNameIsANewTag()
        {
            var a = Ref(PathOf(Add("A")));
            TagOperations.Rename(a.Id, "B");

            var newA = Add("A");

            Assert.AreNotEqual(a.Id, newA);
            Assert.AreEqual("B", a.Path);
        }

        [Test]
        public void DeleteTagWithChildren_DeletesSubtree()
        {
            Add("A.B.C");
            Add("A.B.D");
            var c = Ref("A.B.C");

            Assert.IsTrue(TagOperations.Delete(IdOf("A.B"), false).Success);

            Assert.IsFalse(Main.TryFind("A.B", out _));
            Assert.IsFalse(Main.TryFind("A.B.D", out _));
            Assert.AreEqual(TagStatus.Deleted, c.Resolve().Status);
            Assert.IsTrue(Main.TryFind("A", out _));
        }

        [Test]
        public void DeleteLastChild_RemoveEmptyParentsOn()
        {
            Add("A.B.C");

            CollectionAssert.AreEqual(new[] { "A.B", "A" }, TagOperations.GetParentsLeftEmpty(IdOf("A.B.C")));
            Assert.IsTrue(TagOperations.Delete(IdOf("A.B.C"), true).Success);

            Assert.AreEqual(0, Main.Count);
        }

        [Test]
        public void DeleteLastChild_RemoveEmptyParentsOff()
        {
            Add("A.B.C");
            var b = Ref("A.B");

            Assert.IsTrue(TagOperations.Delete(IdOf("A.B.C"), false).Success);

            Assert.AreEqual(b.Id, IdOf("A.B"), "same GUID");
            Assert.IsTrue(Main.TryGetRow(b.Id, out var row));
            Assert.IsFalse(row.isImplicit, "the parent left empty becomes explicit");
        }

        [Test]
        public void AddingImplicitTag_KeepsItsId()
        {
            Add("A.B");
            var a = Ref("A");
            Assert.IsTrue(Main.TryGetRow(a.Id, out var before) && before.isImplicit);

            Assert.IsTrue(TagOperations.Add("A", "Now explicit").Success);

            Assert.IsTrue(Main.TryGetRow(a.Id, out var after));
            Assert.IsFalse(after.isImplicit);
            Assert.AreEqual("Now explicit", after.description);
            Assert.AreEqual(TagStatus.Live, a.Resolve().Status);
        }

        [Test]
        public void AddingExistingExplicitTag_IsRejected()
        {
            Add("A");
            Assert.IsFalse(TagOperations.Add("A").Success);
        }

        [Test]
        public void WithoutLock_EveryOperationIsRefused()
        {
            var a = Add("A");
            string before = FileText;
            TagLockProvider.Current = new RefusingLockProvider();

            var results = new[]
            {
                TagOperations.Add("B"),
                TagOperations.Rename(a, "C"),
                TagOperations.Delete(a, true),
                TagOperations.EditDescription(a, "text"),
            };

            Assert.IsTrue(results.All(e => !e.Success && e.Error.Contains("Held by a teammate")));
            Assert.AreEqual(before, FileText);
        }

        [Test]
        public void OperationsOnOtherRegistries_AreRefused()
        {
            var vendor = new TagRegistryData
            {
                registryId = TagGuid.Parse("b0000000000000000000000000000002"),
                name = "Vendor",
                kind = TagRegistryKind.Package,
                rows = new List<TagRegistryRow> { new(TestIds.Id(1), TagGuid.Zero, "Vendor", false) },
            };
            RegistryServiceTests.WriteAndImport(FOLDER + "/Vendor.tagregistry", TagRegistryFile.Write(vendor));

            Assert.IsFalse(TagOperations.Rename(TestIds.Id(1), "Other").Success);
            Assert.IsFalse(TagOperations.Delete(TestIds.Id(1), true).Success);
            Assert.IsFalse(TagOperations.EditDescription(TestIds.Id(1), "x").Success);
            Assert.AreEqual("Vendor", TagRegistryService.Snapshot.Find(vendor.registryId).GetPath(TestIds.Id(1)));
        }

        [Test]
        public void Edits_ArePendingUntilSave_SaveWrites()
        {
            string empty = FileText;
            Assert.IsFalse(TagOperations.HasPendingChanges);

            Add("Ability.Fire");
            TagOperations.EditDescription(IdOf("Ability.Fire"), "Burns things");

            Assert.IsTrue(TagOperations.HasPendingChanges);
            Assert.AreEqual(empty, FileText, "staged edits write nothing");
            Assert.AreEqual("Burns things", Main.TryGetRow(IdOf("Ability.Fire"), out var row) ? row.description : null, "every view shows staged edits");

            Save();

            StringAssert.Contains("Fire", FileText);
            StringAssert.Contains("Burns things", FileText);
            AssertFileValid();
        }

        [Test]
        public void Discard_RestoresTheSavedContent()
        {
            var a = Ref(PathOf(Add("A")));
            Save();
            string saved = FileText;

            TagOperations.Rename(a.Id, "B");
            Add("C");
            TagOperations.Discard();

            Assert.IsFalse(TagOperations.HasPendingChanges);
            Assert.AreEqual("A", a.Path);
            Assert.IsFalse(Main.TryFind("C", out _));
            Assert.AreEqual(saved, FileText);
        }

        [Test]
        public void ChangingBack_LeavesNothingPending()
        {
            var a = Add("A");
            Save();

            TagOperations.Rename(a, "B");
            Assert.IsTrue(TagOperations.HasPendingChanges);

            TagOperations.Rename(a, "A");
            Assert.IsFalse(TagOperations.HasPendingChanges, "pending means different from the saved content");
        }

        [Test]
        public void Builds_UseTheSavedContent()
        {
            var a = Add("A");
            Save();
            TagOperations.Rename(a, "Staged");

            Assert.AreEqual("Staged", TagRegistryService.Snapshot.Main.GetPath(a));
            Assert.AreEqual("A", TagRegistryService.SavedSnapshot.Main.GetPath(a));
        }

        [Test]
        public void InvalidMainRegistry_BlocksOperations()
        {
            Add("A");
            File.WriteAllText(MAIN, FileText + "<<<<<<< HEAD\n");

            var result = TagOperations.Add("B");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("conflict marker", result.Error);
        }

        private static string PathOf(TagGuid _Id) => Main.GetPath(_Id);
    }



    public sealed class UndoTests : OperationTestBase
    {
        [Test]
        public void UndoRedo_Rename()
        {
            var a = Ref(PathOf(Add("A")));
            Save();
            string saved = FileText;
            TagOperations.Rename(a.Id, "B");

            Undo.PerformUndo();
            Assert.AreEqual("A", a.Path);
            Assert.IsFalse(TagOperations.HasPendingChanges, "back to the saved content");

            Undo.PerformRedo();
            Assert.AreEqual("B", a.Path);
            Assert.AreEqual(saved, FileText, "undo and redo write nothing");

            Save();
            StringAssert.Contains("| B", FileText);
            AssertFileValid();
        }

        [Test]
        public void UndoDelete_RestoresOriginalId()
        {
            var a = Ref(PathOf(Add("A")));
            TagOperations.Delete(a.Id, true);
            Assert.AreEqual(TagStatus.Deleted, a.Resolve().Status);

            Undo.PerformUndo();

            Assert.AreEqual(TagStatus.Live, a.Resolve().Status);
            Assert.AreEqual(a.Id, IdOf("A"));
        }

        [Test]
        public void UndoAdd_ThenRedo()
        {
            var a = Ref(PathOf(Add("A")));

            Undo.PerformUndo();
            Assert.AreEqual(TagStatus.Deleted, a.Resolve().Status);

            Undo.PerformRedo();
            Assert.AreEqual(TagStatus.Live, a.Resolve().Status);
        }

        [Test]
        public void SaveWithoutLock_IsRefused()
        {
            string before = FileText;
            Add("A");
            TagLockProvider.Current = new RefusingLockProvider();

            var result = TagOperations.Save();

            Assert.IsFalse(result.Success);
            StringAssert.Contains("Held by a teammate", result.Error);
            Assert.AreEqual(before, FileText);
            Assert.IsTrue(TagOperations.HasPendingChanges, "the edits stay staged");
        }

        [Test]
        public void SaveAfterExternalChange_IsRefused() // change not yet imported
        {
            Add("A");
            Save();
            Add("B");
            string external = FileText.Replace("| A", "| Z");
            File.WriteAllText(MAIN, external);

            Assert.IsTrue(TagOperations.DiskChangedWhilePending);
            Assert.IsFalse(TagOperations.Save().Success);
            Assert.AreEqual(external, FileText, "staged content never overwrites newer content");

            TagOperations.Discard();
            Assert.IsFalse(TagOperations.HasPendingChanges);
            Assert.IsFalse(Main.TryFind("B", out _));
        }

        [Test]
        public void ImportedExternalChange_WithoutStagedEdits_IsLoaded_UndoHistoryCleared() // change imported (e.g. a pull)
        {
            Add("A");
            Save();
            LogAssert.ignoreFailingMessages = true; // the external edit has no valid checksum
            string external = FileText.Replace("| A", "| Z");
            RegistryServiceTests.WriteAndImport(MAIN, external);

            Undo.PerformUndo();

            Assert.AreEqual(external, FileText);
            Assert.IsTrue(Main.TryFind("Z", out _));
            Assert.IsFalse(TagOperations.HasPendingChanges);
        }

        [Test]
        public void ImportedExternalChange_WithStagedEdits_KeepsThem()
        {
            Add("A");
            Save();
            Add("B");
            LogAssert.ignoreFailingMessages = true; // the external edit has no valid checksum
            string external = FileText.Replace("| A", "| Z");
            RegistryServiceTests.WriteAndImport(MAIN, external);

            Assert.IsTrue(Main.TryFind("B", out _), "staged edits are kept");
            Assert.IsTrue(TagOperations.DiskChangedWhilePending);
            Assert.IsFalse(TagOperations.Save().Success);
            Assert.AreEqual(external, FileText);
        }

        [Test]
        public void UnrelatedUndo_DoesNotTouchTheRegistry()
        {
            Add("A");
            string before = FileText;

            var other = ScriptableObject.CreateInstance<TagSerializationFixture>();
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(other, "Unrelated");
            other.name = "Changed";
            Undo.FlushUndoRecordObjects();

            Undo.PerformUndo();

            Assert.AreEqual(before, FileText);
            Object.DestroyImmediate(other);
        }

        private static string PathOf(TagGuid _Id) => Main.GetPath(_Id);
    }
}
