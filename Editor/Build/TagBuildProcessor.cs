using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// A tag reference that doesn't resolve, found while building.
    /// </summary>
    internal sealed class MissingTagIssue
    {
        public string Location;
        public string PropertyPath;
        public GameplayTag Tag;
        public TagStatus Status;

        public override string ToString()
            => $"{Location} > {PropertyPath}: \"{Tag.LastKnownName}\" ({Status}, registry {Tag.RegistryId}, id {Tag.Id})";
    }



    /// <summary>
    /// Build steps of the tag system: bake the runtime database, keep it in Preloaded Assets, and report
    /// references that don't resolve in the content going into the build.
    /// </summary>
    internal sealed class TagBuildProcessor : IPreprocessBuildWithReport
    {
        public const string DATABASE_FOLDER = TagRegistryService.ROOT_FOLDER + "/Generated/Resources";
        public const string DATABASE_PATH = DATABASE_FOLDER + "/" + GameplayTagDatabase.RESOURCE_NAME + ".asset";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport _Report)
        {
            // Pending generated code goes out before the player scripts compile.
            TagCodeGen.Flush();

            // While building, the registries hold only saved content, so the baked database and the scene checks
            // agree. Staged edits come back once the build is done.
            if (TagOperations.HasPendingChanges)
            {
                Debug.LogWarning("The gameplay tags have unsaved changes in the Gameplay Tag Manager. They are not in this build.");
                EditorApplication.delayCall += TagRegistryService.Reload;
            }

            var database = Bake(DATABASE_PATH);
            EnsurePreloaded(database);

            var issues = ValidateAssets(GetResourcesContent());
            Report(issues, "Resources content", GameplayTagSettings.instance.BuildPolicyOnMissingTags);
        }



        #region Bake
        /// <summary>
        /// Write the database of every loaded registry, as saved, to <paramref name="_Path"/>. The asset keeps its GUID, and
        /// is only saved when its content changed.
        /// </summary>
        public static GameplayTagDatabase Bake(string _Path)
        {
            TagRegistryService.Reload();

            var built = TagRegistryService.SavedSnapshot.BuildDatabase();
            built.name = GameplayTagDatabase.RESOURCE_NAME;

            var existing = AssetDatabase.LoadAssetAtPath<GameplayTagDatabase>(_Path);
            if (existing == null)
            {
                EnsureFolder(Path.GetDirectoryName(_Path)?.Replace('\\', '/'));
                AssetDatabase.CreateAsset(built, _Path);
                AssetDatabase.SaveAssets();
                return built;
            }

            if (existing.ContentHash != built.ContentHash)
            {
                EditorUtility.CopySerialized(built, existing);
                existing.name = GameplayTagDatabase.RESOURCE_NAME;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
            }

            Object.DestroyImmediate(built);
            return existing;
        }

        /// <summary>
        /// Keep the baked database in Preloaded Assets permanently, so the player loads it at startup.
        /// Nothing is restored after the build.
        /// </summary>
        private static void EnsurePreloaded(GameplayTagDatabase _Database)
        {
            var preloaded = PlayerSettings.GetPreloadedAssets().ToList();
            if (preloaded.Contains(_Database)) return;

            preloaded.RemoveAll(e => e is GameplayTagDatabase);
            preloaded.Add(_Database);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
        }

        private static void EnsureFolder(string _Folder)
        {
            if (string.IsNullOrEmpty(_Folder) || AssetDatabase.IsValidFolder(_Folder)) return;

            string parent = Path.GetDirectoryName(_Folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(_Folder));
        }
        #endregion



        #region Validation
        /// <summary>
        /// Assets in <c>Resources</c> folders and everything they depend on, scenes excluded.
        /// </summary>
        internal static IEnumerable<string> GetResourcesContent()
        {
            var roots = AssetDatabase.GetAllAssetPaths()
                .Where(e => e.StartsWith("Assets/") && e.Contains("/Resources/") && !AssetDatabase.IsValidFolder(e))
                .ToArray();

            return AssetDatabase.GetDependencies(roots, true)
                .Where(e => e.EndsWith(".prefab") || e.EndsWith(".asset"))
                .Distinct();
        }

        /// <summary>
        /// Find unresolved references in assets. Read-only: assets are never changed.
        /// </summary>
        public static List<MissingTagIssue> ValidateAssets(IEnumerable<string> _AssetPaths)
        {
            var issues = new List<MissingTagIssue>();

            foreach (string path in _AssetPaths)
                foreach (var asset in TagPropertyWalker.GetAssetObjects(path))
                {
                    using var serializedObject = new SerializedObject(asset);
                    FindMissing(serializedObject, path, false, issues);
                }

            return issues;
        }

        /// <summary>
        /// Find unresolved references in a scene's components, inactive ones included.
        /// </summary>
        /// <param name="_WriteNone"> Replace each one with None. Only for the copy of a scene going into a build. </param>
        public static List<MissingTagIssue> ProcessScene(Scene _Scene, bool _WriteNone)
        {
            var issues = new List<MissingTagIssue>();

            foreach (var component in TagPropertyWalker.GetSceneObjects(_Scene))
            {
                using var serializedObject = new SerializedObject(component);
                string location = $"{_Scene.path} > {TagPropertyWalker.Describe(component)}";

                if (FindMissing(serializedObject, location, _WriteNone, issues) && _WriteNone)
                    serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            return issues;
        }

        /// <summary>
        /// Check every tag property, including lists, nested structs and managed references.
        /// </summary>
        /// <returns> Whether any reference was found missing. </returns>
        private static bool FindMissing(SerializedObject _SerializedObject, string _Location, bool _WriteNone, List<MissingTagIssue> _Issues)
        {
            bool found = false;

            foreach (var property in TagPropertyWalker.Enumerate(_SerializedObject))
            {
                var tag = GameplayTagDrawerHelper.Read(property);
                var resolution = tag.Resolve();
                if (!resolution.IsMissing) continue;

                found = true;
                _Issues.Add(new MissingTagIssue
                {
                    Location = _Location,
                    PropertyPath = property.propertyPath,
                    Tag = tag,
                    Status = resolution.Status,
                });

                if (_WriteNone)
                    GameplayTagDrawerHelper.Write(property, GameplayTag.None);
            }

            return found;
        }

        /// <summary>
        /// Log the issues, or fail the build under <see cref="MissingTagBuildPolicy.Fail"/>.
        /// </summary>
        /// <exception cref="BuildFailedException"> Under <see cref="MissingTagBuildPolicy.Fail"/>, if there is any issue. </exception>
        public static void Report(List<MissingTagIssue> _Issues, string _Context, MissingTagBuildPolicy _Policy)
        {
            if (_Issues.Count == 0) return;

            var message = new StringBuilder($"{_Issues.Count} gameplay tag reference(s) in {_Context} don't resolve:");
            foreach (var issue in _Issues)
                message.Append("\n  ").Append(issue);

            if (_Policy == MissingTagBuildPolicy.Fail)
                throw new BuildFailedException(message.ToString());

            Debug.LogWarning(message + "\nThey behave as None at runtime.");
        }
        #endregion
    }



    /// <summary>
    /// Writes None in place of unresolved references in the copy of each scene going into a build.
    /// Source scenes are never touched, and Play Mode is skipped so the editor keeps showing "(deleted)".
    /// </summary>
    internal sealed class TagSceneProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene _Scene, BuildReport _Report)
        {
            // Checked instead of "report == null", which may also be null for some build types.
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var issues = TagBuildProcessor.ProcessScene(_Scene, true);
            TagBuildProcessor.Report(issues, $"scene \"{_Scene.path}\"", GameplayTagSettings.instance.BuildPolicyOnMissingTags);
        }
    }
}
