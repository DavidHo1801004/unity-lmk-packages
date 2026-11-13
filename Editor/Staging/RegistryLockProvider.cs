using System.Collections.Generic;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Exclusive lock on the files registry operations write: main registries, generated package registries
    /// and generated code, of every kind. Every staged operation, save, generation and code write asks it first,
    /// with the full list of files it would write.
    /// </summary>
    /// <remarks>
    /// Projects can add providers, e.g. for Perforce exclusive checkout, Unity Version Control locks or Git LFS
    /// locks. Those call a server, so callers ask once per operation, never per frame. <br/>
    /// A provider needs a public parameterless constructor to be listed in Project Settings.
    /// </remarks>
    public abstract class RegistryLockProvider
    {
        private static RegistryLockProvider current = new NoneLockProvider();

        /// <summary>
        /// The provider every registry operation asks. Never <see langword="null"/>: setting it to
        /// <see langword="null"/> restores <see cref="NoneLockProvider"/>.
        /// </summary>
        public static RegistryLockProvider Current
        {
            get => current;
            set => current = value ?? new NoneLockProvider();
        }

        /// <summary>
        /// Display name, e.g. "None" or "Perforce".
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Whether this user currently holds the lock on every path.
        /// </summary>
        public abstract bool IsHeld(IReadOnlyList<string> _Paths);

        /// <summary>
        /// Acquire the lock on every path, or keep it if it's already held.
        /// </summary>
        /// <param name="_Paths"> Project-relative paths of every file the operation would write. </param>
        /// <param name="_Error"> Why the lock is not available, e.g. who holds it. </param>
        public abstract bool TryAcquire(IReadOnlyList<string> _Paths, out string _Error);

        /// <summary>
        /// The message of a refused operation.
        /// </summary>
        internal static string RefusedMessage(string _LockError)
            => $"The registry lock ({Current.Name}) is not available. {_LockError}".TrimEnd();
    }



    /// <summary>
    /// No locking: every request succeeds. For solo projects, or teams that serialize registry edits another way.
    /// </summary>
    public sealed class NoneLockProvider : RegistryLockProvider
    {
        public override string Name => "None";

        public override bool IsHeld(IReadOnlyList<string> _Paths) => true;

        public override bool TryAcquire(IReadOnlyList<string> _Paths, out string _Error)
        {
            _Error = null;
            return true;
        }
    }
}
