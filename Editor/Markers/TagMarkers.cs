using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

using LMK.Editor.Registry;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// The user's tag markers: named colours, defined once and assigned to tags of any registry, several per tag.
    /// They are personal (under <c>UserSettings/</c>, never committed) and need no lock. <br/>
    /// Every change is written to the file at once, and can be undone. Markers are not staged: <i>Save</i> and
    /// <i>Discard</i> in the Tag Editor don't affect them.
    /// </summary>
    [InitializeOnLoad]
    internal static class TagMarkers
    {
        /// <summary>
        /// Longest marker text.
        /// </summary>
        public const int MAX_TEXT_LENGTH = 32;

        private const string SESSION_REVISION = "LMK.GameplayTags.MarkerRevision";

        /// <summary>
        /// Called after the markers changed: an edit, an undo or a redo.
        /// </summary>
        public static event Action Changed;

        // Found again after a domain reload, or when it was destroyed.
        private static TagMarkerState cachedState;

        // The revision last written to the file. Unknown after a domain reload, which only costs one extra write.
        private static int writtenRevision = -1;

        // The markers of each tag, for one revision.
        private static int indexRevision = -1;
        private static Dictionary<(RegistryGuid, RegistryGuid), List<TagMarkerDefinition>> index;

        static TagMarkers()
        {
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }



        #region Reading
        /// <summary>
        /// Every marker, in the order rows show them. Never modify it.
        /// </summary>
        public static IReadOnlyList<TagMarkerDefinition> Definitions => State.set.definitions;

        public static bool TryGetDefinition(RegistryGuid _Marker, out TagMarkerDefinition _Definition)
        {
            var set = State.set;
            int index = set.IndexOfDefinition(_Marker);

            _Definition = index >= 0 ? set.definitions[index] : default;
            return index >= 0;
        }

        /// <summary>
        /// The markers of a tag, in definition order. Empty when it has none.
        /// </summary>
        public static IReadOnlyList<TagMarkerDefinition> GetMarkers(RegistryGuid _Registry, RegistryGuid _Tag)
        {
            var markers = Index();
            return markers.TryGetValue((_Registry, _Tag), out var list) ? list : (IReadOnlyList<TagMarkerDefinition>)Array.Empty<TagMarkerDefinition>();
        }

        public static bool HasMarker(RegistryGuid _Registry, RegistryGuid _Tag, RegistryGuid _Marker)
        {
            foreach (var marker in GetMarkers(_Registry, _Tag))
                if (marker.id.Equals(_Marker))
                    return true;

            return false;
        }

        /// <summary>
        /// How many tags have a marker.
        /// </summary>
        public static int CountUses(RegistryGuid _Marker)
        {
            int count = 0;
            foreach (var assignment in State.set.assignments)
                if (assignment.marker.Equals(_Marker))
                    count++;

            return count;
        }

        /// <summary>
        /// The texts of a tag's markers, one per line, for searching. <see langword="null"/> when it has none.
        /// </summary>
        public static string GetSearchText(RegistryGuid _Registry, RegistryGuid _Tag)
        {
            var markers = GetMarkers(_Registry, _Tag);
            if (markers.Count == 0) return null;

            var texts = new string[markers.Count];
            for (int i = 0; i < markers.Count; i++)
                texts[i] = markers[i].text;

            return string.Join("\n", texts);
        }
        #endregion



        #region Definitions
        /// <summary>
        /// Add a marker at the end of the list.
        /// </summary>
        /// <returns> The new marker's ID, or why the text can't be used. </returns>
        public static RegistryResult AddDefinition(string _Text, Color _Color)
        {
            string text = Clean(_Text);
            string error = CheckText(text, RegistryGuid.Zero);
            if (error != null) return RegistryResult.Fail(error);

            return Change("Add Gameplay Tag Marker", true, set =>
            {
                var id = NewUniqueId(set);
                set.definitions.Add(new TagMarkerDefinition(id, text, TagMarkerSet.Normalize(_Color)));
                return RegistryResult.Ok(id);
            });
        }

        /// <summary>
        /// Change a marker's text and colour. Every tag with the marker shows the change. Changes within one editor
        /// event, e.g. dragging in the colour picker, are one undo step.
        /// </summary>
        public static RegistryResult EditDefinition(RegistryGuid _Marker, string _Text, Color _Color)
        {
            string text = Clean(_Text);
            string error = CheckText(text, _Marker);
            if (error != null) return RegistryResult.Fail(error);

            return Change("Edit Gameplay Tag Marker", false, set =>
            {
                int index = set.IndexOfDefinition(_Marker);
                if (index < 0) return RegistryResult.Fail(UnknownMarker(_Marker));

                set.definitions[index] = new TagMarkerDefinition(_Marker, text, TagMarkerSet.Normalize(_Color));
                return RegistryResult.Ok(_Marker);
            });
        }

        /// <summary>
        /// Delete a marker, and remove it from every tag.
        /// </summary>
        public static RegistryResult DeleteDefinition(RegistryGuid _Marker)
        {
            return Change("Delete Gameplay Tag Marker", true, set =>
            {
                int index = set.IndexOfDefinition(_Marker);
                if (index < 0) return RegistryResult.Fail(UnknownMarker(_Marker));

                set.definitions.RemoveAt(index);
                set.assignments.RemoveAll(e => e.marker.Equals(_Marker));
                return RegistryResult.Ok(_Marker);
            });
        }

        /// <summary>
        /// Move a marker in the list, which is the order rows show markers in.
        /// </summary>
        /// <param name="_Index"> The marker's index after the move, clamped to the list. </param>
        public static RegistryResult MoveDefinition(RegistryGuid _Marker, int _Index)
        {
            return Change("Move Gameplay Tag Marker", true, set =>
            {
                int index = set.IndexOfDefinition(_Marker);
                if (index < 0) return RegistryResult.Fail(UnknownMarker(_Marker));

                var definition = set.definitions[index];
                set.definitions.RemoveAt(index);
                set.definitions.Insert(Mathf.Clamp(_Index, 0, set.definitions.Count), definition);
                return RegistryResult.Ok(_Marker);
            });
        }
        #endregion



        #region Assignments
        /// <summary>
        /// Give a tag a marker. Tags of any registry can have markers, read-only ones too.
        /// </summary>
        public static RegistryResult Assign(RegistryGuid _Registry, RegistryGuid _Tag, RegistryGuid _Marker)
        {
            if (_Registry.IsZero || _Tag.IsZero) return RegistryResult.Fail("Only tags can have markers.");

            return Change("Assign Gameplay Tag Marker", true, set =>
            {
                if (set.IndexOfDefinition(_Marker) < 0) return RegistryResult.Fail(UnknownMarker(_Marker));

                foreach (var assignment in set.assignments)
                    if (assignment.Is(_Registry, _Tag, _Marker))
                        return RegistryResult.Ok(_Marker);

                set.assignments.Add(new TagMarkerAssignment(_Registry, _Tag, _Marker));
                return RegistryResult.Ok(_Marker);
            });
        }

        /// <summary>
        /// Remove a marker from a tag. Nothing changes if the tag doesn't have it.
        /// </summary>
        public static RegistryResult Unassign(RegistryGuid _Registry, RegistryGuid _Tag, RegistryGuid _Marker)
        {
            return Change("Unassign Gameplay Tag Marker", true, set =>
            {
                set.assignments.RemoveAll(e => e.Is(_Registry, _Tag, _Marker));
                return RegistryResult.Ok(_Marker);
            });
        }
        #endregion



        #region State
        /// <summary>
        /// The markers being edited, loaded from <see cref="TagMarkerStore.FilePath"/> on first use, or when that
        /// path changed.
        /// </summary>
        private static TagMarkerState State
        {
            get
            {
                if (cachedState == null)
                    cachedState = TagMarkerState.FindOrCreate();

                if (cachedState.path != TagMarkerStore.FilePath)
                {
                    Undo.ClearUndo(cachedState);
                    cachedState.set = TagMarkerStore.Load();
                    cachedState.path = TagMarkerStore.FilePath;
                    cachedState.revision = NextRevision();
                    writtenRevision = cachedState.revision;
                }

                return cachedState;
            }
        }

        /// <summary>
        /// Forget the markers in memory and their undo history, so the next use loads the file again. For tests.
        /// </summary>
        internal static void Reload()
        {
            var state = TagMarkerState.Find();
            if (state != null)
            {
                Undo.ClearUndo(state);
                UnityEngine.Object.DestroyImmediate(state);
            }

            cachedState = null;
            writtenRevision = -1;
            indexRevision = -1;
            Changed?.Invoke();
        }

        /// <summary>
        /// Apply one change to a copy of the markers. A failed or empty change leaves everything as it was.
        /// Otherwise the change is recorded for Undo, written to the file, and announced.
        /// </summary>
        private static RegistryResult Change(string _UndoName, bool _NewUndoGroup, Func<TagMarkerSet, RegistryResult> _Change)
        {
            var state = State;
            var working = state.set.Clone();

            var result = _Change(working);
            if (!result.Success || working.ContentEquals(state.set)) return result;

            if (_NewUndoGroup)
                Undo.IncrementCurrentGroup();

            Undo.RecordObject(state, _UndoName);
            Undo.SetCurrentGroupName(_UndoName);

            state.set = working;
            state.revision = NextRevision();
            Undo.FlushUndoRecordObjects();

            Write(state);
            Changed?.Invoke();
            return result;
        }

        private static void OnUndoRedoPerformed()
        {
            var state = cachedState != null ? cachedState : TagMarkerState.Find();
            if (state == null || state.revision == writtenRevision || state.path != TagMarkerStore.FilePath) return;

            cachedState = state;
            Write(state);
            Changed?.Invoke();
        }

        private static void Write(TagMarkerState _State)
        {
            TagMarkerStore.Save(_State.path, _State.set);
            writtenRevision = _State.revision;
        }

        private static Dictionary<(RegistryGuid, RegistryGuid), List<TagMarkerDefinition>> Index()
        {
            var state = State;
            if (index != null && indexRevision == state.revision) return index;

            index = new Dictionary<(RegistryGuid, RegistryGuid), List<TagMarkerDefinition>>();
            indexRevision = state.revision;

            // In definition order, whatever the order of the assignments.
            var set = state.set;
            foreach (var definition in set.definitions)
            {
                foreach (var assignment in set.assignments)
                {
                    if (!assignment.marker.Equals(definition.id)) continue;

                    var key = (assignment.registry, assignment.tag);
                    if (!index.TryGetValue(key, out var list))
                        index.Add(key, list = new List<TagMarkerDefinition>());

                    list.Add(definition);
                }
            }

            return index;
        }

        private static int NextRevision()
        {
            int revision = SessionState.GetInt(SESSION_REVISION, 0) + 1;
            SessionState.SetInt(SESSION_REVISION, revision);
            return revision;
        }
        #endregion



        #region Helpers
        private static string Clean(string _Text) => (_Text ?? string.Empty).Trim();

        /// <summary>
        /// Why a text can't name a marker, or <see langword="null"/>.
        /// </summary>
        /// <param name="_Self"> The marker being renamed, which may keep its own text. Zero for a new marker. </param>
        internal static string CheckText(string _Text, RegistryGuid _Self)
        {
            if (string.IsNullOrEmpty(_Text)) return "A marker needs a name.";
            if (_Text.Length > MAX_TEXT_LENGTH) return $"A marker name can't be longer than {MAX_TEXT_LENGTH} characters.";

            foreach (var definition in State.set.definitions)
                if (!definition.id.Equals(_Self) && string.Equals(definition.text, _Text, StringComparison.OrdinalIgnoreCase))
                    return $"A marker is already named \"{definition.text}\".";

            return null;
        }

        private static string UnknownMarker(RegistryGuid _Marker) => $"There is no marker {_Marker}.";

        private static RegistryGuid NewUniqueId(TagMarkerSet _Set)
        {
            RegistryGuid id;
            do id = RegistryGuid.NewGuid();
            while (_Set.IndexOfDefinition(id) >= 0);

            return id;
        }
        #endregion
    }
}
