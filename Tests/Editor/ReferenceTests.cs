using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using UnityEditor;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry.Tests
{
    public sealed class ReferenceTests : KindTestBase
    {
        private const string INDEX = "Temp/RegistryCoreTests/RefIndex";
        private const string PENDING = "Temp/RegistryCoreTests/PendingImports";
        private const string LOGS = "Temp/RegistryCoreTests/Logs";

        private string previousIndex, previousPending, previousLogs;

        [SetUp]
        public void UseTestPaths()
        {
            previousIndex = RegistryReferenceIndex.IndexPath;
            previousPending = RegistryImportMapping.PendingPath;
            previousLogs = RegistryImportMapping.LogFolder;

            RegistryReferenceIndex.IndexPath = INDEX;
            RegistryImportMapping.PendingPath = PENDING;
            RegistryImportMapping.LogFolder = LOGS;
            RegistryReferenceIndex.Clear();
        }

        [TearDown]
        public void RestorePaths()
        {
            RegistryReferenceIndex.Flush();
            RegistryReferenceIndex.IndexPath = previousIndex;
            RegistryImportMapping.PendingPath = previousPending;
            RegistryImportMapping.LogFolder = previousLogs;
        }



        #region Helpers
        /// <summary>
        /// Stage and save a row of the main registry of <paramref name="_Kind"/>.
        /// </summary>
        private static RegistryGuid AddSaved<TAsset>(TreeKindBase<TAsset> _Kind, string _Segment) where TAsset : RegistryAsset<TreeRow>
        {
            var result = RegistryEdit.Apply(_Kind, "Add", data =>
            {
                var id = RegistryGuid.NewGuid();
                data.rows.Add(new RegistryRow<TreeRow>(id, new TreeRow(RegistryGuid.Zero, _Segment)));
                return RegistryResult.Ok(id);
            });

            Assert.IsTrue(RegistrySession.Save().Success);
            return result.Id;
        }

        private static List<string> Find(RegistryKind _Kind, RegistryGuid _Registry, RegistryGuid _Id)
        {
            Assert.IsTrue(RegistryReferenceIndex.TryFindAssets(_Kind, new[] { (_Registry, _Id) }, false, out var assets));
            return assets;
        }
        #endregion



        #region Index
        [Test]
        public void Index_FindsAssetsByKindAndRow()
        {
            var mainA = a.MainRegistryId;
            var fire = AddSaved(a, "Fire");
            string folder = CreateFolder("Index");
            string holder = CreateHolder(folder, "Holder", h => h.single = new TreeRef(mainA, fire, "Fire"));
            var mainB = b.MainRegistryId;
            CreateHolder(folder, "Other", h => h.other = new TreeRefB(mainB, fire, "Fire"));

            CollectionAssert.AreEqual(new[] { holder }, Find(a, mainA, fire), "the same row ID in another kind is another row");
            CollectionAssert.IsEmpty(Find(b, mainA, fire));
            CollectionAssert.IsEmpty(Find(a, mainA, RegistryGuid.NewGuid()));
        }

        [Test]
        public void Index_SavingUpdatesWithoutAScan_AndOutsideChangesAreRescanned()
        {
            var mainA = a.MainRegistryId;
            var fire = AddSaved(a, "Fire");
            string holder = CreateHolder(CreateFolder("Index"), "Holder", _ => { });
            CollectionAssert.IsEmpty(Find(a, mainA, fire));

            var asset = AssetDatabase.LoadAssetAtPath<ReferenceHolder>(holder);
            asset.single = new TreeRef(mainA, fire, "Fire");
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            CollectionAssert.AreEqual(new[] { holder }, Find(a, mainA, fire));
            Assert.AreEqual(0, RegistryReferenceIndex.LastScanCount, "a save updates its entry without a scan");

            // A pull: the file changes without a save from this editor.
            File.WriteAllText(holder, File.ReadAllText(holder).Replace(fire.Lo.ToString(), "12345"));
            AssetDatabase.ImportAsset(holder, ImportAssetOptions.ForceSynchronousImport);

            CollectionAssert.IsEmpty(Find(a, mainA, fire));
            Assert.AreEqual(1, RegistryReferenceIndex.LastScanCount);
        }

        [Test]
        public void Index_IsKeptInLibrary_AndRebuiltWhenKindsChange()
        {
            var mainA = a.MainRegistryId;
            var fire = AddSaved(a, "Fire");
            string holder = CreateHolder(CreateFolder("Index"), "Holder", h => h.single = new TreeRef(mainA, fire, "Fire"));
            Find(a, mainA, fire);
            RegistryReferenceIndex.Flush();

            Assert.IsTrue(File.Exists(INDEX));
            // The kinds signature lists every registered kind, in order: domain packages in the project add theirs.
            string header = File.ReadAllText(INDEX).Split('\n')[0];
            StringAssert.StartsWith("LMK.Registry.RefIndex 1\t", header);
            CollectionAssert.IsSubsetOf(new[] { "tree", "treeb" }, header.Split('\t')[1].Split(','));

            Find(a, mainA, fire);
            Assert.AreEqual(0, RegistryReferenceIndex.LastScanCount, "nothing changed");

            RegistryKinds.Unregister(b);
            CollectionAssert.AreEqual(new[] { holder }, Find(a, mainA, fire));
            Assert.Greater(RegistryReferenceIndex.LastScanCount, 0, "a change of kinds rescans everything");
        }
        #endregion



        #region Tools
        [Test]
        public void Replace_RewritesOnlyThatKindsReferences()
        {
            var mainA = a.MainRegistryId;
            var fire = AddSaved(a, "Fire");
            var ice = AddSaved(a, "Ice");
            string holder = CreateHolder(CreateFolder("Tools"), "Holder", h =>
            {
                h.single = new TreeRef(mainA, fire, "Fire");
                h.list.Add(new TreeRef(mainA, fire, "Fire"));
                h.other = new TreeRefB(mainA, fire, "Fire");
            });

            RegistryReferenceTools.PlanReplace(a, (mainA, fire), (mainA, ice), false, out var changes);
            Assert.AreEqual(2, changes.Count);

            var result = RegistryReferenceTools.Apply(changes, "Replace");

            Assert.AreEqual(2, result.Rewritten);
            var asset = AssetDatabase.LoadAssetAtPath<ReferenceHolder>(holder);
            Assert.AreEqual(ice, asset.single.Id);
            Assert.AreEqual("Ice", asset.single.LastKnownName);
            Assert.AreEqual(ice, asset.list[0].Id);
            Assert.AreEqual(fire, asset.other.Id, "the other kind's reference is untouched");
        }

        [Test]
        public void Relink_PointsMissingReferencesToTheRowWithTheirName()
        {
            var mainA = a.MainRegistryId;
            var fire = AddSaved(a, "Fire");
            var deleted = RegistryGuid.NewGuid();
            string holder = CreateHolder(CreateFolder("Tools"), "Holder", h =>
            {
                h.single = new TreeRef(mainA, deleted, "Fire");
                h.list.Add(new TreeRef(mainA, RegistryGuid.NewGuid(), "Nothing"));
            });

            RegistryReferenceTools.PlanRelink(a, false, out var changes);

            Assert.AreEqual(1, changes.Count, "a missing reference without a matching name is left");
            RegistryReferenceTools.Apply(changes, "Relink");
            Assert.AreEqual(fire, AssetDatabase.LoadAssetAtPath<ReferenceHolder>(holder).single.Id);
        }

        [Test]
        public void Apply_SkipsReferencesChangedSinceThePreview()
        {
            var mainA = a.MainRegistryId;
            var fire = AddSaved(a, "Fire");
            var ice = AddSaved(a, "Ice");
            string holder = CreateHolder(CreateFolder("Tools"), "Holder", h => h.single = new TreeRef(mainA, fire, "Fire"));

            RegistryReferenceTools.PlanReplace(a, (mainA, fire), (mainA, ice), false, out var changes);

            var asset = AssetDatabase.LoadAssetAtPath<ReferenceHolder>(holder);
            asset.single = new TreeRef(mainA, RegistryGuid.NewGuid(), "Wind");
            EditorUtility.SetDirty(asset);

            var result = RegistryReferenceTools.Apply(changes, "Replace");

            Assert.AreEqual(0, result.Rewritten);
            Assert.AreEqual(1, result.Skipped);
        }
        #endregion



        #region Import Mapping
        [Test]
        public void Map_RewritesUnknownRegistriesByName_ForEveryKind()
        {
            var mainA = a.MainRegistryId;
            var fire = AddSaved(a, "Fire");
            var otherProject = RegistryGuid.NewGuid();
            var incomingIce = RegistryGuid.NewGuid();
            string holder = CreateHolder(CreateFolder("Incoming"), "Holder", h =>
            {
                h.single = new TreeRef(otherProject, RegistryGuid.NewGuid(), "Fire");
                h.list.Add(new TreeRef(otherProject, incomingIce, "Ice"));
                h.list.Add(new TreeRef(mainA, fire, "Fire"));
                h.other = new TreeRefB(otherProject, RegistryGuid.NewGuid(), "Stun");
            });

            var result = RegistryImportMapping.Map(new[] { holder }, "test", false);

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsFalse(result.Postponed, result.Message);
            Assert.AreEqual(3, result.Rewritten.Count, result.Message);
            Assert.AreEqual(2, result.AddedRows.Count, "Ice and Stun are new");

            var asset = AssetDatabase.LoadAssetAtPath<ReferenceHolder>(holder);
            Assert.AreEqual(mainA, asset.single.RegistryId);
            Assert.AreEqual(fire, asset.single.Id, "an existing name maps to that row");
            Assert.AreEqual(incomingIce, asset.list[0].Id, "a new name keeps the incoming ID");
            Assert.AreEqual(b.MainRegistryId, asset.other.RegistryId);
            Assert.IsTrue(b.TryFindInMain("Stun", out _));

            Assert.IsFalse(RegistrySession.HasPendingChanges, "the added rows are saved");
            Assert.IsTrue(File.Exists(result.LogPath));
        }

        [Test]
        public void Map_WithoutTheLock_IsPostponed_ThenMapsPending()
        {
            string holder = CreateHolder(CreateFolder("Incoming"), "Holder", h => h.single = new TreeRef(RegistryGuid.NewGuid(), RegistryGuid.NewGuid(), "Fire"));
            RegistryLockProvider.Current = new RecordingLockProvider { refuse = true };

            var result = RegistryImportMapping.Map(new[] { holder }, "test", false);

            Assert.IsTrue(result.Postponed);
            CollectionAssert.AreEqual(new[] { holder }, RegistryImportMapping.PendingAssets);

            RegistryLockProvider.Current = null;
            var mapped = RegistryImportMapping.MapPending(false);

            Assert.IsTrue(mapped.Success && !mapped.Postponed, mapped.Message);
            Assert.IsFalse(RegistryImportMapping.HasPending);
            Assert.AreEqual(a.MainRegistryId, AssetDatabase.LoadAssetAtPath<ReferenceHolder>(holder).single.RegistryId);
        }

        [Test]
        public void Map_LeavesReferencesWithoutAName()
        {
            string holder = CreateHolder(CreateFolder("Incoming"), "Holder", h => h.single = new TreeRef(RegistryGuid.NewGuid(), RegistryGuid.NewGuid(), ""));

            var result = RegistryImportMapping.Map(new[] { holder }, "test", false);

            Assert.AreEqual(1, result.Unmapped.Count, result.Message);
            Assert.IsEmpty(result.Rewritten);
        }
        #endregion
    }
}
