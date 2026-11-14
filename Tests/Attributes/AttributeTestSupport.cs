using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;

using LMK.AbilitySystem.Tests;
using LMK.Editor.Registry;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

using Object = UnityEngine.Object;
using Row = LMK.Runtime.AbilitySystem.GameplayAttributeDatabaseBuilder.Row;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// Fixed IDs and the test registry.
    /// </summary>
    public static class AttributeIds
    {
        public static readonly RegistryGuid MainRegistry = RegistryGuid.Parse("7c1e9a00000000000000000000000001");
        public static readonly RegistryGuid PackageRegistry = RegistryGuid.Parse("7c1e9a00000000000000000000000002");

        /// <summary>
        /// A deterministic, non-zero attribute ID.
        /// </summary>
        public static RegistryGuid Id(int _N) => new(0x3000_0000_0000_0000UL, (ulong)_N);

        public const int HEALTH = 1, MAX_HEALTH = 2, MANA = 3, MAX_MANA = 4, ATTACK_POWER = 5, ARMOR = 6, INCOMING_DAMAGE = 7, A = 8, B = 9;

        public static AttributeBound Bound(int _Id) => AttributeBound.FromAttribute(MainRegistry, Id(_Id));

        /// <summary>
        /// Health [0, MaxHealth], MaxHealth, Mana [0, MaxMana], MaxMana, AttackPower, Armor, the meta attribute
        /// IncomingDamage, and the unbounded A and B. Defaults are 100 for health and 50 for mana.
        /// </summary>
        public static IEnumerable<Row> MainRows() => new[]
        {
            new Row(Id(HEALTH), "Vitals.Health", 100f, AttributeBound.FromConstant(0f), Bound(MAX_HEALTH)),
            new Row(Id(MAX_HEALTH), "Vitals.MaxHealth", 100f, AttributeBound.FromConstant(1f)),
            new Row(Id(MANA), "Vitals.Mana", 50f, AttributeBound.FromConstant(0f), Bound(MAX_MANA), AttributeMaxChange.KeepRatio),
            new Row(Id(MAX_MANA), "Vitals.MaxMana", 50f, AttributeBound.FromConstant(0f)),
            new Row(Id(ATTACK_POWER), "Combat.AttackPower", 10f, AttributeBound.FromConstant(0f)),
            new Row(Id(ARMOR), "Combat.Armor", 0f, AttributeBound.FromConstant(0f)),
            new Row(Id(INCOMING_DAMAGE), "Meta.IncomingDamage", 0f, _Flags: AttributeFlags.Meta),
            new Row(Id(A), "A"),
            new Row(Id(B), "B"),
        };

        public static GameplayAttributeDatabaseBuilder MainBuilder()
            => new GameplayAttributeDatabaseBuilder().AddRegistry(MainRegistry, "Main", RegistryOrigin.Main, MainRows());

        public static GameplayAttribute Main(int _Id, string _Name = null) => GameplayAttribute.FromId(MainRegistry, Id(_Id), _Name);
    }



    /// <summary>
    /// Base for tests that swap the active attribute database. Restores the previous database, and the editor hook
    /// that loads one, after every test.
    /// </summary>
    public abstract class AttributeDatabaseTestBase
    {
        private GameplayAttributeDatabase previousActive;
        private Action previousEnsureActive;
        private readonly List<GameplayAttributeDatabase> created = new();

        [SetUp]
        public void SaveActiveDatabase()
        {
            previousEnsureActive = GameplayAttributeDatabase.EditorEnsureActive;
            previousActive = GameplayAttributeDatabase.Active;
            GameplayAttributeDatabase.EditorEnsureActive = null;
        }

        [TearDown]
        public void RestoreActiveDatabase()
        {
            GameplayAttributeDatabase.EditorEnsureActive = previousEnsureActive;
            GameplayAttributeDatabase.SetActive(previousActive);

            foreach (var database in created)
                if (database != null)
                    Object.DestroyImmediate(database);

            created.Clear();
        }

        /// <summary>
        /// Build a database, make it active, and destroy it after the test.
        /// </summary>
        protected GameplayAttributeDatabase Activate(GameplayAttributeDatabaseBuilder _Builder)
        {
            var database = Track(_Builder.Build());
            GameplayAttributeDatabase.SetActive(database);
            return database;
        }

        protected GameplayAttributeDatabase Track(GameplayAttributeDatabase _Database)
        {
            created.Add(_Database);
            return _Database;
        }
    }



    /// <summary>
    /// Runs every test against an attribute main registry in a test folder, never the project's own. Generated code
    /// and markers go to <c>Temp/</c>, and the reference index to its own file.
    /// </summary>
    public abstract class AttributeKindTestBase
    {
        protected const string FOLDER = "Assets/__AbilitySystemRegistryTests";

        // Outside Assets/, so writing generated code never triggers a script compile during the run.
        protected const string TEMP_FOLDER = "Temp/__AbilitySystemRegistryTests";
        protected const string ATTRIBUTE_CODE = TEMP_FOLDER + "/GameplayAttributes.g.cs";
        private const string INDEX = TEMP_FOLDER + "/ReferenceIndex.json";

        private readonly Dictionary<RegistryKind, (string Main, string Code, string Markers, string Generated, string Database)> previousPaths = new();
        private string previousIndex;

        internal static AttributeKind Attributes => AttributeKind.Instance;

        [SetUp]
        public void CreateAttributeRegistry()
        {
            RegistryLockProvider.Current = null;
            Undo.ClearAll();

            previousIndex = RegistryReferenceIndex.IndexPath;
            RegistryReferenceIndex.IndexPath = INDEX;
            RegistryReferenceIndex.Clear();

            if (!AssetDatabase.IsValidFolder(FOLDER))
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(FOLDER));

            Redirect(Attributes, "Attributes", ATTRIBUTE_CODE);
            var result = AttributeOperations.CreateMainRegistry();
            Assert.IsTrue(result.Success, result.Error);

            OnSetUp();
        }

        [TearDown]
        public void DeleteRegistries()
        {
            RegistryLockProvider.Current = null;
            RegistryPackages.InstalledPackagesOverride = null;
            RegistrySession.Discard();

            foreach (var (kind, paths) in previousPaths)
            {
                RegistryCodeGen.CancelPending(kind);
                kind.MainRegistryPath = paths.Main;
                kind.CodeOutputPath = paths.Code;
                kind.MarkersPath = paths.Markers;
                kind.GeneratedRegistryFolder = paths.Generated;
                kind.DatabaseFolder = paths.Database;
            }

            RegistryReferenceIndex.Flush();
            RegistryReferenceIndex.IndexPath = previousIndex;

            if (Directory.Exists(TEMP_FOLDER))
                Directory.Delete(TEMP_FOLDER, true);

            AssetDatabase.DeleteAsset(FOLDER);

            foreach (var kind in previousPaths.Keys)
                kind.Reload();

            previousPaths.Clear();
            Undo.ClearAll();
        }

        protected virtual void OnSetUp() { }

        /// <summary>
        /// Point a kind's files at the test folders, until the test ends.
        /// </summary>
        protected void Redirect(RegistryKind _Kind, string _Name, string _CodePath)
        {
            previousPaths[_Kind] = (_Kind.MainRegistryPath, _Kind.CodeOutputPath, _Kind.MarkersPath, _Kind.GeneratedRegistryFolder, _Kind.DatabaseFolder);

            _Kind.MainRegistryPath = $"{FOLDER}/{_Name}/Main.{_Kind.FileExtension}";
            _Kind.CodeOutputPath = _CodePath;
            _Kind.MarkersPath = $"{TEMP_FOLDER}/{_Name}Markers.json";
            _Kind.GeneratedRegistryFolder = $"{FOLDER}/{_Name}/Packages";
            _Kind.DatabaseFolder = $"{FOLDER}/{_Name}/Generated/Resources";
        }



        #region Helpers
        internal static RegistrySnapshot<AttributeRegistryRow>.Registry MainAttributes => Attributes.Snapshot.Main;

        protected static RegistryGuid AttributeRegistryId => Attributes.MainRegistryId;

        protected static RegistryGuid AddAttribute(string _Name, float _Default = 0f)
        {
            var result = AttributeOperations.Add(_Name, _Default);
            Assert.IsTrue(result.Success, result.Error);
            return result.Id;
        }

        protected static void SaveAll()
        {
            var result = RegistrySession.Save();
            Assert.IsTrue(result.Success, result.Error);
            Assert.IsFalse(RegistrySession.HasPendingChanges);
        }

        protected static RegistryGuid AttributeId(string _Name)
        {
            Assert.IsTrue(MainAttributes.TryFind(_Name, out var id), $"\"{_Name}\" not found");
            return id;
        }

        internal static AttributeRegistryRow AttributeRow(string _Name)
        {
            Assert.IsTrue(MainAttributes.TryGetRow(AttributeId(_Name), out var row));
            return row;
        }

        protected static void SetBounds(string _Name, AttributeBound _Min, AttributeBound _Max)
        {
            var metadata = AttributeMetadata.Of(AttributeRow(_Name));
            metadata.min = _Min;
            metadata.max = _Max;

            var result = AttributeOperations.EditMetadata(AttributeId(_Name), metadata);
            Assert.IsTrue(result.Success, result.Error);
        }

        protected static AttributeBound BoundTo(string _Name) => AttributeBound.FromAttribute(AttributeRegistryId, AttributeId(_Name));

        protected static string FileText(RegistryKind _Kind) => File.ReadAllText(_Kind.MainRegistryPath);

        protected static string CreateFolder(string _Name)
        {
            string folder = $"{FOLDER}/{_Name}";
            RegistryAssets.EnsureFolder(folder);
            return folder;
        }

        /// <summary>
        /// Save an <see cref="AttributeHolder"/> asset and return its path.
        /// </summary>
        protected static string CreateHolder(string _Folder, string _Name, Action<AttributeHolder> _Fill)
        {
            var holder = ScriptableObject.CreateInstance<AttributeHolder>();
            _Fill(holder);

            string path = $"{_Folder}/{_Name}.asset";
            AssetDatabase.CreateAsset(holder, path);
            AssetDatabase.SaveAssets();
            return path;
        }

        /// <summary>
        /// Write an attribute registry file and import it.
        /// </summary>
        internal static void WriteRegistry(string _Path, RegistryData<AttributeRegistryRow> _Data)
        {
            RegistryAssets.EnsureFolder(Path.GetDirectoryName(_Path)?.Replace('\\', '/'));
            RegistryFile.WriteAtomic(_Path, RegistryFile.Write(_Data, Attributes));
            AssetDatabase.ImportAsset(_Path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }
        #endregion
    }



    /// <summary>
    /// The gameplay tag kind, reached through the registry core and, for its staged operations, reflection: its
    /// editor API is internal to the tag package.
    /// </summary>
    public static class TagKindAccess
    {
        public const string ID = "gameplaytags";

        private static readonly Type OPERATIONS = Type.GetType("LMK.Editor.GameplayTags.TagOperations, LMK.GameplayTags.Editor", true);

        public static RegistryKind Kind
        {
            get
            {
                var kind = RegistryKinds.Find(ID);
                Assert.IsNotNull(kind, "the gameplay tag kind is registered");
                return kind;
            }
        }

        public static RegistryResult CreateMainRegistry() => (RegistryResult)Call("CreateMainRegistry");

        public static RegistryResult Add(string _Path) => (RegistryResult)Call("Add", _Path, null);

        public static RegistryResult Rename(RegistryGuid _Id, string _NewSegment) => (RegistryResult)Call("Rename", _Id, _NewSegment);

        public static RegistryResult Delete(RegistryGuid _Id) => (RegistryResult)Call("Delete", _Id, false);

        private static object Call(string _Method, params object[] _Arguments)
        {
            var method = OPERATIONS.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(e => e.Name == _Method && e.GetParameters().Length == _Arguments.Length);

            return method.Invoke(null, _Arguments);
        }
    }



    /// <summary>
    /// Runs every test against a tag main registry and an attribute main registry in the test folder: the first
    /// tests that have both kinds.
    /// </summary>
    public abstract class CrossKindTestBase : AttributeKindTestBase
    {
        protected const string TAG_CODE = TEMP_FOLDER + "/GameplayTags.g.cs";

        protected static RegistryKind Tags => TagKindAccess.Kind;

        protected override void OnSetUp()
        {
            Redirect(Tags, "Tags", TAG_CODE);
            var result = TagKindAccess.CreateMainRegistry();
            Assert.IsTrue(result.Success, result.Error);
        }

        protected static RegistryGuid TagRegistryId => Tags.MainRegistryId;

        protected static RegistryGuid AddTag(string _Path)
        {
            var result = TagKindAccess.Add(_Path);
            Assert.IsTrue(result.Success, result.Error);
            return result.Id;
        }
    }
}
