using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;

using LMK.Runtime.GameplayTags;

using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// The CI checks of the tag system that run inside Unity: registry integrity, build content and pack
    /// validation. Registry ID stability compares two revisions, so it runs outside Unity (see the repository's CI
    /// workflow), and the serialization fixture is an edit mode test.
    /// </summary>
    /// <remarks>
    /// Call the checks from your own tests, or run them from the command line:
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod LMK.Editor.GameplayTags.TagProjectChecks.RunFromCommandLine</c>.
    /// The editor exits with 1 if any check fails.
    /// </remarks>
    public static class TagProjectChecks
    {
        /// <summary>
        /// Every registry file in the project and in the packages you author parses, is consistent and has a valid
        /// checksum.
        /// </summary>
        /// <returns> One message per problem. </returns>
        public static List<string> CheckRegistryIntegrity()
        {
            var failures = new List<string>();

            foreach (string path in OwnRegistryFiles())
            {
                var parsed = TagRegistryFile.Parse(File.ReadAllText(path));

                foreach (string error in parsed.Errors)
                    failures.Add($"\"{path}\": {error}");

                if (parsed.ChecksumMismatch)
                    failures.Add($"\"{path}\": the checksum doesn't match its rows. It was edited outside the Gameplay Tag Manager.");
            }

            return failures;
        }

        /// <summary>
        /// The tag references going into a build resolve: in the enabled build scenes, and in Resources content.
        /// </summary>
        /// <param name="_Warnings"> Unresolved references, when the build policy only warns about them. </param>
        /// <returns> Unresolved references, when the build policy fails builds on them. </returns>
        public static List<string> CheckBuildContent(List<string> _Warnings = null)
        {
            var issues = new List<MissingTagIssue>();

            // Build scenes are opened one by one, replacing the open ones.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("The build content check needs to open the build scenes.");

            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (var buildScene in EditorBuildSettings.scenes.Where(e => e.enabled && File.Exists(e.path)))
                {
                    var scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                    issues.AddRange(TagBuildProcessor.ProcessScene(scene, false));
                }
            }
            finally
            {
                // Untitled scenes can't be restored: start from an empty one then.
                if (setup.Length > 0 && setup.All(e => !string.IsNullOrEmpty(e.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            issues.AddRange(TagBuildProcessor.ValidateAssets(TagBuildProcessor.GetResourcesContent()));

            var messages = issues.Select(e => $"{e}: doesn't resolve.").ToList();
            if (GameplayTagSettings.instance.BuildPolicyOnMissingTags == MissingTagBuildPolicy.Fail)
                return messages;

            _Warnings?.AddRange(messages);
            return new List<string>();
        }

        /// <summary>
        /// The packages you author (embedded or local) pass pack validation.
        /// </summary>
        /// <returns> One message per problem, prefixed with the package name. </returns>
        public static List<string> CheckAuthoredPackages()
        {
            var failures = new List<string>();

            foreach (var info in AuthoredPackages())
            {
                var package = TagPackage.From(info);
                var published = GameplayTagSettings.instance.TryGetPublishedRegistry(package.Name, out var id) ? id : TagGuid.Zero;

                foreach (string error in TagPackValidation.Validate(package, published))
                    failures.Add($"{package.Name}: {error}");
            }

            return failures;
        }

        /// <summary>
        /// Run every check, log the results, and exit the editor: 0 if every check passed, 1 otherwise.
        /// </summary>
        public static void RunFromCommandLine()
        {
            var warnings = new List<string>();
            var failures = new List<string>();

            TagRegistryService.Reload();
            failures.AddRange(CheckRegistryIntegrity());
            failures.AddRange(CheckBuildContent(warnings));
            failures.AddRange(CheckAuthoredPackages());

            foreach (string warning in warnings)
                Debug.LogWarning($"Gameplay tag check: {warning}");

            if (failures.Count == 0)
                Debug.Log("Gameplay tag checks passed.");
            else
                Debug.LogError($"Gameplay tag checks failed:\n- {string.Join("\n- ", failures)}");

            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }



        /// <summary>
        /// Registry files you are responsible for: in <c>Assets/</c> and in the packages you author.
        /// </summary>
        private static IEnumerable<string> OwnRegistryFiles()
        {
            var roots = new List<string> { "Assets/" };
            roots.AddRange(AuthoredPackages().Select(e => e.assetPath.TrimEnd('/') + "/"));

            return AssetDatabase.GetAllAssetPaths()
                .Where(e => e.EndsWith("." + TagRegistryFile.EXTENSION, StringComparison.OrdinalIgnoreCase))
                .Where(e => roots.Any(root => e.StartsWith(root, StringComparison.Ordinal)))
                .OrderBy(e => e, StringComparer.Ordinal);
        }

        private static IEnumerable<PackageInfo> AuthoredPackages()
            => PackageInfo.GetAllRegisteredPackages().Where(e => e.source == PackageSource.Embedded || e.source == PackageSource.Local);
    }
}
