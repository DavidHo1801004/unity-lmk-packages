using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags.Tests
{
    public sealed class TagFileFormatTests
    {
        [Test]
        public void Parse_ReadsPathsAndDescriptions()
        {
            var result = TagFile.Parse("# Combat.tags\nAbility.Fire ; Fire abilities ; and more\r\nStatus\n\n  Status.Burning;  \n", "Combat.tags");

            Assert.IsTrue(result.IsValid, string.Join("\n", result.Errors));
            CollectionAssert.AreEqual(new[] { "Ability.Fire", "Status", "Status.Burning" }, result.Entries.Select(e => e.Path));
            Assert.AreEqual("Fire abilities ; and more", result.Entries[0].Description, "everything after the first ';'");
            Assert.IsFalse(result.Entries[1].HasDescription, "no ';' means no description");
            Assert.AreEqual(string.Empty, result.Entries[2].Description, "a ';' with nothing after it is an empty description");
        }

        [Test]
        public void Parse_ReportsInvalidLinesWithTheirLocation()
        {
            var result = TagFile.Parse("A..B\nValid\nBad-Name ; text\n<<<<<<< HEAD\n", "Bad.tags");

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(3, result.Errors.Count, string.Join("\n", result.Errors));
            StringAssert.StartsWith("Bad.tags:1:", result.Errors[0]);
            StringAssert.StartsWith("Bad.tags:3:", result.Errors[1]);
            StringAssert.Contains("conflict marker", result.Errors[2]);
        }

        [Test]
        public void Parse_SamePathInSeveralFiles_IsOneEntry()
        {
            var result = TagFile.Parse(new[]
            {
                ("One.tags", "Status.Stun\nStatus.Burning ; From fire"),
                ("Two.tags", "Status.Stun ; Can't act\nStatus.Burning ; Another text"),
            });

            Assert.AreEqual(2, result.Entries.Count);
            Assert.AreEqual("Can't act", result.Entries.Single(e => e.Path == "Status.Stun").Description, "a later line fills in a missing description");
            Assert.AreEqual("From fire", result.Entries.Single(e => e.Path == "Status.Burning").Description, "the first description wins");
            Assert.AreEqual(1, result.Warnings.Count);
            StringAssert.Contains("Two.tags:2", result.Warnings[0]);
        }

        [Test]
        public void Write_SortsAndAlignsAndRoundTrips()
        {
            var entries = new[]
            {
                new TagFileEntry("Status.Burning", "Damage over time"),
                new TagFileEntry("Ability.Fire.Projectile", "Fired projectile abilities"),
                new TagFileEntry("Ability"),
            };

            string text = TagFile.Write(entries, new[] { "Combat.tags" });

            Assert.AreEqual(
                "# Combat.tags\n" +
                "Ability\n" +
                "Ability.Fire.Projectile ; Fired projectile abilities\n" +
                "Status.Burning          ; Damage over time\n",
                text);

            var parsed = TagFile.Parse(text);
            CollectionAssert.AreEquivalent(entries.Select(e => (e.Path, e.Description ?? string.Empty)),
                                           parsed.Entries.Select(e => (e.Path, e.Description ?? string.Empty)));
        }
    }



    public sealed class TagFileImportTests : OperationTestBase
    {
        private static string WriteTagFile(string _Name, string _Text)
        {
            string path = FOLDER + "/" + _Name;
            File.WriteAllText(path, _Text);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        private static TagFileImportResult Import(string _Text, bool _Overwrite = false, bool _Remove = false,
            System.Func<IReadOnlyList<string>, TagFileRemovalChoice> _Confirm = null)
        {
            string path = WriteTagFile("Import.tags", _Text);
            var options = new TagFileImportOptions { OverwriteDescriptions = _Overwrite, RemoveTagsNotInFiles = _Remove };

            return TagFileImport.Import(new[] { path }, options, _Confirm ?? (_ =>
            {
                Assert.Fail("No tag should be removed.");
                return TagFileRemovalChoice.Cancel;
            }));
        }

        private static TagRegistryRow Row(string _Path)
        {
            Assert.IsTrue(Main.TryGetRow(IdOf(_Path), out var row));
            return row;
        }



        [Test]
        public void EditingATagFile_WithoutImporting_ChangesNothing()
        {
            Add("Status.Stun");
            var stun = Ref("Status.Stun");
            string before = FileText;

            string path = WriteTagFile("Combat.tags", "Status.Stun\nStatus.Burning ; New\n");
            File.WriteAllText(path, "Status.Stunned\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            Assert.AreEqual(before, FileText);
            Assert.IsFalse(Main.TryFind("Status.Burning", out _));
            Assert.IsFalse(Main.TryFind("Status.Stunned", out _));
            Assert.AreEqual("Status.Stun", stun.Path);
            Assert.AreEqual(TagStatus.Live, stun.Resolve().Status);
        }

        [Test]
        public void Import_NewPaths_AddsThemWithImplicitParents()
        {
            var result = Import("Ability.Fire.Projectile ; Fired projectile abilities\nStatus.Burning\n");

            Assert.IsTrue(result.Success, result.Message);
            Assert.IsFalse(Row("Ability.Fire.Projectile").isImplicit);
            Assert.AreEqual("Fired projectile abilities", Row("Ability.Fire.Projectile").description);
            Assert.IsFalse(Row("Status.Burning").isImplicit);
            Assert.IsTrue(Row("Ability").isImplicit);
            Assert.IsTrue(Row("Ability.Fire").isImplicit);
            Assert.IsTrue(Row("Status").isImplicit);

            CollectionAssert.AreEqual(new[] { "Ability.Fire.Projectile", "Status.Burning" }, result.Plan.Added);
            CollectionAssert.AreEqual(new[] { "Ability", "Ability.Fire", "Status" }, result.Plan.ImplicitParentsAdded);
            AssertFileValid();
        }

        [Test]
        public void Import_PathOfAnImplicitTag_MakesItExplicit_WithTheSameId()
        {
            Add("Ability.Fire");
            var ability = Ref("Ability");
            Assert.IsTrue(Row("Ability").isImplicit);

            var result = Import("Ability ; Every ability\n");

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(ability.Id, IdOf("Ability"), "same GUID");
            Assert.IsFalse(Row("Ability").isImplicit);
            Assert.AreEqual("Every ability", Row("Ability").description, "an implicit tag takes the file's description");
            CollectionAssert.AreEqual(new[] { "Ability" }, result.Plan.MadeExplicit);
        }

        [Test]
        public void Import_RemoveOff_RemovesNothing()
        {
            Add("A");
            Add("B.C");

            var result = Import("A\nD\n");

            Assert.IsTrue(result.Success, result.Message);
            Assert.IsTrue(Main.TryFind("B.C", out _));
            Assert.IsTrue(Main.TryFind("D", out _));
            CollectionAssert.AreEqual(new[] { "B", "B.C" }, result.Plan.NotInFiles, "reported, not removed");
            CollectionAssert.IsEmpty(result.Plan.Removed);
        }

        [Test]
        public void Import_RemoveOn_TheDeleteDialogRunsForTheMissingPaths()
        {
            Add("A.X");
            Add("B.C");
            Add("Keep.Child");
            var bc = Ref("B.C");

            IReadOnlyList<string> asked = null;
            var result = Import("A\nKeep.Child\n", _Remove: true, _Confirm: paths =>
            {
                asked = paths;
                return TagFileRemovalChoice.Remove;
            });

            Assert.IsTrue(result.Success, result.Message);
            CollectionAssert.AreEqual(new[] { "A.X", "B", "B.C" }, asked);
            Assert.IsFalse(Main.TryFind("A.X", out _));
            Assert.IsFalse(Main.TryFind("B", out _));
            Assert.IsTrue(Main.TryFind("A", out _));
            Assert.IsTrue(Main.TryFind("Keep", out _), "parents of listed paths stay");
            Assert.AreEqual(TagStatus.Deleted, bc.Resolve().Status);
            AssertFileValid();
        }

        [Test]
        public void Import_RemoveOn_KeepOrCancel()
        {
            Add("A");
            Add("B");

            var kept = Import("A\nC\n", _Remove: true, _Confirm: _ => TagFileRemovalChoice.KeepThem);
            Assert.IsTrue(kept.Success, kept.Message);
            Assert.IsTrue(Main.TryFind("B", out _));
            Assert.IsTrue(Main.TryFind("C", out _), "the import still runs");

            string before = FileText;
            var cancelled = Import("A\nD\n", _Remove: true, _Confirm: _ => TagFileRemovalChoice.Cancel);
            Assert.IsTrue(cancelled.Cancelled);
            Assert.AreEqual(before, FileText, "nothing is written");
        }

        [Test]
        public void Import_RenamedLine_IsAnAdd_TheOriginalKeepsItsId()
        {
            Add("Status.Stun");
            var stun = Ref("Status.Stun");

            Assert.IsTrue(Import("Status.Stunned\n").Success);

            Assert.AreEqual(stun.Id, IdOf("Status.Stun"), "untouched");
            Assert.AreNotEqual(stun.Id, IdOf("Status.Stunned"), "a new tag, not a rename");
            Assert.AreEqual("Status.Stun", stun.Path);

            Assert.IsTrue(Import("Status.Stunned\n", _Remove: true, _Confirm: _ => TagFileRemovalChoice.Remove).Success);

            Assert.AreEqual(TagStatus.Deleted, stun.Resolve().Status, "delete plus add");
            Assert.IsTrue(Main.TryFind("Status.Stunned", out _));
        }

        [Test]
        public void Import_OverwriteDescriptions()
        {
            Assert.IsTrue(TagOperations.Add("A", "Old").Success);
            Assert.IsTrue(TagOperations.Add("B", "Kept").Success);

            Import("A ; New\nB\n");
            Assert.AreEqual("Old", Row("A").description, "off: existing descriptions stay");

            var result = Import("A ; New\nB\n", _Overwrite: true);
            Assert.AreEqual("New", Row("A").description);
            Assert.AreEqual("Kept", Row("B").description, "a line without ';' never changes a description");
            CollectionAssert.AreEqual(new[] { "A" }, result.Plan.DescriptionsUpdated);
        }

        [Test]
        public void Import_IsOneUndoStep()
        {
            Add("Existing");

            Assert.IsTrue(Import("Ability.Fire\nStatus.Stun ; Can't act\n").Success);
            Undo.PerformUndo();

            Assert.IsFalse(Main.TryFind("Ability", out _));
            Assert.IsFalse(Main.TryFind("Status.Stun", out _));
            Assert.IsTrue(Main.TryFind("Existing", out _));
            AssertFileValid();
        }

        [Test]
        public void Import_InvalidFile_ImportsNothing()
        {
            string before = FileText;

            var result = Import("Valid\nNot-Valid\n");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("Import.tags:2", result.Message);
            Assert.AreEqual(before, FileText);
        }

        [Test]
        public void Import_WithoutLock_IsRefused()
        {
            string before = FileText;
            TagLockProvider.Current = new RefusingLockProvider();

            var result = Import("A\n");

            Assert.IsFalse(result.Success);
            StringAssert.Contains("Held by a teammate", result.Message);
            Assert.AreEqual(before, FileText);
        }

        [Test]
        public void Import_UnchangedRegistry_WritesNothing()
        {
            Assert.IsTrue(TagOperations.Add("A.B", "Text").Success);
            string before = FileText;

            var result = Import("A.B ; Text\n", _Overwrite: true);

            Assert.IsTrue(result.Success, result.Message);
            Assert.IsFalse(result.Plan.HasChanges);
            Assert.AreEqual(before, FileText);
        }

        [Test]
        public void TagFile_ImportsAsAnEmptyAsset_RegistryUnchanged()
        {
            Add("A");
            string before = FileText;

            string path = WriteTagFile("Combat.tags", "A\nB ; Text\n");

            Assert.IsInstanceOf<TagFileImporter>(AssetImporter.GetAtPath(path));
            Assert.IsInstanceOf<TagFileAsset>(AssetDatabase.LoadMainAssetAtPath(path));
            CollectionAssert.IsEmpty(
                typeof(TagFileAsset).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
                "the imported asset stores none of the file's tags");
            Assert.AreEqual(before, FileText);
            Assert.IsFalse(Main.TryFind("B", out _));
        }

        [Test]
        public void TagFileWithABadLine_StillImports_WithAWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"Bad\.tags:2"));

            string path = WriteTagFile("Bad.tags", "A\nNot-Valid\n");

            Assert.IsInstanceOf<TagFileAsset>(AssetDatabase.LoadMainAssetAtPath(path));
        }

        [Test]
        public void Preview_ShowsEachTagsStatusAgainstTheMainRegistry()
        {
            Add("A.B");
            Assert.IsTrue(TagOperations.Add("C", "Old").Success);
            Add("D");

            var preview = TagFilePreview.Build(TagFile.Parse("A ; x\nA.B\nC ; New\nE.F\n"), Main);

            var nodes = Flatten(preview.Roots).ToDictionary(e => e.Path);
            CollectionAssert.AreEqual(new[] { "A", "C", "E" }, preview.Roots.Select(e => e.Path), "sorted, unlisted D left out");
            Assert.AreEqual(TagFileTagStatus.MakesExplicit, nodes["A"].Status);
            Assert.AreEqual(TagFileTagStatus.Exists, nodes["A.B"].Status);
            Assert.AreEqual(TagFileTagStatus.DescriptionDiffers, nodes["C"].Status);
            Assert.AreEqual("Old", nodes["C"].RegistryDescription);
            Assert.AreEqual("New", nodes["C"].Description);
            Assert.AreEqual(TagFileTagStatus.New, nodes["E"].Status);
            Assert.IsFalse(nodes["E"].IsListed);
            Assert.AreEqual(TagFileTagStatus.New, nodes["E.F"].Status);
            Assert.IsTrue(nodes["E.F"].IsListed);

            Assert.AreEqual(4, preview.ListedCount);
            Assert.AreEqual(1, preview.NewCount);
            Assert.AreEqual(1, preview.NewParentCount);
            Assert.AreEqual(1, preview.MakesExplicitCount);
            Assert.AreEqual(1, preview.DescriptionDiffersCount);
            Assert.IsTrue(preview.CanImport);
            Assert.AreEqual("4 tag(s) · 1 new · 1 new parent(s) · 1 made explicit · 1 description(s) differ", preview.Summary);
        }

        [Test]
        public void Preview_WithErrorsOrWithoutMainRegistry_CantImport()
        {
            var withErrors = TagFilePreview.Build(TagFile.Parse("A\nNot-Valid\n"), Main);
            Assert.IsFalse(withErrors.CanImport);
            Assert.AreEqual(1, withErrors.ListedCount, "valid lines are still shown");

            var withoutMain = TagFilePreview.Build(TagFile.Parse("A\n"), null);
            Assert.IsFalse(withoutMain.CanImport);
            Assert.AreEqual(TagFileTagStatus.NoMainRegistry, withoutMain.Roots[0].Status);
        }

        [Test]
        public void FindRegistry_ByFilePath()
        {
            var vendor = WriteVendorRegistry();

            Assert.AreEqual(vendor, TagFileExport.FindRegistry(FOLDER + "/Vendor.tagregistry").Id);
            Assert.AreEqual(Main.Id, TagFileExport.FindRegistry(MAIN).Id);
            Assert.IsNull(TagFileExport.FindRegistry(FOLDER + "/Missing.tagregistry"));
        }

        [Test]
        public void ExportToFolder_OneFilePerRegistry_ClashingNamesNumbered()
        {
            WriteVendorRegistry();
            Add("A");
            var vendor = TagFileExport.FindRegistry(FOLDER + "/Vendor.tagregistry");
            var registries = new[] { Main, vendor, vendor };
            string folder = CODE_FOLDER + "/Export";
            var errors = new List<string>();

            var written = TagFileExport.ExportToFolder(registries, folder, errors);

            CollectionAssert.IsEmpty(errors);
            CollectionAssert.AreEqual(new[] { "Main.tags", "Vendor.tags", "Vendor_2.tags" }, written.Select(Path.GetFileName));
            for (int i = 0; i < registries.Length; i++)
                Assert.AreEqual(TagFileExport.ExportText(registries[i]), File.ReadAllText(written[i]));
        }

        private static TagGuid WriteVendorRegistry()
        {
            var data = new TagRegistryData
            {
                registryId = TagGuid.Parse("b0000000000000000000000000000002"),
                name = "Vendor",
                kind = TagRegistryKind.Package,
                rows = new List<TagRegistryRow> { new(TestIds.Id(1), TagGuid.Zero, "Stun", false) },
            };
            RegistryServiceTests.WriteAndImport(FOLDER + "/Vendor.tagregistry", TagRegistryFile.Write(data));
            return data.registryId;
        }

        private static IEnumerable<TagFilePreview.Node> Flatten(IEnumerable<TagFilePreview.Node> _Nodes)
            => _Nodes.SelectMany(e => new[] { e }.Concat(Flatten(e.Children)));

        [Test]
        public void Export_MatchesRegistryPathsAndDescriptions_RegistryUnchanged()
        {
            Assert.IsTrue(TagOperations.Add("Ability.Fire.Projectile", "Fired projectile abilities").Success);
            Assert.IsTrue(TagOperations.Add("Status.Burning", "Damage over time").Success);
            Add("Status.Stun");
            Assert.IsTrue(TagOperations.EditDescription(IdOf("Ability"), "Implicit, with a description").Success);
            string before = FileText;

            string path = FOLDER + "/Exported.tags";
            Assert.IsTrue(TagFileExport.ExportToFile(Main, path, out string error), error);

            var parsed = TagFile.Parse(File.ReadAllText(path));
            Assert.IsTrue(parsed.IsValid, string.Join("\n", parsed.Errors));

            // Every registry path is listed, or is the parent of a listed path.
            var covered = new HashSet<string>();
            foreach (var entry in parsed.Entries)
                for (string p = entry.Path; p.Length > 0; p = TagPath.GetParent(p))
                    covered.Add(p);

            CollectionAssert.AreEquivalent(Main.Rows.Select(e => Main.GetPath(e.id)), covered);

            foreach (var entry in parsed.Entries)
                Assert.AreEqual(Row(entry.Path).description, entry.Description ?? string.Empty, entry.Path);

            Assert.AreEqual(before, FileText, "export never writes the registry");

            var reimport = TagFileImport.Import(new[] { path }, new TagFileImportOptions { OverwriteDescriptions = true });
            Assert.IsTrue(reimport.Success, reimport.Message);
            CollectionAssert.AreEqual(new[] { "Ability" }, reimport.Plan.MadeExplicit, "only the implicit tag with a description changes");
            CollectionAssert.IsEmpty(reimport.Plan.Added);
            CollectionAssert.IsEmpty(reimport.Plan.DescriptionsUpdated);
        }
    }
}
