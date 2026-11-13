using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using NUnit.Framework;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags.Tests
{
    /// <summary>
    /// The generator on its own, from hand-made registries.
    /// </summary>
    public sealed class CodeGenSourceTests
    {
        private static readonly RegistryGuid REGISTRY = RegistryGuid.Parse("6b1f0c2a9d7e4b3f8a5c1d0e2f3a4b5c");
        private static readonly RegistryGuid FIRE = RegistryGuid.Parse("3f2ae8d1b6c04a7f8c2d9e0b1a4f77c1");

        private static TagCodeGen.Registry Registry(params (RegistryGuid Id, string Path)[] _Tags)
            => Registry(REGISTRY, "Main", true, _Tags);

        private static TagCodeGen.Registry Registry(RegistryGuid _Id, string _Name, bool _IsMain, params (RegistryGuid Id, string Path)[] _Tags)
        {
            var registry = new TagCodeGen.Registry { Id = _Id, Name = _Name, IsMain = _IsMain };
            foreach (var (id, path) in _Tags)
                registry.Tags.Add(new TagCodeGen.Tag(id, path));

            return registry;
        }

        private static string Generate(TagCodeGen.Registry _Registry, string _Previous = null, List<string> _Warnings = null, ISet<(RegistryGuid, string)> _Drop = null)
            => TagCodeGen.GenerateSource(new[] { _Registry }, _Previous, string.Empty, _Warnings, _Drop);

        private static Dictionary<string, TagCodeGen.GeneratedMember> Members(string _Source)
            => TagCodeGen.ParseGenerated(_Source).ToDictionary(e => e.Name);



        [Test]
        public void Constant_HoldsRegistryIdTagIdAndName()
        {
            string source = Generate(Registry((FIRE, "Ability.Fire")));

            // The literals are the halves of the 32-digit text, read big-endian, as in the registry file.
            StringAssert.Contains("RegistryId = new global::LMK.Runtime.Registry.RegistryGuid(0x6b1f0c2a9d7e4b3fUL, 0x8a5c1d0e2f3a4b5cUL);", source);
            StringAssert.Contains(
                "Ability_Fire = global::LMK.Runtime.GameplayTags.GameplayTag.FromId(RegistryId, " +
                "new global::LMK.Runtime.Registry.RegistryGuid(0x3f2ae8d1b6c04a7fUL, 0x8c2d9e0b1a4f77c1UL), \"Ability.Fire\");", source);
            StringAssert.Contains("public static partial class Tags", source);
            StringAssert.Contains("public static partial class Main", source);

            var member = Members(source)["Ability_Fire"];
            Assert.AreEqual(REGISTRY, member.RegistryId);
            Assert.AreEqual(FIRE, member.TagId);
            Assert.AreEqual("3f2ae8d1b6c04a7f8c2d9e0b1a4f77c1", member.TagId.ToString(), "string → RegistryGuid → literal → string");
            Assert.IsFalse(member.IsAlias);
        }

        [Test]
        public void CodeGeneratedBy10_IsStillRead()
        {
            // 1.0.0 wrote TagGuid literals; its constants and aliases must carry over to the first generation.
            const string SOURCE =
                "public static readonly global::LMK.Runtime.GameplayTags.TagGuid RegistryId = new global::LMK.Runtime.GameplayTags.TagGuid(0x6b1f0c2a9d7e4b3fUL, 0x8a5c1d0e2f3a4b5cUL);\n" +
                "public static readonly global::LMK.Runtime.GameplayTags.GameplayTag Ability_Fire = global::LMK.Runtime.GameplayTags.GameplayTag.FromId(RegistryId, " +
                "new global::LMK.Runtime.GameplayTags.TagGuid(0x3f2ae8d1b6c04a7fUL, 0x8c2d9e0b1a4f77c1UL), \"Ability.Fire\");\n";

            var member = Members(SOURCE)["Ability_Fire"];
            Assert.AreEqual(REGISTRY, member.RegistryId);
            Assert.AreEqual(FIRE, member.TagId);
        }

        [Test]
        public void Rename_LeavesAnObsoleteAlias_UntilPruned()
        {
            string v1 = Generate(Registry((FIRE, "Ability.Fire")));
            string v2 = Generate(Registry((FIRE, "Ability.Burn")), v1);

            StringAssert.Contains("[global::System.Obsolete(\"Renamed to Tags.Main.Ability_Burn (\\\"Ability.Burn\\\").\")]", v2);
            StringAssert.Contains($"Ability_Fire = Ability_Burn; // alias of {FIRE}", v2);
            Assert.IsTrue(Members(v2)["Ability_Fire"].IsAlias);
            Assert.AreEqual(FIRE, Members(v2)["Ability_Fire"].TagId);

            // A second rename: every old name points at the newest one.
            string v3 = Generate(Registry((FIRE, "Ability.Blaze")), v2);
            StringAssert.Contains("Ability_Fire = Ability_Blaze;", v3);
            StringAssert.Contains("Ability_Burn = Ability_Blaze;", v3);

            // Regenerating keeps the aliases.
            Assert.AreEqual(v3, Generate(Registry((FIRE, "Ability.Blaze")), v3));

            // Prune: unused aliases go, used ones stay.
            string pruned = Generate(Registry((FIRE, "Ability.Blaze")), v3, _Drop: new HashSet<(RegistryGuid, string)> { (REGISTRY, "Ability_Fire") });
            Assert.IsFalse(Members(pruned).ContainsKey("Ability_Fire"));
            Assert.IsTrue(Members(pruned)["Ability_Burn"].IsAlias);

            // Pruned names never come back.
            Assert.AreEqual(pruned, Generate(Registry((FIRE, "Ability.Blaze")), pruned));
        }

        [Test]
        public void Prune_FindsUsesInScripts()
        {
            string folder = Path.Combine("Temp", "__GameplayTagCodeGenTests");
            Directory.CreateDirectory(folder);

            try
            {
                string script = Path.Combine(folder, "User.cs");
                File.WriteAllText(script, "class User { void M() { var t = Tags.Main.Ability_Burn; } } // Ability_Firestorm\n");

                var used = TagCodeGen.FindUsedNames(new[] { "Ability_Fire", "Ability_Burn" }, new[] { script });

                CollectionAssert.AreEquivalent(new[] { "Ability_Burn" }, used, "whole words only");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void DeletedTag_LosesItsConstantAndAliases()
        {
            var other = RegistryGuid.Parse("9c41aa07e5d34b2c8f1e0d9a7b6c5d4e");
            string v1 = Generate(Registry((FIRE, "Ability.Fire"), (other, "Other")));
            string v2 = Generate(Registry((FIRE, "Ability.Burn"), (other, "Other")), v1);
            Assert.IsTrue(Members(v2).ContainsKey("Ability_Fire"));

            string v3 = Generate(Registry((other, "Other")), v2);

            CollectionAssert.AreEquivalent(new[] { "Other" }, Members(v3).Keys, "any use of the deleted tag is now a compile error");
            Assert.IsFalse(v3.Contains("Ability_"));
        }

        [Test]
        public void RenameThenAddTheOldName_TheNewTagTakesTheName()
        {
            var newFire = RegistryGuid.Parse("0a91c3e27f4b4d0c9e1f55b2c8d7a610");
            string v1 = Generate(Registry((FIRE, "Ability.Fire")));
            var warnings = new List<string>();

            string v2 = Generate(Registry((FIRE, "Ability.Burn"), (newFire, "Ability.Fire")), v1, warnings);

            var members = Members(v2);
            Assert.AreEqual(newFire, members["Ability_Fire"].TagId);
            Assert.IsFalse(members["Ability_Fire"].IsAlias);
            Assert.AreEqual(1, warnings.Count, string.Join("\n", warnings));
            StringAssert.Contains("Tags.Main.Ability_Burn", warnings[0]);
        }

        [Test]
        public void Names_ClashesKeywordsAndReservedNames()
        {
            var a = RegistryGuid.Parse("aaaaaaaa000000000000000000000001");
            var b = RegistryGuid.Parse("bbbbbbbb000000000000000000000002");
            var warnings = new List<string>();

            string source = Generate(Registry(
                (a, "A.B_C"),
                (b, "A_B.C"),
                (RegistryGuid.Parse("c0000000000000000000000000000003"), "class"),
                (RegistryGuid.Parse("d0000000000000000000000000000004"), "1st"),
                (RegistryGuid.Parse("e0000000000000000000000000000005"), "RegistryId"),
                (RegistryGuid.Parse("f0000000000000000000000000000006"), "Main")), null, warnings);

            var members = Members(source);
            Assert.AreEqual(a, members["A_B_C_aaaaaaaa"].TagId, "a clash never gives the name to one tag silently");
            Assert.AreEqual(b, members["A_B_C_bbbbbbbb"].TagId);
            Assert.IsFalse(members.ContainsKey("A_B_C"));
            StringAssert.Contains(" @class = ", source);
            Assert.IsTrue(members.ContainsKey("_1st"));
            Assert.IsTrue(members.ContainsKey("RegistryId_e0000000"));
            Assert.IsTrue(members.ContainsKey("Main_f0000000"), "a member can't share its class's name");
            Assert.AreEqual(4, warnings.Count, string.Join("\n", warnings));
        }

        [Test]
        public void OneClassPerRegistry()
        {
            var vendorA = RegistryGuid.Parse("a1000000000000000000000000000000");
            var vendorB = RegistryGuid.Parse("b2000000000000000000000000000000");
            var other = RegistryGuid.Parse("c3000000000000000000000000000000");

            string source = TagCodeGen.GenerateSource(new[]
            {
                Registry((FIRE, "Ability.Fire")),
                Registry(vendorA, "Vendor", false, (RegistryGuid.Parse("a1000000000000000000000000000001"), "Vendor.Stun")),
                Registry(vendorB, "Vendor", false),
                Registry(other, "com.other.combat", false),
            }, null, string.Empty);

            StringAssert.Contains("public static partial class Main\n", source);
            StringAssert.Contains("public static partial class Vendor_a1000000\n", source);
            StringAssert.Contains("public static partial class Vendor_b2000000\n", source);
            StringAssert.Contains("public static partial class com_other_combat\n", source);

            var stun = TagCodeGen.ParseGenerated(source).Single(e => e.Name == "Vendor_Stun");
            Assert.AreEqual(vendorA, stun.RegistryId, "members are read back under their own registry");
        }

        [Test]
        public void Namespace()
        {
            string inNamespace = TagCodeGen.GenerateSource(new[] { Registry() }, null, "My.Game");
            StringAssert.Contains("namespace My.Game\n{\n    /// <summary>", inNamespace);

            var warnings = new List<string>();
            string invalid = TagCodeGen.GenerateSource(new[] { Registry() }, null, "1st.class", warnings);
            Assert.IsFalse(invalid.Contains("namespace"));
            Assert.AreEqual(1, warnings.Count);
        }

        [Test]
        public void Output_IsDeterministicWithLfLineEndings()
        {
            string a = Generate(Registry((FIRE, "B"), (RegistryGuid.Parse("0a91c3e27f4b4d0c9e1f55b2c8d7a610"), "A")));
            string b = Generate(Registry((RegistryGuid.Parse("0a91c3e27f4b4d0c9e1f55b2c8d7a610"), "A"), (FIRE, "B")));

            Assert.AreEqual(a, b, "sorted by path");
            Assert.IsFalse(a.Contains("\r"));
            Assert.Less(a.IndexOf(" A = "), a.IndexOf(" B = "));
        }
    }



    /// <summary>
    /// Code generation driven by tag operations, written to a temporary file.
    /// </summary>
    public sealed class CodeGenTests : OperationTestBase
    {
        private static string Code => File.ReadAllText(CODE);

        [Test]
        public void GeneratedConstant_Resolves()
        {
            Add("Ability.Fire");
            Save();

            var member = TagCodeGen.ParseGenerated(Code).Single(e => e.Name == "Ability_Fire");
            var tag = GameplayTag.FromId(member.RegistryId, member.TagId, "Ability.Fire");

            Assert.AreEqual(Main.Id, member.RegistryId);
            Assert.AreEqual(IdOf("Ability.Fire"), member.TagId);
            Assert.AreEqual(TagStatus.Live, tag.Resolve().Status);
            Assert.AreEqual("Ability.Fire", tag.Path);
        }

        [Test]
        public void StagedEdits_AreNotGenerated_SaveWritesTheCode()
        {
            TagCodeGen.Generate();
            Add("A");

            Assert.IsFalse(TagCodeGen.IsPending);
            Assert.IsTrue(TagCodeGen.Generate().Success);
            StringAssert.DoesNotContain(" A = ", Code, "code follows the saved registries");

            Save();

            Assert.IsFalse(TagCodeGen.IsPending);
            StringAssert.Contains(" A = ", Code);
        }

        [Test]
        public void FlushWritesPendingCode()
        {
            Add("A");
            Save();
            File.Delete(CODE);
            TagCodeGen.MarkPending();

            TagCodeGen.Flush();

            Assert.IsFalse(TagCodeGen.IsPending);
            StringAssert.Contains(" A = ", Code);
        }

        [Test]
        public void RegistryWithCodeGenerationOff_GetsNoClass()
        {
            Add("A");
            Save();
            var settings = GameplayTagSettings.instance;
            var registryId = Main.Id;

            try
            {
                settings.SetCodeGenerated(registryId, false, false);
                Assert.IsTrue(TagCodeGen.Generate().Success);

                Assert.IsFalse(TagCodeGen.ParseGenerated(Code).Any(e => e.RegistryId == registryId));
                StringAssert.DoesNotContain("class Main", Code);
                Assert.AreEqual("Tags.Main", TagCodeGen.ClassNameOf(registryId), "the class it would get");
            }
            finally
            {
                settings.SetCodeGenerated(registryId, true, false);
            }

            Assert.IsTrue(TagCodeGen.Generate().Success);
            Assert.IsTrue(TagCodeGen.ParseGenerated(Code).Any(e => e.RegistryId == registryId && e.Name == "A"));
        }

        [Test]
        public void UnchangedCode_IsNotWritten()
        {
            Add("A");
            Save();
            var written = File.GetLastWriteTimeUtc(CODE);
            File.SetLastWriteTimeUtc(CODE, written.AddMinutes(-5));

            TagCodeGen.MarkPending();
            TagCodeGen.Flush();

            Assert.AreEqual(written.AddMinutes(-5), File.GetLastWriteTimeUtc(CODE), "same content: the file isn't touched");
        }

        [Test]
        public void WithoutLock_CodeStaysPending()
        {
            Add("A");
            RegistryLockProvider.Current = new RefusingLockProvider();

            var result = TagCodeGen.Generate();

            Assert.IsFalse(result.Success);
            Assert.IsTrue(TagCodeGen.IsPending);
            Assert.IsFalse(File.Exists(CODE));
        }

        [Test]
        public void RenameThenPrune_RemovesTheUnusedAlias()
        {
            // Prune scans every script, this one included, so the old name must not appear in any source file.
            string before = "Old" + System.Guid.NewGuid().ToString("N")[..12];
            var id = Add("PruneTest." + before);
            Save();

            Assert.IsTrue(TagOperations.Rename(id, "New").Success);
            Save();

            var alias = TagCodeGen.ParseGenerated(Code).Single(e => e.Name == "PruneTest_" + before);
            Assert.IsTrue(alias.IsAlias);
            Assert.AreEqual(id, alias.TagId);

            Assert.IsTrue(TagCodeGen.PruneAliases(out var pruned).Success);

            CollectionAssert.AreEqual(new[] { "PruneTest_" + before }, pruned);
            Assert.IsFalse(TagCodeGen.ParseGenerated(Code).Any(e => e.IsAlias));
            Assert.IsTrue(TagCodeGen.ParseGenerated(Code).Any(e => e.Name == "PruneTest_New"));
        }

        [Test]
        public void DeletedTag_ConstantRemoved()
        {
            Add("A");
            Add("B");
            Save();

            Assert.IsTrue(TagOperations.Delete(IdOf("A"), true).Success);
            Save();

            CollectionAssert.AreEquivalent(new[] { "B" }, TagCodeGen.ParseGenerated(Code).Select(e => e.Name));
        }

        [Test]
        public void PackageRegistries_GetTheirOwnClass_OtherMainRegistriesDont()
        {
            var vendor = new RegistryData<TagRegistryRow>
            {
                registryId = RegistryGuid.Parse("b0000000000000000000000000000002"),
                name = "Vendor",
                origin = RegistryOrigin.Package,
                rows = new List<RegistryRow<TagRegistryRow>> { TagRegistryRow.Create(TestIds.Id(1), RegistryGuid.Zero, "Stun", false) },
            };
            RegistryServiceTests.WriteAndImport(FOLDER + "/Vendor.tagregistry", TagRegistryFile.Write(vendor));
            Add("A");

            Save();

            var members = TagCodeGen.ParseGenerated(Code);
            CollectionAssert.AreEqual(new[] { "Stun" }, members.Where(e => e.RegistryId == vendor.registryId).Select(e => e.Name));
            StringAssert.Contains("public static partial class Vendor\n", Code);

            // Only this test's main registry: the project's own Main.tagregistry is also loaded, and skipped.
            int registryClasses = Regex.Matches(Code, @"\bRegistryId = new ").Count;
            int expected = 1 + TagRegistryService.Snapshot.Registries.Count(e => e.Kind != TagRegistryKind.Main);
            Assert.AreEqual(expected, registryClasses);
        }
    }
}
