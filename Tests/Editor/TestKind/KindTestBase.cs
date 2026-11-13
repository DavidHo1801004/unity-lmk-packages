using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using UnityEditor;

using LMK.Runtime.Registry;

using Object = UnityEngine.Object;

namespace LMK.Editor.Registry.Tests
{
    /// <summary>
    /// Base of tests that use the two tree test kinds: registers them with fresh main registries in a test folder,
    /// and removes every trace after each test.
    /// </summary>
    public abstract class KindTestBase
    {
        protected const string FOLDER = "Assets/__RegistryCoreTests";
        private const string MARKERS_FOLDER = "Temp/RegistryCoreTests";

        protected TreeKind a;
        protected TreeKindB b;
        private Action previousEnsureActive;

        /// <summary>
        /// Records every lock request, and refuses them while <see cref="refuse"/> is set.
        /// </summary>
        protected sealed class RecordingLockProvider : RegistryLockProvider
        {
            public readonly List<List<string>> requests = new();
            public bool refuse;

            public override string Name => "Recording";

            public override bool IsHeld(IReadOnlyList<string> _Paths) => !refuse;

            public override bool TryAcquire(IReadOnlyList<string> _Paths, out string _Error)
            {
                requests.Add(_Paths.ToList());
                _Error = refuse ? "Held by someone else." : null;
                return !refuse;
            }
        }

        [SetUp]
        public void RegisterKinds()
        {
            previousEnsureActive = TestDatabase.EditorEnsureActive;
            TestDatabase.EditorEnsureActive = null;
            TestDatabase.SetActive(null);
            RegistryLockProvider.Current = null;

            a = new TreeKind();
            b = new TreeKindB();
            a.GeneratedRegistryFolder = b.GeneratedRegistryFolder = $"{FOLDER}/Generated";
            RegistryKinds.Register(a);
            RegistryKinds.Register(b);

            Assert.IsTrue(a.CreateMainRegistry().Success);
            Assert.IsTrue(b.CreateMainRegistry().Success);
        }

        [TearDown]
        public void UnregisterKinds()
        {
            RegistryLockProvider.Current = null;
            RegistryPackages.InstalledPackagesOverride = null;

            foreach (RegistryKind kind in new RegistryKind[] { a, b })
            {
                RegistryKinds.Unregister(kind);

                var state = RegistryStagingState.Find(kind.Id);
                if (state != null)
                {
                    Undo.ClearUndo(state);
                    Object.DestroyImmediate(state);
                }
            }

            foreach (var database in new[] { a.EditorDatabase, b.EditorDatabase })
                if (database != null)
                    Object.DestroyImmediate(database);

            TestDatabase.SetActive(null);
            TestDatabase.EditorEnsureActive = previousEnsureActive;

            if (AssetDatabase.IsValidFolder(FOLDER))
                AssetDatabase.DeleteAsset(FOLDER);
            if (Directory.Exists(MARKERS_FOLDER))
                Directory.Delete(MARKERS_FOLDER, true);
        }



        #region Helpers
        protected static string FileText(RegistryKind _Kind) => File.ReadAllText(_Kind.MainRegistryPath);

        protected static string CreateFolder(string _Name)
        {
            string folder = $"{FOLDER}/{_Name}";
            RegistryAssets.EnsureFolder(folder);
            return folder;
        }

        /// <summary>
        /// Write a registry file and import it.
        /// </summary>
        protected static void WriteRegistry<TAsset>(TreeKindBase<TAsset> _Kind, string _Path, RegistryData<TreeRow> _Data) where TAsset : RegistryAsset<TreeRow>
        {
            RegistryAssets.EnsureFolder(Path.GetDirectoryName(_Path)?.Replace('\\', '/'));
            RegistryFile.WriteAtomic(_Path, RegistryFile.Write(_Data, _Kind));
            AssetDatabase.ImportAsset(_Path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        /// <summary>
        /// Save a <see cref="ReferenceHolder"/> asset, text-serialized, and return its path.
        /// </summary>
        protected static string CreateHolder(string _Folder, string _Name, Action<ReferenceHolder> _Fill)
        {
            var holder = UnityEngine.ScriptableObject.CreateInstance<ReferenceHolder>();
            _Fill(holder);

            string path = $"{_Folder}/{_Name}.asset";
            AssetDatabase.CreateAsset(holder, path);
            AssetDatabase.SaveAssets();
            return path;
        }

        protected static RegistryData<TreeRow> NewRegistry(RegistryGuid _Id, RegistryOrigin _Origin, params (int Id, int Parent, string Segment)[] _Rows)
        {
            var data = new RegistryData<TreeRow> { registryId = _Id, name = "Test", origin = _Origin };
            foreach (var (id, parent, segment) in _Rows)
                data.rows.Add(new RegistryRow<TreeRow>(Id(id), new TreeRow(parent == 0 ? RegistryGuid.Zero : Id(parent), segment)));

            return data;
        }

        protected static RegistryGuid Id(int _N) => new(0x2000_0000_0000_0000UL, (ulong)_N);
        #endregion
    }
}
