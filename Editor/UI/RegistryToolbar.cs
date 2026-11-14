using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Utilities;

using ToolbarButton = LMK.Editor.UIElements.ToolbarButton;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// The start of a registry window's toolbar: Save and Discard for every kind's staged edits, enabled while there
    /// are unsaved changes, and the lock provider in use. A window adds its own controls (search, add, options) after
    /// them with <see cref="AddControl"/>.
    /// </summary>
    public sealed class RegistryToolbar : VisualElement
    {
        public const string USS_PATH = "Packages/com.lmk.registry/Editor/Assets/RegistryUSS.uss";
        public const string USS_CLASS = "registry-toolbar";

        private readonly ToolbarButton saveButton;
        private readonly ToolbarButton discardButton;
        private readonly Label lockLabel;
        private readonly VisualElement controls;

        public ToolbarButton SaveButton => saveButton;
        public ToolbarButton DiscardButton => discardButton;
        public Label LockLabel => lockLabel;



        public RegistryToolbar()
        {
            name = "registry-toolbar";
            AddToClassList(USS_CLASS);

            var styleSheet = LMKAssetUtility.Load<StyleSheet>(USS_PATH);
            if (styleSheet != null)
                styleSheets.Add(styleSheet);

            saveButton = new ToolbarButton(false, () => RegistryCommands.SaveWithDialogs())
            {
                name = "save-button",
                text = "Save",
                tooltip = "Write every registry change and generate the code.",
            };
            Add(saveButton);

            discardButton = new ToolbarButton(false, RegistryCommands.DiscardWithDialog)
            {
                name = "discard-button",
                text = "Discard",
                tooltip = "Drop every unsaved registry change.",
            };
            Add(discardButton);

            controls = new VisualElement { name = "controls" };
            controls.AddToClassList(USS_CLASS + "__controls");
            Add(controls);

            lockLabel = new Label { name = "lock-label" };
            lockLabel.AddToClassList(USS_CLASS + "__lock");
            Add(lockLabel);

            RegisterCallback<AttachToPanelEvent>(_ => RegistrySession.StateChanged += Refresh);
            RegisterCallback<DetachFromPanelEvent>(_ => RegistrySession.StateChanged -= Refresh);

            Refresh();
        }

        /// <summary>
        /// Add a window's own control after Save and Discard.
        /// </summary>
        public void AddControl(VisualElement _Control) => controls.Add(_Control);

        /// <summary>
        /// Enable Save and Discard while there are unsaved changes, and show the lock provider.
        /// </summary>
        public void Refresh()
        {
            bool pending = RegistrySession.HasPendingChanges;
            saveButton.SetEnabled(pending);
            discardButton.SetEnabled(pending);

            // Only the provider's name: asking whether the lock is held may call a server.
            var provider = RegistryLockProvider.Current;
            bool locking = provider is not NoneLockProvider;
            lockLabel.text = locking ? $"Lock: {provider.Name}" : string.Empty;
            lockLabel.tooltip = locking
                ? $"Registry edits ask the {provider.Name} lock first. Change it in Project Settings > LMK > Registries."
                : string.Empty;
            lockLabel.style.display = locking ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
