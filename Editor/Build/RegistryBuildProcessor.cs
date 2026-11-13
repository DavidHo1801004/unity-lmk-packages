using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

using LMK.Runtime.Registry;

using Object = UnityEngine.Object;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// A reference that doesn't resolve, found while building.
    /// </summary>
    public sealed class MissingReferenceIssue
    {
        public string Location;
        public string PropertyPath;
        public RegistryReference Reference;
        public RegistryStatus Status;

        public override string ToString()
            => $"{Location} > {PropertyPath}: {Reference.Kind?.DisplayName} \"{Reference.Name}\" ({Status}, registry {Reference.Registry}, id {Reference.Id})";
    }



    /// <summary>
    /// Build steps shared by every registry kind: write pending generated files, bake each kind's runtime database
    /// from its saved registries, keep it in Preloaded Assets, and report references that don't resolve in the
    /// content going into the build.
    /// </summary>
    public sealed class RegistryBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport _Report)
        {
            var kinds = RegistryKinds.All;
            if (kinds.Count == 0) return;

            // Pending generated code goes out before the player scripts compile.
            foreach (var kind in kinds)
                kind.InvokeOnBeforeBuild();

            // While building, the registries hold only saved content, so the baked databases and the scene checks
            // agree. Staged edits come back once the build is done.
            var pending = kinds.Where(e => e.HasPendingChanges).ToList();
            if (pending.Count > 0)
            {
                Debug.LogWarning($"{string.Join(" and ", pending.Select(e => e.DisplayName))} have unsaved changes. They are not in this build.");
                EditorApplication.delayCall += () =>
                {
                    foreach (var kind in RegistryKinds.All)
                        kind.Reload();
                };
            }

            foreach (var kind in kinds)
                EnsurePreloaded(Bake(kind));

            Report(ValidateAssets(GetResourcesContent()), "Resources content");
        }



        #region Bake
        /// <summary>
        /// Path of a kind's baked database: in its <see cref="RegistryKind.DatabaseFolder"/>, named after the database type.
        /// </summary>
        public static string DatabasePath(RegistryKind _Kind, Type _DatabaseType)
            => $"{_Kind.DatabaseFolder.TrimEnd('/')}/{_DatabaseType.Name}.asset";

        /// <summary>
        /// Write the database of every loaded registry of a kind, as saved, to its database path. The asset keeps its
        /// GUID, and is only saved when its content changed.
        /// </summary>
        public static RegistryDatabase Bake(RegistryKind _Kind) => Bake(_Kind, null);

        /// <summary>
        /// Write the database of every loaded registry of a kind, as saved, to <paramref name="_Path"/>.
        /// </summary>
        /// <param name="_Path"> The asset to write, or <see langword="null"/> for the kind's database path. </param>
        public static RegistryDatabase Bake(RegistryKind _Kind, string _Path)
        {
            _Kind.Reload();

            var built = _Kind.BuildSavedDatabase();
            var type = built.GetType();
            string name = type.Name;
            string path = _Path ?? DatabasePath(_Kind, type);
            built.name = name;
            built.hideFlags = HideFlags.None;

            var existing = AssetDatabase.LoadAssetAtPath(path, type) as RegistryDatabase;
            if (existing == null)
            {
                RegistryAssets.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                AssetDatabase.CreateAsset(built, path);
                AssetDatabase.SaveAssets();
                return built;
            }

            if (existing.ContentHash != built.ContentHash)
            {
                EditorUtility.CopySerialized(built, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
            }

            Object.DestroyImmediate(built);
            return existing;
        }

        /// <summary>
        /// Keep a baked database in Preloaded Assets permanently, in place of any other database of its type, so the
        /// player loads it at startup. Nothing is restored after the build.
        /// </summary>
        public static void EnsurePreloaded(RegistryDatabase _Database)
        {
            var preloaded = PlayerSettings.GetPreloadedAssets().ToList();
            if (preloaded.Contains(_Database)) return;

            var type = _Database.GetType();
            preloaded.RemoveAll(e => e == null || e.GetType() == type);
            preloaded.Add(_Database);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
        }
        #endregion



        #region Validation
        /// <summary>
        /// Assets in <c>Resources</c> folders and everything they depend on, scenes excluded.
        /// </summary>
        public static IEnumerable<string> GetResourcesContent()
        {
            var roots = AssetDatabase.GetAllAssetPaths()
                .Where(e => e.StartsWith("Assets/") && e.Contains("/Resources/") && !AssetDatabase.IsValidFolder(e))
                .ToArray();

            return AssetDatabase.GetDependencies(roots, true)
                .Where(e => e.EndsWith(".prefab") || e.EndsWith(".asset"))
                .Distinct();
        }

        /// <summary>
        /// Find unresolved references of every kind in assets. Read-only: assets are never changed.
        /// </summary>
        public static List<MissingReferenceIssue> ValidateAssets(IEnumerable<string> _AssetPaths)
        {
            var issues = new List<MissingReferenceIssue>();

            foreach (string path in _AssetPaths)
                foreach (var asset in RegistryPropertyWalker.GetAssetObjects(path))
                {
                    using var serializedObject = new SerializedObject(asset);
                    FindMissing(serializedObject, path, false, issues);
                }

            return issues;
        }

        /// <summary>
        /// Find unresolved references of every kind in a scene's components, inactive ones included.
        /// </summary>
        /// <param name="_WriteNone"> Replace each one with None. Only for the copy of a scene going into a build. </param>
        public static List<MissingReferenceIssue> ProcessScene(Scene _Scene, bool _WriteNone)
        {
            var issues = new List<MissingReferenceIssue>();

            foreach (var component in RegistryPropertyWalker.GetSceneObjects(_Scene))
            {
                using var serializedObject = new SerializedObject(component);
                string location = $"{_Scene.path} > {RegistryPropertyWalker.Describe(component)}";

                if (FindMissing(serializedObject, location, _WriteNone, issues) && _WriteNone)
                    serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            return issues;
        }

        /// <returns> Whether any reference was found missing. </returns>
        private static bool FindMissing(SerializedObject _SerializedObject, string _Location, bool _WriteNone, List<MissingReferenceIssue> _Issues)
        {
            bool found = false;

            foreach (var (property, kind) in RegistryPropertyWalker.Enumerate(_SerializedObject))
            {
                var reference = RegistryProperties.Read(property, kind);
                var status = kind.GetStatus(reference.Registry, reference.Id);
                if (status != RegistryStatus.Deleted && status != RegistryStatus.MissingRegistry) continue;

                found = true;
                _Issues.Add(new MissingReferenceIssue
                {
                    Location = _Location,
                    PropertyPath = property.propertyPath,
                    Reference = reference,
                    Status = status,
                });

                if (_WriteNone)
                    RegistryProperties.Write(property, RegistryGuid.Zero, RegistryGuid.Zero, null);
            }

            return found;
        }

        /// <summary>
        /// Fail the build with the issues of every kind whose policy is <see cref="MissingReferencePolicy.Fail"/>, and log
        /// the others as warnings.
        /// </summary>
        /// <param name="_Policy"> The policy of a kind. <see cref="RegistrySettings"/> when <see langword="null"/>. </param>
        /// <exception cref="BuildFailedException"> If an issue belongs to a kind whose policy fails builds. </exception>
        public static void Report(List<MissingReferenceIssue> _Issues, string _Context, Func<RegistryKind, MissingReferencePolicy> _Policy = null)
        {
            if (_Issues.Count == 0) return;

            _Policy ??= kind => RegistrySettings.instance.GetPolicy(kind?.Id);

            var failing = _Issues.Where(e => _Policy(e.Reference.Kind) == MissingReferencePolicy.Fail).ToList();
            var warning = _Issues.Except(failing).ToList();

            if (warning.Count > 0)
                Debug.LogWarning(Describe(warning, _Context) + "\nThey behave as None at runtime.");

            if (failing.Count > 0)
                throw new BuildFailedException(Describe(failing, _Context));
        }

        private static string Describe(List<MissingReferenceIssue> _Issues, string _Context)
        {
            var message = new StringBuilder($"{_Issues.Count} registry reference(s) in {_Context} don't resolve:");
            foreach (var issue in _Issues)
                message.Append("\n  ").Append(issue);

            return message.ToString();
        }
        #endregion
    }



    /// <summary>
    /// Writes None in place of unresolved references of every kind in the copy of each scene going into a build.
    /// Source scenes are never touched, and Play Mode is skipped so the editor keeps showing "(deleted)".
    /// </summary>
    public sealed class RegistrySceneProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene _Scene, BuildReport _Report)
        {
            // Checked instead of "report == null", which may also be null for some build types.
            if (EditorApplication.isPlayingOrWillChangePlaymode || RegistryKinds.All.Count == 0) return;

            var issues = RegistryBuildProcessor.ProcessScene(_Scene, true);
            RegistryBuildProcessor.Report(issues, $"scene \"{_Scene.path}\"");
        }
    }
}
