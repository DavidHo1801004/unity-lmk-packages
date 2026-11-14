using System.Globalization;
using System.Linq;
using System.Threading;

using NUnit.Framework;

using LMK.Editor.Registry;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

using static LMK.Editor.AbilitySystem.Tests.AttributeIds;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// The <c>*.attrregistry</c> format and the registry rules, without the asset database.
    /// </summary>
    public sealed class AttributeRegistryFileTests
    {
        private static AttributeKind Kind => AttributeKind.Instance;

        private static RegistryData<AttributeRegistryRow> Registry(params RegistryRow<AttributeRegistryRow>[] _Rows)
        {
            var data = new RegistryData<AttributeRegistryRow> { registryId = MainRegistry, name = "Main", origin = RegistryOrigin.Main };
            data.rows.AddRange(_Rows);
            return data;
        }

        private static RegistryRow<AttributeRegistryRow> Attribute(int _Id, string _Name, float _Default = 0f, AttributeBound _Min = default,
                                                                   AttributeBound _Max = default, AttributeFlags _Flags = AttributeFlags.None)
            => AttributeRegistryRow.Create(Id(_Id), _Name, _Default, _Min, _Max, AttributeMaxChange.Clamp, _Flags);

        private static System.Collections.Generic.List<AttributeIssue> Check(RegistryData<AttributeRegistryRow> _Data) => AttributeOperations.Validate(_Data);



        #region Format
        [Test]
        public void WriteThenParse_RoundTripsEveryColumn()
        {
            var data = Registry(
                AttributeRegistryRow.Create(Id(HEALTH), "Vitals.Health", 100f, AttributeBound.FromConstant(0f), Bound(MAX_HEALTH),
                                            AttributeMaxChange.KeepRatio, AttributeFlags.Integer, "Hit points | current"),
                AttributeRegistryRow.Create(Id(MAX_HEALTH), "Vitals.MaxHealth", 100.25f, AttributeBound.FromConstant(-1.5f)),
                AttributeRegistryRow.Create(Id(INCOMING_DAMAGE), "Meta.IncomingDamage", 0f, _Flags: AttributeFlags.Meta | AttributeFlags.Integer));

            string text = RegistryFile.Write(data, Kind);
            var parsed = RegistryFile.Parse(text, Kind);

            Assert.IsTrue(parsed.IsValid, string.Join("\n", parsed.Errors));
            Assert.IsFalse(parsed.ChecksumMismatch);
            Assert.AreEqual(3, parsed.Data.rows.Count);

            foreach (var row in data.rows)
            {
                var read = parsed.Data.rows.Single(e => e.id == row.id).data;
                Assert.AreEqual(row.data.name, read.name);
                Assert.AreEqual(row.data.defaultValue, read.defaultValue);
                Assert.AreEqual(row.data.min, read.min);
                Assert.AreEqual(row.data.max, read.max);
                Assert.AreEqual(row.data.onMaxChange, read.onMaxChange);
                Assert.AreEqual(row.data.flags, read.flags);
                Assert.AreEqual(row.data.description, read.description, "the last column may hold the separator");
            }

            Assert.AreEqual(text, RegistryFile.Write(parsed.Data, Kind), "writing is stable");
        }

        [Test]
        public void Write_SpellsBoundsPoliciesAndFlags()
        {
            string text = RegistryFile.Write(Registry(
                AttributeRegistryRow.Create(Id(MANA), "Vitals.Mana", 50f, AttributeBound.FromConstant(0f), Bound(MAX_MANA), AttributeMaxChange.KeepRatio),
                AttributeRegistryRow.Create(Id(INCOMING_DAMAGE), "Meta.IncomingDamage", _Flags: AttributeFlags.Meta | AttributeFlags.Integer)), Kind);

            StringAssert.StartsWith(AttributeKind.FORMAT_LINE, text);
            StringAssert.Contains($"@{MainRegistry}:{Id(MAX_MANA)}", text);
            StringAssert.Contains("keepRatio", text);
            StringAssert.Contains("meta,integer", text);
            StringAssert.Contains("| clamp", text);
            Assert.IsFalse(text.Contains('\r'), "LF line endings");
        }

        [Test]
        public void Numbers_UseTheInvariantCulture()
        {
            var culture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            try
            {
                string text = RegistryFile.Write(Registry(AttributeRegistryRow.Create(Id(1), "A", 1.5f, AttributeBound.FromConstant(0.25f))), Kind);
                StringAssert.Contains("1.5", text);
                StringAssert.Contains("0.25", text);
                Assert.AreEqual(1.5f, RegistryFile.Parse(text, Kind).Data.rows[0].data.defaultValue);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [TestCase("abc", "-", "-", "clamp", "-", "invalid default")]
        [TestCase("1", "@nope", "-", "clamp", "-", "invalid attribute bound")]
        [TestCase("1", "-", "lots", "clamp", "-", "invalid bound")]
        [TestCase("1", "-", "-", "squash", "-", "unknown onMaxChange")]
        [TestCase("1", "-", "-", "clamp", "huge", "unknown flag")]
        [TestCase("NaN", "-", "-", "clamp", "-", "invalid default")]
        public void InvalidCells_RejectTheRow_NamingTheProblem(string _Default, string _Min, string _Max, string _Policy, string _Flags, string _Expected)
        {
            Assert.IsFalse(Kind.TryParseRow(new[] { "A", _Default, _Min, _Max, _Policy, _Flags, string.Empty }, out _, out string error));
            StringAssert.Contains(_Expected, error);
        }

        [Test]
        public void ReadsAMinimalHandWrittenRow()
        {
            Assert.IsTrue(Kind.TryParseRow(new[] { "Vitals.Health", "100", "0", "-", "", "", "" }, out var row, out string error), error);
            Assert.AreEqual(AttributeMaxChange.Clamp, row.onMaxChange, "an empty policy is clamp");
            Assert.AreEqual(AttributeFlags.None, row.flags);
            Assert.AreEqual(AttributeBound.FromConstant(0f), row.min);
            Assert.IsTrue(row.max.IsNone);
        }
        #endregion



        #region Rules
        [Test]
        public void ValidRegistry_HasNoIssue()
        {
            var data = Registry(
                Attribute(HEALTH, "Vitals.Health", 100f, AttributeBound.FromConstant(0f), Bound(MAX_HEALTH)),
                Attribute(MAX_HEALTH, "Vitals.MaxHealth", 100f, AttributeBound.FromConstant(1f)),
                Attribute(INCOMING_DAMAGE, "Meta.IncomingDamage", _Flags: AttributeFlags.Meta),
                Attribute(A, "A", _Max: AttributeBound.FromAttribute(PackageRegistry, Id(99))));

            CollectionAssert.IsEmpty(Check(data), "a bound in another registry is checked when it's set, not here");
        }

        [Test]
        public void Names_MustBeValidAndUnique()
        {
            var issues = Check(Registry(Attribute(1, "Vitals.Health"), Attribute(2, "Vitals.Health"), Attribute(3, "Bad-Name"), Attribute(4, "Trailing.")));

            Assert.IsTrue(issues.HasError(AttributeIssueKind.DuplicateName, "Vitals.Health"));
            Assert.IsTrue(issues.HasError(AttributeIssueKind.InvalidName, "Bad-Name"));
            Assert.IsTrue(issues.HasError(AttributeIssueKind.InvalidName, "Trailing."));
        }

        [Test]
        public void DanglingBound_InTheSameRegistry()
        {
            var issues = Check(Registry(Attribute(1, "Health", _Max: Bound(2))));

            Assert.IsTrue(issues.HasError(AttributeIssueKind.DanglingBound, "Health"), string.Join("\n", issues));
        }

        [Test]
        public void ClampCycle_IsRejected_ListingTheAttributes()
        {
            var data = Registry(Attribute(1, "X", _Max: Bound(2)), Attribute(2, "Y", _Max: Bound(1)), Attribute(3, "Z", _Max: Bound(1)));

            var issues = Check(data);
            Assert.IsTrue(issues.HasError(AttributeIssueKind.ClampCycle, "X", "Y"), string.Join("\n", issues));
            Assert.IsFalse(issues.Any(e => e.Kind == AttributeIssueKind.ClampCycle && e.Attributes.Contains("Z")), "Z is not on the cycle");
            StringAssert.Contains("\"X\" → \"Y\" → \"X\"", issues.Single(e => e.Kind == AttributeIssueKind.ClampCycle).Message);

            var parsed = RegistryFile.Parse(RegistryFile.Write(data, Kind), Kind);
            Assert.IsFalse(parsed.IsValid, "a file with a cycle is rejected");
        }

        [Test]
        public void SelfBound_IsACycle()
        {
            Assert.IsTrue(Check(Registry(Attribute(1, "X", _Min: Bound(1)))).HasError(AttributeIssueKind.ClampCycle, "X"));
        }

        [Test]
        public void Ranges_ConstantMinBelowMax_AndDefaultInside()
        {
            var issues = Check(Registry(
                Attribute(1, "Inverted", 0f, AttributeBound.FromConstant(5f), AttributeBound.FromConstant(1f)),
                Attribute(2, "TooHigh", 10f, AttributeBound.FromConstant(0f), AttributeBound.FromConstant(5f)),
                Attribute(3, "AttributeBounds", -10f, _Max: Bound(2))));

            Assert.IsTrue(issues.HasError(AttributeIssueKind.RangeError, "Inverted"));
            Assert.IsTrue(issues.HasError(AttributeIssueKind.RangeError, "TooHigh"));
            Assert.IsFalse(issues.HasError(AttributeIssueKind.RangeError, "AttributeBounds"), "only constant bounds are checked");
        }

        [Test]
        public void MetaAttributes_HaveNoBounds_AndBoundNothing()
        {
            var issues = Check(Registry(
                Attribute(1, "Meta.Damage", _Max: AttributeBound.FromConstant(10f), _Flags: AttributeFlags.Meta),
                Attribute(2, "Meta.Scratch", _Flags: AttributeFlags.Meta),
                Attribute(3, "Health", _Max: Bound(2))));

            Assert.IsTrue(issues.HasError(AttributeIssueKind.MetaMisuse, "Meta.Damage"));
            Assert.IsTrue(issues.HasError(AttributeIssueKind.MetaMisuse, "Health", "Meta.Scratch"));
        }

        [Test]
        public void BoundWithoutRegistry_IsDangling()
        {
            var issues = Check(Registry(Attribute(1, "Health", _Max: AttributeBound.FromAttribute(RegistryGuid.Zero, Id(2)))));

            Assert.IsTrue(issues.HasError(AttributeIssueKind.DanglingBound, "Health"));
        }
        #endregion
    }
}
