using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

using LMK.Runtime.Registry;

using Object = UnityEngine.Object;

namespace LMK.Editor.Registry.Tests
{
    public sealed class StagingTests : KindTestBase
    {

        #region Helpers
        private static RegistryResult Add<TAsset>(TreeKindBase<TAsset> _Kind, string _Segment) where TAsset : RegistryAsset<TreeRow>
        {
            return RegistryEdit.Apply(_Kind, "Add Test Row", data =>
            {
                var id = RegistryGuid.NewGuid();
                data.rows.Add(new RegistryRow<TreeRow>(id, new TreeRow(RegistryGuid.Zero, _Segment)));
                return RegistryResult.Ok(id);
            });
        }

        private static bool Has<TAsset>(TreeKindBase<TAsset> _Kind, string _Name) where TAsset : RegistryAsset<TreeRow>
            => _Kind.Snapshot.Main.TryFind(_Name, out _);

        /// <summary>
        /// Change the main registry on disk, as a pull or a revert would, and import it.
        /// </summary>
        private static void WriteExternally<TAsset>(TreeKindBase<TAsset> _Kind, string _Segment) where TAsset : RegistryAsset<TreeRow>
        {
            var data = RegistryFile.Parse(FileText(_Kind), _Kind).Data;
            data.rows.Add(new RegistryRow<TreeRow>(RegistryGuid.NewGuid(), new TreeRow(RegistryGuid.Zero, _Segment)));
            File.WriteAllText(_Kind.MainRegistryPath, RegistryFile.Write(data, _Kind));
            AssetDatabase.ImportAsset(_Kind.MainRegistryPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }
        #endregion



        #region Staging
        [Test]
        public void CreateMainRegistry_WritesAnEmptyMainRegistry()
        {
            Assert.IsTrue(File.Exists(a.MainRegistryPath));
            Assert.IsNotNull(a.Snapshot.Main);
            Assert.AreEqual(RegistryOrigin.Main, a.Snapshot.Main.Origin);
            Assert.AreEqual(0, a.Snapshot.Main.Count);
            Assert.IsFalse(a.CreateMainRegistry().Success, "a second one is refused");
        }

        [Test]
        public void Apply_StagesWithoutWriting_AndEveryViewShowsIt()
        {
            string saved = FileText(a);
            var result = Add(a, "Fire");

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(saved, FileText(a), "nothing is written before Save");
            Assert.IsTrue(Has(a, "Fire"));
            Assert.IsFalse(a.SavedSnapshot.Main.TryFind("Fire", out _), "builds see only saved content");
            Assert.IsTrue(a.HasPendingChanges);
            Assert.IsTrue(RegistrySession.HasPendingChanges);
            Assert.IsFalse(b.HasPendingChanges);

            Assert.IsTrue(TestDatabase.ResolveActive(a.Snapshot.Main.Id, result.Id).IsLive, "the editor database follows staged edits");
        }

        [Test]
        public void Save_WritesTheStagedContent_AndClearsPending()
        {
            Add(a, "Fire");

            var result = RegistrySession.Save();

            Assert.IsTrue(result.Success, result.Error);
            StringAssert.Contains("| Fire", FileText(a));
            Assert.IsFalse(RegistrySession.HasPendingChanges);
            Assert.IsTrue(a.SavedSnapshot.Main.TryFind("Fire", out _));
            Assert.IsTrue(RegistryFile.Parse(FileText(a), a).IsValid);
            Assert.AreEqual(1, a.savedCount, "generated files are written once per save of the kind, not at creation");
            Assert.AreEqual(0, b.savedCount, "a kind without staged edits is not saved");
        }

        [Test]
        public void Discard_DropsStagedEdits()
        {
            string saved = FileText(a);
            Add(a, "Fire");

            RegistrySession.Discard();

            Assert.IsFalse(Has(a, "Fire"));
            Assert.IsFalse(RegistrySession.HasPendingChanges);
            Assert.AreEqual(saved, FileText(a));
        }

        [Test]
        public void InvalidResult_StagesNothing()
        {
            var result = RegistryEdit.Apply(a, "Add Orphan", data =>
            {
                data.rows.Add(new RegistryRow<TreeRow>(RegistryGuid.NewGuid(), new TreeRow(RegistryGuid.NewGuid(), "Orphan")));
                return RegistryResult.Ok();
            });

            Assert.IsFalse(result.Success);
            StringAssert.Contains("does not exist", result.Error);
            Assert.IsFalse(a.HasPendingChanges);
        }

        [Test]
        public void RefusedChange_StagesNothing()
        {
            var result = RegistryEdit.Apply(a, "Refuse", data =>
            {
                data.rows.Clear();
                return RegistryResult.Fail("No.");
            });

            Assert.AreEqual("No.", result.Error);
            Assert.IsFalse(a.HasPendingChanges);
        }

        [Test]
        public void Apply_WithoutMainRegistry_IsRefused()
        {
            AssetDatabase.DeleteAsset(a.MainRegistryPath);

            var result = Add(a, "Fire");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("no main registry", result.Error);
        }
        #endregion



        #region Undo
        [Test]
        public void UndoAndRedo_RestoreStagedState_AndWriteNothing()
        {
            string saved = FileText(a);
            Add(a, "Fire");

            Undo.PerformUndo();
            Assert.IsFalse(Has(a, "Fire"));
            Assert.IsFalse(a.HasPendingChanges, "back to the saved content");

            Undo.PerformRedo();
            Assert.IsTrue(Has(a, "Fire"));
            Assert.AreEqual(saved, FileText(a), "undo and redo write nothing");
        }

        [Test]
        public void UndoAcrossKinds_RestoresInReverseOrder_AndWritesNothing()
        {
            string savedA = FileText(a), savedB = FileText(b);
            Add(a, "Fire");
            Add(b, "Ice");

            Undo.PerformUndo();
            Assert.IsTrue(Has(a, "Fire"), "the first undo reverts the last edit only");
            Assert.IsFalse(Has(b, "Ice"));

            Undo.PerformUndo();
            Assert.IsFalse(Has(a, "Fire"));
            Assert.IsFalse(RegistrySession.HasPendingChanges);
            Assert.AreEqual(savedA, FileText(a));
            Assert.AreEqual(savedB, FileText(b));
        }
        #endregion



        #region Session across kinds
        [Test]
        public void SaveAcrossKinds_AsksTheLockOnce_ForEveryFile()
        {
            var provider = new RecordingLockProvider();
            Add(a, "Fire");
            Add(b, "Ice");
            RegistryLockProvider.Current = provider;

            var result = RegistrySession.Save();

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(1, provider.requests.Count);
            CollectionAssert.AreEquivalent(new[] { a.MainRegistryPath, b.MainRegistryPath }, provider.requests[0]);
            StringAssert.Contains("| Fire", FileText(a));
            StringAssert.Contains("| Ice", FileText(b));
            Assert.IsFalse(RegistrySession.HasPendingChanges);
        }

        [Test]
        public void DiscardAcrossKinds_DropsBoth()
        {
            Add(a, "Fire");
            Add(b, "Ice");

            RegistrySession.Discard();

            Assert.IsFalse(Has(a, "Fire"));
            Assert.IsFalse(Has(b, "Ice"));
            Assert.IsFalse(RegistrySession.HasPendingChanges);
        }

        [Test]
        public void SaveIsAllOrNothing_WhenOneKindChangedOnDisk()
        {
            string savedB = FileText(b);
            Add(a, "Fire");
            Add(b, "Ice");
            WriteExternally(a, "Pulled");

            var result = RegistrySession.Save();

            Assert.IsFalse(result.Success);
            StringAssert.Contains("changed on disk", result.Error);
            Assert.AreEqual(savedB, FileText(b), "the other kind is not written either");
            Assert.IsTrue(RegistrySession.DiskChangedWhilePending);
            Assert.IsTrue(Has(a, "Fire"), "staged edits are kept");
        }
        #endregion



        #region Lock
        [Test]
        public void RefusingLock_RefusesEveryOperation_WithItsMessage()
        {
            Add(a, "Fire");
            RegistryLockProvider.Current = new RecordingLockProvider { refuse = true };

            var apply = Add(a, "Ice");
            var save = RegistrySession.Save();

            AssetDatabase.DeleteAsset(b.MainRegistryPath);
            var create = b.CreateMainRegistry();

            foreach (var result in new[] { apply, save, create })
            {
                Assert.IsFalse(result.Success);
                StringAssert.Contains("Held by someone else.", result.Error);
                StringAssert.Contains("Recording", result.Error);
            }

            Assert.IsTrue(Has(a, "Fire"));
            Assert.IsFalse(Has(a, "Ice"));
            Assert.IsFalse(File.Exists(b.MainRegistryPath));
        }

        [Test]
        public void StagedOperation_AsksTheLockForTheKindsFiles()
        {
            var provider = new RecordingLockProvider();
            RegistryLockProvider.Current = provider;

            Add(a, "Fire");

            Assert.AreEqual(1, provider.requests.Count);
            CollectionAssert.AreEqual(a.LockedPaths, provider.requests[0]);
        }
        #endregion



        #region Disk
        [Test]
        public void ExternalChange_WithoutStagedEdits_IsFollowed()
        {
            WriteExternally(a, "Pulled");

            Assert.IsTrue(Has(a, "Pulled"));
            Assert.IsFalse(a.HasPendingChanges);
            Assert.IsTrue(Add(a, "Fire").Success);
            Assert.IsTrue(Has(a, "Pulled") && Has(a, "Fire"));
        }

        [Test]
        public void ExternalChange_WhilePending_KeepsEditsAndRefusesSave()
        {
            Add(a, "Fire");
            WriteExternally(a, "Pulled");

            Assert.IsTrue(a.DiskChangedWhilePending);
            Assert.IsTrue(Has(a, "Fire"));
            Assert.IsFalse(RegistrySession.Save().Success);

            RegistrySession.Discard();
            Assert.IsTrue(Has(a, "Pulled"));
            Assert.IsFalse(Has(a, "Fire"));
        }

        [Test]
        public void RejectedFile_KeepsTheLastVersionThatLoaded()
        {
            Add(a, "Fire");
            RegistrySession.Save();

            LogAssert.Expect(LogType.Error, new Regex("conflict marker"));
            LogAssert.Expect(LogType.Warning, new Regex("last version that loaded"));
            File.WriteAllText(a.MainRegistryPath, "<<<<<<< HEAD\n" + FileText(a));
            AssetDatabase.ImportAsset(a.MainRegistryPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var main = a.Snapshot.Main;
            Assert.IsTrue(main.UsingLastGood);
            Assert.IsTrue(main.TryFind("Fire", out _));
            Assert.IsTrue(main.Errors.Any(e => e.Contains("conflict marker")));
        }

        [Test]
        public void GeneratedRegistry_LoadsOnlyWhileItsPackageIsInstalled()
        {
            var data = new RegistryData<TreeRow>
            {
                registryId = RegistryGuid.NewGuid(),
                name = "com.vendor.combat",
                origin = RegistryOrigin.Generated,
                package = "com.vendor.combat",
                version = "1.0.0",
            };
            string path = $"{FOLDER}/Packages/com.vendor.combat.{a.FileExtension}";
            RegistryAssets.EnsureFolder($"{FOLDER}/Packages");
            RegistryFile.WriteAtomic(path, RegistryFile.Write(data, a));

            RegistryPackages.InstalledPackagesOverride = () => new Dictionary<string, string>();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            Assert.IsNull(a.Snapshot.Find(data.registryId));
            CollectionAssert.Contains(a.InactiveRegistryIds, data.registryId);

            RegistryPackages.InstalledPackagesOverride = () => new Dictionary<string, string> { ["com.vendor.combat"] = "1.0.0" };
            a.Reload();

            Assert.IsNotNull(a.Snapshot.Find(data.registryId));
            Assert.IsTrue(a.Snapshot.Find(data.registryId).IsReadOnly);
        }
        #endregion



        #region Markers
        [Test]
        public void Markers_AreStaged_SavedWithoutLock_AndDiscarded()
        {
            var registry = a.Snapshot.Main.Id;
            var row = Add(a, "Fire").Id;
            RegistrySession.Save();

            a.SetMarker(registry, row, Color.red);
            Assert.IsTrue(a.TryGetMarker(registry, row, out var color));
            Assert.AreEqual((Color)(Color32)Color.red, color);
            Assert.IsTrue(a.HasPendingChanges);
            Assert.IsFalse(File.Exists(a.MarkersPath), "staged, not written");

            var provider = new RecordingLockProvider { refuse = true };
            RegistryLockProvider.Current = provider;
            string savedText = FileText(a);

            Assert.IsTrue(RegistrySession.Save().Success, "markers are per user: no lock");
            Assert.IsTrue(File.Exists(a.MarkersPath));
            Assert.AreEqual(savedText, FileText(a), "a marker-only save writes no registry");
            Assert.IsEmpty(provider.requests);

            a.SetMarker(registry, row, Color.clear);
            Assert.IsFalse(a.TryGetMarker(registry, row, out _));
            RegistrySession.Discard();
            Assert.IsTrue(a.TryGetMarker(registry, row, out _), "discard restores the saved marker");
        }
        #endregion
    }
}
