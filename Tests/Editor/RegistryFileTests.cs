using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

using static LMK.Editor.GameplayTags.Tests.TestIds;

namespace LMK.Editor.GameplayTags.Tests
{
    public sealed class RegistryFileTests
    {
        internal static RegistryData<TagRegistryRow> SampleData() => new()
        {
            registryId = MainRegistry,
            name = "Main",
            origin = RegistryOrigin.Main,
            rows = new List<RegistryRow<TagRegistryRow>>
            {
                TagRegistryRow.Create(Id(3), Id(2), "Projectile", false, "Fired projectile abilities"),
                TagRegistryRow.Create(Id(1), RegistryGuid.Zero, "Ability", true),
                TagRegistryRow.Create(Id(2), Id(1), "Fire", false, "Fire | burn"),
                TagRegistryRow.Create(Id(4), RegistryGuid.Zero, "Status", false),
            },
        };



        [Test]
        public void WriteThenParse_RoundTrips()
        {
            var original = SampleData();
            var result = TagRegistryFile.Parse(TagRegistryFile.Write(original));

            Assert.IsTrue(result.IsValid, string.Join("\n", result.Errors));
            Assert.IsEmpty(result.Warnings);
            Assert.IsFalse(result.ChecksumMismatch);
            Assert.AreEqual(original.registryId, result.Data.registryId);
            Assert.AreEqual("Main", result.Data.name);
            Assert.AreEqual(RegistryOrigin.Main, result.Data.origin);

            var parsed = result.Data.rows.ToDictionary(e => e.id);
            foreach (var row in original.rows)
            {
                Assert.AreEqual(row.data.parent, parsed[row.id].data.parent);
                Assert.AreEqual(row.data.segment, parsed[row.id].data.segment);
                Assert.AreEqual(row.data.isImplicit, parsed[row.id].data.isImplicit);
                Assert.AreEqual(row.data.description, parsed[row.id].data.description);
            }
        }

        [Test]
        public void Write_SortsRowsByGuid_UsesLf_AndIsStable()
        {
            var data = SampleData();
            string text = TagRegistryFile.Write(data);

            StringAssert.DoesNotContain("\r", text);
            StringAssert.StartsWith(TagRegistryFile.FORMAT_LINE, text);

            var ids = TagRegistryFile.Parse(text).Data.rows.Select(e => e.id).ToList();
            CollectionAssert.AreEqual(ids.OrderBy(e => e).ToList(), ids);

            data.rows.Reverse();
            Assert.AreEqual(text, TagRegistryFile.Write(data), "row order in memory doesn't change the file");
        }

        [Test]
        public void CrlfWorkingCopy_StillMatchesChecksum()
        {
            string crlf = TagRegistryFile.Write(SampleData()).Replace("\n", "\r\n");
            var result = TagRegistryFile.Parse(crlf);

            Assert.IsTrue(result.IsValid);
            Assert.IsFalse(result.ChecksumMismatch);
            Assert.AreEqual(TagRegistryFile.HashText(TagRegistryFile.Write(SampleData())), TagRegistryFile.HashText(crlf));
        }

        [Test]
        public void HandEditedRow_LoadsWithChecksumWarning()
        {
            string text = TagRegistryFile.Write(SampleData()).Replace("| Status", "| Statuz");
            var result = TagRegistryFile.Parse(text);

            Assert.IsTrue(result.IsValid, "a hand edit that parses still loads");
            Assert.IsTrue(result.ChecksumMismatch);
            Assert.IsTrue(result.Warnings.Any(e => e.Contains("Checksum mismatch")));
            Assert.IsTrue(result.Data.rows.Any(e => e.data.segment == "Statuz"));
        }

        [Test]
        public void TrailingWhitespace_DoesNotChangeChecksum()
        {
            string original = TagRegistryFile.Write(SampleData());
            string text = string.Join("\n", original.Split('\n').Select(e => e.Length > 0 ? e + "   \t" : e));

            Assert.AreNotEqual(original, text);
            Assert.IsFalse(TagRegistryFile.Parse(text).ChecksumMismatch);
        }

        [Test]
        public void OptionalPackageHeader_RoundTrips()
        {
            var data = SampleData();
            data.origin = RegistryOrigin.Generated;
            data.package = "com.vendor.combat";

            var result = TagRegistryFile.Parse(TagRegistryFile.Write(data));

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(RegistryOrigin.Generated, result.Data.origin);
            Assert.AreEqual("com.vendor.combat", result.Data.package);
        }



        #region Integrity
        private static void AssertRejected(RegistryData<TagRegistryRow> _Data, string _ExpectedError)
        {
            var result = TagRegistryFile.Parse(TagRegistryFile.Write(_Data));

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains(_ExpectedError)), $"expected \"{_ExpectedError}\" in:\n{string.Join("\n", result.Errors)}");
        }

        [Test]
        public void MissingParent_IsRejected()
        {
            var data = SampleData();
            data.rows.Add(TagRegistryRow.Create(Id(9), Id(50), "Orphan", false));
            AssertRejected(data, "does not exist");
        }

        [Test]
        public void ParentCycle_IsRejected()
        {
            var data = SampleData();
            data.rows.Add(TagRegistryRow.Create(Id(10), Id(11), "LoopA", false));
            data.rows.Add(TagRegistryRow.Create(Id(11), Id(10), "LoopB", false));
            AssertRejected(data, "cycle");
        }

        [Test]
        public void DuplicateSiblingSegments_AreRejected()
        {
            var data = SampleData();
            data.rows.Add(TagRegistryRow.Create(Id(12), Id(1), "Fire", false));
            AssertRejected(data, "share the parent");
        }

        [Test]
        public void DuplicateIds_AreRejected()
        {
            var data = SampleData();
            data.rows.Add(TagRegistryRow.Create(Id(4), RegistryGuid.Zero, "Other", false));
            AssertRejected(data, "Duplicate tag ID");
        }

        [Test]
        public void InvalidSegment_IsRejected()
        {
            var data = SampleData();
            data.rows.Add(TagRegistryRow.Create(Id(13), RegistryGuid.Zero, "Bad-Name", false));
            AssertRejected(data, "invalid character");
        }

        [Test]
        public void ConflictMarkers_AreRejected()
        {
            string text = TagRegistryFile.Write(SampleData());
            text = text.Insert(text.LastIndexOf('\n', text.Length - 2) + 1, "<<<<<<< HEAD\n=======\n>>>>>>> branch\n");

            var result = TagRegistryFile.Parse(text);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("conflict marker")));
        }

        [Test]
        public void MissingHeaders_AreRejected()
        {
            var result = TagRegistryFile.Parse("name Main\n");

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("\"registry\"")));
            Assert.IsTrue(result.Errors.Any(e => e.Contains("\"kind\"")));
        }
        #endregion
    }
}
