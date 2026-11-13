using System.IO;
using System.Linq;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry.Tests
{
    public sealed class RegistryFileTests
    {
        private static readonly RegistryGuid Main = RegistryGuid.Parse("6b1f0c2a9d7e4b3f8a5c1d0e2f3a4b5c");

        private static RegistryGuid Id(int _N) => new(0x1000_0000_0000_0000UL, (ulong)_N);

        private static RegistryRow<TreeRow> Row(int _Id, int _Parent, string _Segment, bool _Implicit = false, string _Description = "")
            => new(Id(_Id), new TreeRow(_Parent == 0 ? RegistryGuid.Zero : Id(_Parent), _Segment, _Implicit, _Description));

        private static RegistryData<TreeRow> SampleData()
        {
            var data = new RegistryData<TreeRow> { registryId = Main, name = "Main", origin = RegistryOrigin.Main };
            data.rows.Add(Row(3, 1, "Fire", false, "Burns | over time"));
            data.rows.Add(Row(1, 0, "Ability", true));
            data.rows.Add(Row(2, 1, "Ice"));
            data.rows.Add(Row(4, 0, "Status"));
            return data;
        }

        private static RegistryParseResult<TreeRow> Parse(string _Text) => RegistryFile.Parse(_Text, TreeFormat.Instance);

        private static string Write(RegistryData<TreeRow> _Data) => RegistryFile.Write(_Data, TreeFormat.Instance);

        /// <summary>
        /// A gameplay tag registry written by com.lmk.gameplaytags 1.0.0.
        /// </summary>
        private const string TAG_REGISTRY_V3 =
            "# gameplaytag-registry v3 — written by the Gameplay Tag Manager. Do not edit by hand.\n" +
            "registry  7338dde9672e4165b8c7a755fd361488\n" +
            "name      Main\n" +
            "kind      main\n" +
            "checksum  sha256:0658ae268b45672b6fe44d99fe3aa0f729c04b34e3210dacfc66bea5315b1507\n" +
            "# guid                           | parent                           | segment | flags    | description\n" +
            "0aa0781293eb4f74af5ab9bd85468d7a | 61514ec01278426e815a3156b009f168 | Stun    | -        |\n" +
            "1570a3ddc5764dd7b87e798c9691519e | 324a4b686e6d4c2ca07341735ba38fc6 | Ability | implicit |\n" +
            "324a4b686e6d4c2ca07341735ba38fc6 | -                                | GASU    | implicit |\n" +
            "348356e1cc7a4de7b3c53013aeb0a61d | 61514ec01278426e815a3156b009f168 | Shocked | -        |\n" +
            "34c4068addad41f28784ee6cd1eaac7f | 61514ec01278426e815a3156b009f168 | Bleed   | -        |\n" +
            "4c69c07ae0274dd58670ec44b78df695 | c5dc0f9691c74e3e952497d169f1854b | Range   | -        |\n" +
            "61514ec01278426e815a3156b009f168 | 324a4b686e6d4c2ca07341735ba38fc6 | Effect  | implicit |\n" +
            "6b3235556319461fb97f296629579f47 | c5dc0f9691c74e3e952497d169f1854b | Melee   | -        |\n" +
            "a3e03890f3e140ea9359ce92d105f5ac | c5dc0f9691c74e3e952497d169f1854b | Single  | -        |\n" +
            "c5dc0f9691c74e3e952497d169f1854b | 1570a3ddc5764dd7b87e798c9691519e | Attack  | -        |\n" +
            "e19418575d2a4882846f20e6a0ac8f2c | c5dc0f9691c74e3e952497d169f1854b | AOE     | -        |\n" +
            "e9c8da90a424455cb86977f29dbca10e | 61514ec01278426e815a3156b009f168 | Burning | -        |\n";



        #region Format
        [Test]
        public void TagRegistryV3_ParsesAndIsRewrittenByteForByte()
        {
            var result = Parse(TAG_REGISTRY_V3);

            Assert.IsTrue(result.IsValid, string.Join("\n", result.Errors));
            Assert.IsEmpty(result.Warnings);
            Assert.AreEqual(12, result.Data.rows.Count);
            Assert.AreEqual(TAG_REGISTRY_V3, Write(result.Data));
        }

        [Test]
        public void WriteThenParse_RoundTrips()
        {
            var original = SampleData();
            original.origin = RegistryOrigin.Generated;
            original.package = "com.vendor.combat";
            original.version = "1.2.0";

            var result = Parse(Write(original));

            Assert.IsTrue(result.IsValid, string.Join("\n", result.Errors));
            Assert.IsEmpty(result.Warnings);
            Assert.AreEqual(Main, result.Data.registryId);
            Assert.AreEqual(RegistryOrigin.Generated, result.Data.origin);
            Assert.AreEqual("com.vendor.combat", result.Data.package);
            Assert.AreEqual("1.2.0", result.Data.version);

            foreach (var row in original.rows)
            {
                var parsed = result.Data.rows[result.Data.IndexOf(row.id)];
                Assert.AreEqual(row.data.parent, parsed.data.parent);
                Assert.AreEqual(row.data.segment, parsed.data.segment);
                Assert.AreEqual(row.data.isImplicit, parsed.data.isImplicit);
                Assert.AreEqual(row.data.description, parsed.data.description, "the last column may contain the separator");
            }
        }

        [Test]
        public void Write_SortsRowsByGuid_UsesLf_AndIsStable()
        {
            var data = SampleData();
            string text = Write(data);

            StringAssert.DoesNotContain("\r", text);
            StringAssert.StartsWith(TreeFormat.Instance.FormatLine, text);

            var ids = text.Split('\n').Where(l => l.Contains('|') && !l.StartsWith("#")).Select(l => l[..32]).ToList();
            CollectionAssert.AreEqual(ids.OrderBy(i => i, System.StringComparer.Ordinal).ToList(), ids);

            data.rows.Reverse();
            Assert.AreEqual(text, Write(data), "row order in memory doesn't change the file");
        }

        [Test]
        public void MissingLastColumn_ReadsAsEmpty()
        {
            string text = Write(SampleData()).Replace(" | Burns | over time", string.Empty);
            var result = Parse(text);

            Assert.AreEqual(string.Empty, result.Data.rows[result.Data.IndexOf(Id(3))].data.description);
        }
        #endregion



        #region Checksum
        [Test]
        public void CrlfWorkingCopy_StillMatchesChecksum()
        {
            string crlf = Write(SampleData()).Replace("\n", "\r\n");
            var result = Parse(crlf);

            Assert.IsTrue(result.IsValid);
            Assert.IsFalse(result.ChecksumMismatch);
            Assert.AreEqual(RegistryFile.HashText(Write(SampleData())), RegistryFile.HashText(crlf));
        }

        [Test]
        public void HandEditedRow_LoadsWithChecksumWarning_NamingTheEditor()
        {
            var result = Parse(Write(SampleData()).Replace("| Status", "| Statuz"));

            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(result.ChecksumMismatch);
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("Checksum mismatch") && w.Contains("Gameplay Tag Manager")));
        }

        [Test]
        public void TrailingWhitespace_DoesNotChangeChecksum()
        {
            string original = Write(SampleData());
            var result = Parse(original.Replace("\n", "   \n"));

            Assert.IsFalse(result.ChecksumMismatch);
        }

        [Test]
        public void MissingChecksum_IsAWarningOnly()
        {
            string text = string.Join("\n", Write(SampleData()).Split('\n').Where(l => !l.StartsWith("checksum")));
            var result = Parse(text);

            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(result.ChecksumMismatch);
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("Missing checksum")));
        }
        #endregion



        #region Rejection
        private static void AssertRejected(string _Text, string _ExpectedError)
        {
            var result = Parse(_Text);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains(_ExpectedError)), $"expected \"{_ExpectedError}\" in:\n{string.Join("\n", result.Errors)}");
        }

        [Test]
        public void DuplicateIds_AreRejected_WithTheKindsNoun()
        {
            var data = SampleData();
            data.rows.Add(Row(4, 0, "Other"));
            AssertRejected(Write(data), "Duplicate tag ID");
        }

        [Test]
        public void KindValidation_RejectsTheFile()
        {
            var data = SampleData();
            data.rows.Add(Row(9, 50, "Orphan"));
            AssertRejected(Write(data), "does not exist");
        }

        [Test]
        public void KindParseErrors_NameTheLine()
        {
            string text = Write(SampleData()).Replace("| implicit |", "| magic    |");
            AssertRejected(text, "unknown flags \"magic\"");

            var result = Parse(text);
            Assert.IsTrue(result.Errors.Any(e => e.StartsWith("Line ")));
        }

        [Test]
        public void InvalidRowId_IsRejected_WithTheKindsNoun()
        {
            string text = Write(SampleData()) + "nothex | - | Bad | - |\n";
            AssertRejected(text, "invalid tag ID \"nothex\"");
        }

        [Test]
        public void TooFewColumns_AreRejected_NamingTheColumns()
        {
            string text = Write(SampleData()) + $"{Id(20)} | - \n";
            AssertRejected(text, "guid | parent | segment | flags | description");
        }

        [Test]
        public void ConflictMarkers_AreRejected()
        {
            string text = Write(SampleData());
            text = text.Insert(text.IndexOf(Id(2).ToString()), "<<<<<<< HEAD\n");
            AssertRejected(text, "conflict marker");
        }

        [Test]
        public void MissingHeaders_AreRejected()
        {
            string text = string.Join("\n", Write(SampleData()).Split('\n').Where(l => !l.StartsWith("registry") && !l.StartsWith("kind")));
            var result = Parse(text);

            Assert.IsTrue(result.Errors.Any(e => e.Contains("\"registry\"")));
            Assert.IsTrue(result.Errors.Any(e => e.Contains("\"kind\"")));
        }

        [Test]
        public void HeaderAfterRows_IsRejected()
        {
            AssertRejected(Write(SampleData()) + "name      Late\n", "header line after the first row");
        }

        [Test]
        public void UnknownHeader_IsAWarningOnly()
        {
            string text = Write(SampleData()).Replace("kind      main\n", "kind      main\ncolour    blue\n");
            var result = Parse(text);

            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(result.Warnings.Any(w => w.Contains("unknown header \"colour\"")));
        }
        #endregion



        #region Disk and import
        private const string TEST_FOLDER = "Assets/__RegistryCoreTests";

        [TearDown]
        public void DeleteTestFolder()
        {
            if (AssetDatabase.IsValidFolder(TEST_FOLDER))
                AssetDatabase.DeleteAsset(TEST_FOLDER);

            // Files written without an import leave a folder the asset database doesn't know.
            if (Directory.Exists(TEST_FOLDER))
                Directory.Delete(TEST_FOLDER, true);
            if (File.Exists(TEST_FOLDER + ".meta"))
                File.Delete(TEST_FOLDER + ".meta");
        }

        private static void EnsureTestFolder()
        {
            if (!AssetDatabase.IsValidFolder(TEST_FOLDER))
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(TEST_FOLDER));
        }

        [Test]
        public void WriteAtomic_CreatesThenReplaces_LeavingNoTemporaryFile()
        {
            string path = Path.Combine(TEST_FOLDER, "Atomic.txt");

            RegistryFile.WriteAtomic(path, "first");
            Assert.AreEqual("first", File.ReadAllText(path));

            RegistryFile.WriteAtomic(path, "second");
            Assert.AreEqual("second", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(Path.Combine("Temp", "LMKRegistry", "Atomic.txt.tmp")));
        }

        [Test]
        public void Importer_ImportsAValidFile()
        {
            EnsureTestFolder();
            string path = $"{TEST_FOLDER}/Valid.{TreeFormat.EXTENSION}";
            RegistryFile.WriteAtomic(path, Write(SampleData()));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var asset = AssetDatabase.LoadAssetAtPath<TreeRegistryAsset>(path);

            Assert.IsNotNull(asset);
            Assert.IsTrue(asset.IsValid);
            Assert.AreEqual(4, asset.Data.rows.Count);
            Assert.AreEqual(RegistryFile.HashText(File.ReadAllText(path)), asset.FileHash);
        }

        [Test]
        public void Importer_RejectsAnInvalidFile_AndReportsWhy()
        {
            var data = SampleData();
            data.rows.Add(Row(9, 50, "Orphan"));
            EnsureTestFolder();
            string path = $"{TEST_FOLDER}/Invalid.{TreeFormat.EXTENSION}";
            RegistryFile.WriteAtomic(path, Write(data));

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("does not exist"));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var asset = AssetDatabase.LoadAssetAtPath<TreeRegistryAsset>(path);
            Assert.IsNotNull(asset);
            Assert.IsFalse(asset.IsValid);
            Assert.IsNull(asset.Data);
            Assert.IsTrue(asset.Errors.Any(e => e.Contains("does not exist")));
        }
        #endregion
    }
}
