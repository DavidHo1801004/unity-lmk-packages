namespace LMK.Runtime.Registry
{
    /// <summary>
    /// Where a registry comes from: the <c>kind</c> field of a registry file's header.
    /// </summary>
    public enum RegistryOrigin
    {
        /// <summary>
        /// The project's main registry of a kind. One per project, edited in the editor.
        /// </summary>
        Main,

        /// <summary>
        /// A registry shipped inside a package. Read-only.
        /// </summary>
        Package,

        /// <summary>
        /// A registry generated in the project for a package that ships none.
        /// </summary>
        Generated,
    }



    /// <summary>
    /// Identity and display data of one registry in a database.
    /// </summary>
    public readonly struct RegistryInfo
    {
        public readonly RegistryGuid Id;
        public readonly string Name;
        public readonly RegistryOrigin Origin;

        public RegistryInfo(RegistryGuid _Id, string _Name, RegistryOrigin _Origin)
        {
            Id = _Id;
            Name = _Name ?? string.Empty;
            Origin = _Origin;
        }
    }
}
