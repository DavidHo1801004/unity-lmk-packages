using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Outcome of a registry operation: a staged edit, a save, or a command.
    /// </summary>
    public readonly struct RegistryResult
    {
        public readonly bool Success;

        /// <summary>
        /// Why the operation was refused. <see langword="null"/> on success.
        /// </summary>
        public readonly string Error;

        /// <summary>
        /// The row the operation created or changed, if any.
        /// </summary>
        public readonly RegistryGuid Id;

        private RegistryResult(bool _Success, string _Error, RegistryGuid _Id)
        {
            Success = _Success;
            Error = _Error;
            Id = _Id;
        }

        public static RegistryResult Ok(RegistryGuid _Id = default) => new(true, null, _Id);

        public static RegistryResult Fail(string _Error) => new(false, _Error, RegistryGuid.Zero);

        public override string ToString() => Success ? "Ok" : $"Failed: {Error}";
    }
}
