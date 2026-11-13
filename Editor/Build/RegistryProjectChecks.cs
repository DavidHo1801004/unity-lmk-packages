using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;

using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// The CI checks of every registry kind that run inside Unity: registry integrity, build content and pack
    /// validation. Registry ID stability compares two revisions, so it runs outside Unity.
    /// </summary>
    /// <remarks>
    /// Call the checks from your own tests, or run them from the command line:
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod LMK.Editor.Registry.RegistryProjectChecks.RunFromCommandLine</c>.
    /// The editor exits with 1 if any check fails.
    /// </remarks>
    public static class RegistryProjectChecks
    {
        /// <summary>
        /// Every registry file of every kind, in the project and in the packages you author, parses, is consistent and
        /// has a valid checksum.
        /// </summary>
        /// <returns> One message per problem. </returns>
        public static List<string> CheckRegistryIntegrity()
        {
            var failures = new List<string>();

            foreach (var kind in RegistryKinds.All)
                foreach (string path in OwnRegistryFiles(kind))
                    foreach (string problem in kind.CheckFile(File.ReadAllText(path)))
                        failures.Add($"\"{path}\": {problem}");

            return failures;
        }

        /// <summary>
        /// The references of every kind going into a build resolve: in the enabled build scenes, and in Resources content.
        /// </summary>
        /// <param name="_Warnings"> Unresolved references of kinds whose build policy only warns about them. </param>
        /// <returns> Unresolved references of kinds whose build policy fails builds on them. </returns>
        public static List<string> CheckBuildContent(List<string> _Warnings = null)
        {
            var issues = new List<MissingReferenceIssue>();

            // Build scenes are opened one by one, replacing the open ones.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("The build content check needs to open the build scenes.");

            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (var buildScene in EditorBuildSettings.scenes.Where(e => e.enabled && File.Exists(e.path)))
                {
                    var scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                    issues.AddRange(RegistryBuildProcessor.ProcessScene(scene, false));
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

            issues.AddRange(RegistryBuildProcessor.ValidateAssets(RegistryBuildProcessor.GetResourcesContent()));

            var failures = new List<string>();
            foreach (var issue in issues)
            {
                string message = $"{issue}: doesn't resolve.";
                if (RegistrySettings.instance.GetPolicy(issue.Reference.Kind?.Id) == MissingReferencePolicy.Fail) failures.Add(message);
                else _Warnings?.Add(message);
            }

            return failures;
        }

        /// <summary>
        /// The packages you author (embedded or local) pass pack validation, for every kind.
        /// </summary>
        /// <returns> One message per problem, prefixed with the package name. </returns>
        public static List<string> CheckAuthoredPackages()
        {
            var failures = new List<string>();

            foreach (var info in AuthoredPackages())
            {
                var package = RegistryPackage.From(info);
                foreach (string error in RegistryPackageService.Validate(package))
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

            foreach (var kind in RegistryKinds.All)
                kind.Reload();

            failures.AddRange(CheckRegistryIntegrity());
            failures.AddRange(CheckBuildContent(warnings));
            failures.AddRange(CheckAuthoredPackages());

            foreach (string warning in warnings)
                Debug.LogWarning($"Registry check: {warning}");

            if (failures.Count == 0)
                Debug.Log("Registry checks passed.");
            else
                Debug.LogError($"Registry checks failed:\n- {string.Join("\n- ", failures)}");

            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }



        /// <summary>
        /// Registry files of a kind you are responsible for: in <c>Assets/</c> and in the packages you author.
        /// </summary>
        private static IEnumerable<string> OwnRegistryFiles(RegistryKind _Kind)
        {
            var roots = new List<string> { "Assets/" };
            roots.AddRange(AuthoredPackages().Select(e => e.assetPath.TrimEnd('/') + "/"));

            return AssetDatabase.GetAllAssetPaths()
                .Where(_Kind.IsRegistryPath)
                .Where(e => roots.Any(root => e.StartsWith(root, StringComparison.Ordinal)))
                .OrderBy(e => e, StringComparer.Ordinal);
        }

        private static IEnumerable<PackageInfo> AuthoredPackages()
            => PackageInfo.GetAllRegisteredPackages().Where(e => e.source == PackageSource.Embedded || e.source == PackageSource.Local);
    }
}
