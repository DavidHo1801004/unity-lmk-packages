using System;
using System.Collections.Generic;

using NUnit.Framework;

using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

// TagGuid is kept, obsolete, for code written against 1.0.0: these tests keep it working.
#pragma warning disable CS0618

namespace LMK.Editor.GameplayTags.Tests
{
    public sealed class TagGuidTests
    {
        [Test]
        public void ConvertsToAndFromRegistryGuid_ByValue()
        {
            var tagGuid = TagGuid.Parse("0123456789abcdeffedcba9876543210");

            RegistryGuid registryGuid = tagGuid;
            TagGuid back = registryGuid;

            Assert.AreEqual(tagGuid.Hi, registryGuid.Hi);
            Assert.AreEqual(tagGuid.Lo, registryGuid.Lo);
            Assert.AreEqual(tagGuid, back);

            // Code written against 1.0.0 still compiles: TagGuid arguments are accepted where RegistryGuid is asked.
            var tag = GameplayTag.FromId(tagGuid, TagGuid.Parse("00000000000000000000000000000001"));
            Assert.AreEqual(registryGuid, tag.RegistryId);
        }

        [Test]
        public void Parse_ReadsHalvesBigEndianFromHexText()
        {
            // Hi is the first 16 hex digits, Lo the last 16. Not Guid.ToByteArray order.
            var guid = TagGuid.Parse("0123456789abcdeffedcba9876543210");

            Assert.AreEqual(0x0123456789abcdefUL, guid.Hi);
            Assert.AreEqual(0xfedcba9876543210UL, guid.Lo);
        }

        [Test]
        public void TextRoundTrip_IsExactAndLowercase()
        {
            const string TEXT = "3f2ae8d1b6c04a7f8c2d9e0b1a4f77c1";

            Assert.AreEqual(TEXT, TagGuid.Parse(TEXT).ToString());
            Assert.AreEqual(TEXT, TagGuid.Parse(TEXT.ToUpperInvariant()).ToString());
        }

        [Test]
        public void TextToConstructorLiteral_RoundTrips()
        {
            // The form generated code will use: new TagGuid(0x<hi>UL, 0x<lo>UL).
            var original = TagGuid.NewGuid();
            var rebuilt = new TagGuid(Convert.ToUInt64(original.ToString()[..16], 16), Convert.ToUInt64(original.ToString()[16..], 16));

            Assert.AreEqual(original, rebuilt);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("3f2ae8d1b6c04a7f8c2d9e0b1a4f77c")]   // 31 digits
        [TestCase("3f2ae8d1b6c04a7f8c2d9e0b1a4f77c1a")] // 33 digits
        [TestCase("3f2ae8d1-b6c0-4a7f-8c2d-9e0b1a4f77")] // dashes
        [TestCase("3f2ae8d1b6c04a7f8c2d9e0b1a4f77cg")]  // not hex
        public void TryParse_RejectsInvalidText(string _Text)
        {
            Assert.IsFalse(TagGuid.TryParse(_Text, out var result));
            Assert.IsTrue(result.IsZero);
        }

        [Test]
        public void NewGuid_IsNeverZeroAndUnique()
        {
            var seen = new HashSet<TagGuid>();
            for (int i = 0; i < 1000; i++)
            {
                var guid = TagGuid.NewGuid();
                Assert.IsFalse(guid.IsZero);
                Assert.IsTrue(seen.Add(guid));
            }
        }

        [Test]
        public void CompareTo_MatchesOrdinalOrderOfText()
        {
            var guids = new List<TagGuid> { TagGuid.Parse("ffffffffffffffff0000000000000000"), TagGuid.Parse("7fffffffffffffffffffffffffffffff") };
            for (int i = 0; i < 200; i++)
                guids.Add(TagGuid.NewGuid());

            var byValue = new List<TagGuid>(guids);
            byValue.Sort((a, b) => a.CompareTo(b));

            var byText = new List<TagGuid>(guids);
            byText.Sort((a, b) => string.CompareOrdinal(a.ToString(), b.ToString()));

            CollectionAssert.AreEqual(byText, byValue);
        }
    }
}
