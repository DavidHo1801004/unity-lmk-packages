using System.Collections.Generic;

using UnityEngine;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Staged content of the main registry and of the user's tag markers, and the target of Undo for tag
    /// operations. Operations change only this state. <see cref="TagOperations.Save"/> writes it to disk, and
    /// <see cref="TagOperations.Discard"/> reloads it from disk.
    /// </summary>
    /// <remarks>
    /// Never saved as an asset (<see cref="HideFlags.HideAndDontSave"/>). It survives domain reloads, so staged
    /// changes and their undo history outlive a recompile, but not an editor restart.
    /// </remarks>
    internal sealed class TagRegistryState : ScriptableObject
    {
        /// <summary>
        /// Path of the registry file this state belongs to.
        /// </summary>
        public string path = string.Empty;

        /// <summary>
        /// Staged content of the main registry. Empty while the project has no main registry.
        /// </summary>
        public TagRegistryData data = new();

        /// <summary>
        /// Staged markers of this user, for tags of every registry.
        /// </summary>
        public List<TagMarker> markers = new();

        /// <summary>
        /// Unique per change. Undo and redo restore it, which tells whether an undo touched this state.
        /// </summary>
        public int revision;



        public static TagRegistryState Find()
        {
            var states = Resources.FindObjectsOfTypeAll<TagRegistryState>();
            return states.Length > 0 ? states[0] : null;
        }

        public static TagRegistryState FindOrCreate()
        {
            var state = Find();
            if (state != null) return state;

            state = CreateInstance<TagRegistryState>();
            state.name = "Gameplay Tag Registry";
            state.hideFlags = HideFlags.HideAndDontSave;

            return state;
        }
    }
}
