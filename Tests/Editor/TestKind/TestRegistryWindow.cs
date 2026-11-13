namespace LMK.Editor.Registry.Tests
{
    /// <summary>
    /// A registry window with nothing in it, to test what every registry window shares. In its own file so it has a
    /// script asset.
    /// </summary>
    public sealed class TestRegistryWindow : RegistryWindow
    {
        public int stateChanges;

        protected override void OnStateChanged() => stateChanges++;
    }
}
