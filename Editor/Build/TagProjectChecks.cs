using System.Collections.Generic;

using LMK.Editor.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// The CI checks that run inside Unity: registry integrity, build content and pack validation. Since 1.1.0 they
    /// are the registry core's <see cref="RegistryProjectChecks"/>, which check every registry kind, tags included.
    /// </summary>
    /// <remarks>
    /// Call the checks from your own tests, or run them from the command line:
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod LMK.Editor.GameplayTags.TagProjectChecks.RunFromCommandLine</c>
    /// (or <c>LMK.Editor.Registry.RegistryProjectChecks.RunFromCommandLine</c>). The editor exits with 1 if any check fails.
    /// </remarks>
    public static class TagProjectChecks
    {
        /// <summary>
        /// Every registry file in the project and in the packages you author parses, is consistent and has a valid
        /// checksum.
        /// </summary>
        /// <returns> One message per problem. </returns>
        public static List<string> CheckRegistryIntegrity() => RegistryProjectChecks.CheckRegistryIntegrity();

        /// <summary>
        /// The references going into a build resolve: in the enabled build scenes, and in Resources content.
        /// </summary>
        /// <param name="_Warnings"> Unresolved references, when the build policy only warns about them. </param>
        /// <returns> Unresolved references, when the build policy fails builds on them. </returns>
        public static List<string> CheckBuildContent(List<string> _Warnings = null) => RegistryProjectChecks.CheckBuildContent(_Warnings);

        /// <summary>
        /// The packages you author (embedded or local) pass pack validation.
        /// </summary>
        /// <returns> One message per problem, prefixed with the package name. </returns>
        public static List<string> CheckAuthoredPackages() => RegistryProjectChecks.CheckAuthoredPackages();

        /// <summary>
        /// Run every check, log the results, and exit the editor: 0 if every check passed, 1 otherwise.
        /// </summary>
        public static void RunFromCommandLine() => RegistryProjectChecks.RunFromCommandLine();
    }
}
