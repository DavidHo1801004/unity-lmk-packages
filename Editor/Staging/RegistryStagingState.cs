using System.Collections.Generic;

using UnityEngine;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Staged content of one kind's main registry and of the user's markers for that kind, and the target of
    /// Undo for the kind's operations. Operations change only this state. <see cref="RegistrySession.Save"/>
    /// writes it to disk, and <see cref="RegistrySession.Discard"/> reloads it from disk.
    /// </summary>
    /// <remarks>
    /// One object per kind, so reloading one kind from disk clears only that kind's undo history. All of them
    /// share Unity's undo stack, so edits of different kinds undo in the order they were made. <br/>
    /// The registry is stored as the text of its file: the format the core already reads and writes, whatever
    /// the kind's row type. <br/>
    /// Never saved as an asset (<see cref="HideFlags.HideAndDontSave"/>). It survives domain reloads, so staged
    /// changes and their undo history outlive a recompile, but not an editor restart.
    /// </remarks>
    internal sealed class RegistryStagingState : ScriptableObject
    {
        /// <summary>
        /// <see cref="RegistryKind.Id"/> of the kind this state belongs to.
        /// </summary>
        public string kind = string.Empty;

        /// <summary>
        /// Path of the main registry file the staged content belongs to.
        /// </summary>
        public string path = string.Empty;

        /// <summary>
        /// Staged content of the main registry, as its file text. Empty while the kind has no main registry.
        /// </summary>
        public string text = string.Empty;

        /// <summary>
        /// Staged markers of this user, for rows of every registry of the kind.
        /// </summary>
        public List<RegistryMarker> markers = new();

        /// <summary>
        /// Unique per change. Undo and redo restore it, which tells whether an undo touched this state.
        /// </summary>
        public int revision;



        public static RegistryStagingState Find(string _Kind)
        {
            foreach (var state in Resources.FindObjectsOfTypeAll<RegistryStagingState>())
                if (state.kind == _Kind)
                    return state;

            return null;
        }

        public static RegistryStagingState FindOrCreate(string _Kind)
        {
            var state = Find(_Kind);
            if (state != null) return state;

            state = CreateInstance<RegistryStagingState>();
            state.name = $"Registry Staging ({_Kind})";
            state.hideFlags = HideFlags.HideAndDontSave;
            state.kind = _Kind;

            return state;
        }
    }
}
