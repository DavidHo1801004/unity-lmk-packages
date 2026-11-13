using System.Collections.Generic;

using LMK.Editor.Registry;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Checks that a package you author can be packed, for its tags: the tag part of the registry core's
    /// <see cref="RegistryPackageService.Validate(RegistryPackage)"/>. The menu commands are the core's.
    /// </summary>
    /// <remarks>
    /// The package must ship exactly one valid tag registry of kind <c>package</c>, its assets must reference only that
    /// registry or the registries of its declared dependencies, never the authoring project's main registry, every
    /// referenced tag must exist, and the registry ID must be the one it was packed with before. The package's
    /// <c>Tests</c> folder is skipped.
    /// </remarks>
    internal static class TagPackValidation
    {
        /// <summary>
        /// Every reason the package can't be packed, for its tags. Empty if it can.
        /// </summary>
        /// <param name="_PublishedRegistryId"> The registry ID the package was packed with before, or zero. </param>
        public static List<string> Validate(RegistryPackage _Package, RegistryGuid _PublishedRegistryId)
            => RegistryPackageService.Validate(_Package, TagKind.Instance, _PublishedRegistryId);
    }
}
