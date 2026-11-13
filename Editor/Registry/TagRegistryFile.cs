using System.Collections.Generic;

using LMK.Editor.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Reads and writes registry files (<c>*.tagregistry</c>, format v3), through the registry core.
    /// </summary>
    /// <remarks>
    /// <code>
    /// # gameplaytag-registry v3 — written by the Gameplay Tag Manager. Do not edit by hand.
    /// registry  6b1f0c2a9d7e4b3f8a5c1d0e2f3a4b5c
    /// name      Main
    /// kind      main
    /// package   com.vendor.combat   (generated registries only)
    /// version   1.2.0               (generated registries only)
    /// checksum  sha256:…
    /// # guid                             | parent                           | segment | flags    | description
    /// 0a91c3e27f4b4d0c9e1f55b2c8d7a610 | -                                | Ability | implicit |
    /// </code>
    /// Rows are sorted by GUID. The checksum is the SHA-256 of the row lines, each with trailing whitespace
    /// trimmed, joined with LF and ending with LF. CR characters are ignored, so a CRLF working copy still matches.
    /// </remarks>
    internal static class TagRegistryFile
    {
        public const string EXTENSION = TagKind.EXTENSION;

        public const string FORMAT_LINE = TagKind.FORMAT_LINE;

        /// <summary>
        /// Parse and validate the text of a registry file.
        /// </summary>
        public static RegistryParseResult<TagRegistryRow> Parse(string _Text) => RegistryFile.Parse(_Text, TagKind.Instance);

        /// <summary>
        /// Check the integrity of registry content: unique IDs, valid segments, existing parents, no cycles,
        /// unique segments among siblings.
        /// </summary>
        public static void Validate(RegistryData<TagRegistryRow> _Data, List<string> _Errors) => RegistryFile.Validate(_Data, TagKind.Instance, _Errors);

        /// <summary>
        /// Write registry content in the file format: rows sorted by GUID, aligned columns, LF line endings,
        /// and a fresh checksum.
        /// </summary>
        public static string Write(RegistryData<TagRegistryRow> _Data) => RegistryFile.Write(_Data, TagKind.Instance);

        /// <summary>
        /// Hash of a whole file, ignoring CR characters. Used to detect changes made outside the manager.
        /// </summary>
        public static string HashText(string _Text) => RegistryFile.HashText(_Text);
    }
}
