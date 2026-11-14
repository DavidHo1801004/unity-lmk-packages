using UnityEngine;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// The user's markers as <see cref="TagMarkers"/> edits them, and the target of Undo for marker changes.
    /// </summary>
    /// <remarks>
    /// Never saved as an asset (<see cref="HideFlags.HideAndDontSave"/>). It survives domain reloads, so the
    /// undo history of marker changes outlives a recompile, but not an editor restart.
    /// </remarks>
    internal sealed class TagMarkerState : ScriptableObject
    {
        public TagMarkerSet set = new();

        /// <summary>
        /// The markers file <see cref="set"/> was loaded from and is written to.
        /// </summary>
        public string path = string.Empty;

        /// <summary>
        /// Unique per change. Undo and redo restore it, which tells whether an undo touched the markers.
        /// </summary>
        public int revision;



        public static TagMarkerState Find()
        {
            var states = Resources.FindObjectsOfTypeAll<TagMarkerState>();
            return states.Length > 0 ? states[0] : null;
        }

        public static TagMarkerState FindOrCreate()
        {
            var state = Find();
            if (state != null) return state;

            state = CreateInstance<TagMarkerState>();
            state.name = "Gameplay Tag Markers";
            state.hideFlags = HideFlags.HideAndDontSave;
            return state;
        }
    }
}
