using System.Collections.Generic;
using System.Linq;

using UnityEditor;

using UnityEngine;

using LMK.Editor.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// The CI checks of the ability system that run inside Unity: the registry core's checks, which cover attribute
    /// registries and the attribute references going into a build, plus the validation of every effect and ability asset.
    /// </summary>
    /// <remarks>
    /// Call the checks from your own tests, or run them from the command line:
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod LMK.Editor.AbilitySystem.AbilitySystemProjectChecks.RunFromCommandLine</c>.
    /// The editor exits with 1 if any check fails.
    /// </remarks>
    public static class AbilitySystemProjectChecks
    {
        /// <summary>
        /// Every registry file of every kind, attribute registries included, parses, is consistent and has a valid checksum.
        /// </summary>
        public static List<string> CheckRegistryIntegrity() => RegistryProjectChecks.CheckRegistryIntegrity();

        /// <summary>
        /// The references going into a build resolve, attributes included: in the enabled build scenes, and in Resources content.
        /// </summary>
        public static List<string> CheckBuildContent(List<string> _Warnings = null) => RegistryProjectChecks.CheckBuildContent(_Warnings);

        /// <summary>
        /// The packages you author pass pack validation, attribute registries included.
        /// </summary>
        public static List<string> CheckAuthoredPackages() => RegistryProjectChecks.CheckAuthoredPackages();

        /// <summary>
        /// Every effect and ability asset passes <c>EffectValidator</c> and <c>AbilityValidator</c>.
        /// </summary>
        /// <param name="_Warnings"> The validators' warnings. </param>
        /// <returns> The validators' errors. </returns>
        public static List<string> CheckEffectsAndAbilities(List<string> _Warnings = null)
        {
            var problems = AbilityAssetValidation.ValidateAll();
            _Warnings?.AddRange(problems.Where(e => !e.IsError).Select(e => e.ToString()));
            return problems.Where(e => e.IsError).Select(e => e.ToString()).ToList();
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
            failures.AddRange(CheckEffectsAndAbilities(warnings));

            foreach (string warning in warnings)
                Debug.LogWarning($"Ability system check: {warning}");

            if (failures.Count == 0)
                Debug.Log("Ability system checks passed.");
            else
                Debug.LogError($"Ability system checks failed:\n- {string.Join("\n- ", failures)}");

            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }
    }
}
