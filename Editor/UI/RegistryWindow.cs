using System.Linq;

using UnityEditor;
using UnityEngine;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Base of a registry editor window, e.g. the Gameplay Tag Manager. It follows the shared staging session:
    /// Unity asks to save or discard every kind's unsaved edits when such a window closes, generated code is written
    /// when it loses focus, and quitting leaves the question to the window.
    /// </summary>
    /// <remarks>
    /// Derived windows that override <c>OnEnable</c>, <c>OnDisable</c> or <c>OnLostFocus</c> call the base method.
    /// </remarks>
    [InitializeOnLoad]
    public abstract class RegistryWindow : EditorWindow
    {
        static RegistryWindow()
        {
            // Unity asks through the open window itself, since it has unsaved changes.
            RegistrySession.QuitPromptSuppressors.Add(() => Resources.FindObjectsOfTypeAll<RegistryWindow>().Length > 0);
        }

        protected virtual void OnEnable()
        {
            RegistrySession.StateChanged += OnRegistryStateChanged;
            OnRegistryStateChanged();
        }

        protected virtual void OnDisable()
        {
            RegistrySession.StateChanged -= OnRegistryStateChanged;
            RegistryCodeGen.FlushAll();
        }

        // Generated code is written once the user is done editing here, so a burst of changes costs one recompile.
        protected virtual void OnLostFocus() => RegistryCodeGen.FlushAll();

        private void OnRegistryStateChanged()
        {
            hasUnsavedChanges = RegistrySession.HasPendingChanges;
            if (hasUnsavedChanges)
            {
                string names = string.Join(" and ", RegistrySession.PendingKinds.Select(e => e.DisplayName));
                saveChangesMessage = $"{names} have unsaved changes. Save them?";
            }

            OnStateChanged();
        }

        /// <summary>
        /// Called after the staged state of any kind changed.
        /// </summary>
        protected virtual void OnStateChanged() { }

        // Unity asks before closing the window or quitting while hasUnsavedChanges is set.
        public override void SaveChanges()
        {
            if (RegistryCommands.SaveWithDialogs())
                base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            RegistrySession.Discard();
            base.DiscardChanges();
        }
    }
}
