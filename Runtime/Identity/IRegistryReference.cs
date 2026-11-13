namespace LMK.Runtime.Registry
{
    /// <summary>
    /// A serialized reference to one registry row, such as a gameplay tag or a gameplay attribute. <br/>
    /// Its identity is (<see cref="RegistryId"/>, <see cref="Id"/>), so it survives renames.
    /// </summary>
    /// <remarks>
    /// Implementations keep three serialized fields, <c>_registry</c>, <c>_id</c> and <c>_lastKnownName</c>,
    /// whose layout is frozen: renaming or reordering them breaks every saved reference.
    /// </remarks>
    public interface IRegistryReference
    {
        /// <summary>
        /// ID of the registry the row belongs to. Always explicit: zero never means "the main registry".
        /// </summary>
        RegistryGuid RegistryId { get; }

        /// <summary>
        /// ID of the row in its registry. Zero for an unassigned reference.
        /// </summary>
        RegistryGuid Id { get; }

        /// <summary>
        /// The row's name when the reference was assigned. Display, diagnostics and explicit commands only:
        /// it never affects resolution.
        /// </summary>
        string LastKnownName { get; }
    }
}
