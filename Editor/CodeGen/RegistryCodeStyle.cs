using System;
using System.Collections.Generic;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// How a kind's generated constants look. A kind that wants generated code returns one from
    /// <see cref="RegistryKind.CodeStyle"/>; the core does the rest: file layout, class and member names, clashes,
    /// keywords, rename aliases, deletes, and when the file is written.
    /// </summary>
    /// <remarks>
    /// The generated file has a root class (<see cref="RootClass"/>) with one nested class per registry: <c>Main</c>
    /// for the main registry, the registry name for the others. Each holds a <c>RegistryId</c> field and one constant
    /// per row.
    /// </remarks>
    public sealed class RegistryCodeStyle
    {
        /// <summary>
        /// Name of the generated file, e.g. "GameplayTags.g.cs". It is written in the kind's
        /// <c>&lt;RootFolder&gt;/Generated</c> folder.
        /// </summary>
        public string FileName;

        /// <summary>
        /// Name of the root class, e.g. "Tags".
        /// </summary>
        public string RootClass;

        /// <summary>
        /// Fully qualified type of a constant, e.g. "global::LMK.Runtime.GameplayTags.GameplayTag".
        /// </summary>
        public string MemberType;

        /// <summary>
        /// Fully qualified type of the <c>RegistryId</c> field and of the ID literals.
        /// </summary>
        public string GuidType = "global::LMK.Runtime.Registry.RegistryGuid";

        /// <summary>
        /// The base member name of a row, from its name, e.g. "Status.Debuff.Stun" → "Status_Debuff_Stun". The core
        /// replaces characters that are not letters, digits or '_', and resolves clashes.
        /// </summary>
        public Func<string, string> MemberName = _Name => _Name.Replace('.', '_');

        /// <summary>
        /// The initializer of a constant, from the registry field name, the ID literal and the row name. It must contain
        /// "<c>registryField, idLiteral</c>" in that order, which is how the next generation finds the constants of the
        /// previous one, e.g. <c>GameplayTag.FromId(RegistryId, new RegistryGuid(…), "Status.Stun")</c>.
        /// </summary>
        public Func<string, string, string, string> Initializer;

        /// <summary>
        /// Comment lines at the top of the file, without the "//".
        /// </summary>
        public IReadOnlyList<string> Header = Array.Empty<string>();

        /// <summary>
        /// XML summary of the root class.
        /// </summary>
        public string RootSummary = string.Empty;

        /// <summary>
        /// Noun for the rows in the summary of a registry's class, e.g. "Tags" → "Tags of the "Main" registry.".
        /// </summary>
        public string RegistrySummaryNoun = "Rows";

        /// <summary>
        /// Comment lines above the rename aliases of a registry's class, without the "//".
        /// </summary>
        public IReadOnlyList<string> AliasComment = new[] { "Old names of renamed rows. Prune Unused Code Aliases removes the ones no script uses." };

        /// <summary>
        /// Namespace of the root class when the project sets none. Empty for the global namespace.
        /// </summary>
        public string DefaultNamespace = string.Empty;

        /// <summary>
        /// Assemblies a generated assembly definition references, when the project puts the code in its own assembly:
        /// the kind's runtime assembly and <c>LMK.Registry.Runtime</c>.
        /// </summary>
        public IReadOnlyList<string> AssemblyReferences = Array.Empty<string>();
    }
}
