using System.Collections.Generic;

namespace LMK.Editor.Registry.Tests
{
    /// <summary>
    /// A lock provider a project could add: public, with a parameterless constructor. Always grants the lock.
    /// </summary>
    public sealed class TestLockProvider : RegistryLockProvider
    {
        public override string Name => "Test Lock";

        public override bool IsHeld(IReadOnlyList<string> _Paths) => true;

        public override bool TryAcquire(IReadOnlyList<string> _Paths, out string _Error)
        {
            _Error = null;
            return true;
        }
    }
}
