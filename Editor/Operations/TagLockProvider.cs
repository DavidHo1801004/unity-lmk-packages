using System.Collections.Generic;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Exclusive lock on the files tag operations write: the main registry, generated package registries
    /// and generated code. Every write checks it first.
    /// </summary>
    /// <remarks>
    /// Implementations can wrap Perforce exclusive checkout, Unity Version Control locks or Git LFS locks.
    /// Those call a server, so callers check once per operation, never per frame.
    /// </remarks>
    internal abstract class TagLockProvider
    {
        private static TagLockProvider current = new NoneLockProvider();

        /// <summary>
        /// The provider used by <see cref="TagOperations"/>.
        /// </summary>
        public static TagLockProvider Current
        {
            get => current;
            internal set => current = value ?? new NoneLockProvider();
        }

        public abstract string Name { get; }

        /// <summary>
        /// Whether this user currently holds the lock on every path.
        /// </summary>
        public abstract bool IsHeld(IReadOnlyList<string> _Paths);

        /// <summary>
        /// Acquire the lock on every path, or keep it if it's already held.
        /// </summary>
        /// <param name="_Error"> Why the lock is not available, e.g. who holds it. </param>
        public abstract bool TryAcquire(IReadOnlyList<string> _Paths, out string _Error);
    }



    /// <summary>
    /// No locking: every request succeeds. For solo projects, or teams that serialize tag edits another way.
    /// </summary>
    internal sealed class NoneLockProvider : TagLockProvider
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
