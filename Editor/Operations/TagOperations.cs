using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// The tag operations on the main registry: Add, Make Explicit, Rename, Delete and Edit description, and the
    /// user's tag markers. <br/>
    /// Every edit is staged through the registry core (<see cref="RegistryEdit.Apply{TRow, TAsset}"/>): it checks
    /// the lock, validates a copy, records Undo and changes only the staged state. The registries are reloaded with
    /// the staged content, so every tag view shows it. <see cref="Save"/> writes it to disk, and <see cref="Discard"/>
    /// drops it. Package and generated registries are read-only here.
    /// </summary>
    /// <remarks>
    /// Undo and redo only change the staged state. If the file changes on disk while edits are staged (a pull, a
    /// revert), the edits are kept and <see cref="Save"/> is refused until they are discarded, so staged content
    /// never overwrites newer content. Without staged edits, the state follows the file and its undo history
    /// is dropped.
    /// </remarks>
    internal static class TagOperations
    {
        private const string NOT_IN_MAIN = "This tag is not in the main registry. Package and generated registries are read-only.";

        private static TagKind Kind => TagKind.Instance;

        /// <summary>
        /// Called after the staged state changed: an edit, an undo or redo, a save, a discard, or a reload from disk.
        /// </summary>
        public static event Action StateChanged
        {
            add => Kind.StateChanged += value;
            remove => Kind.StateChanged -= value;
        }

        /// <summary>
        /// Called after a marker changed. Marker edits don't reload the registries.
        /// </summary>
        public static event Action MarkersChanged
        {
            add => Kind.MarkersChanged += value;
            remove => Kind.MarkersChanged -= value;
        }

        internal static IReadOnlyList<string> LockedPaths => Kind.LockedPaths;



        #region Pending Changes
        /// <summary>
        /// Whether there are staged tag edits that are not saved: registry content or markers.
        /// </summary>
        public static bool HasPendingChanges => Kind.HasPendingChanges;

        /// <summary>
        /// Whether the main registry file changed on disk since the staged registry content was loaded or saved,
        /// while that content has unsaved edits. <see cref="Save"/> is refused then.
        /// </summary>
        public static bool DiskChangedWhilePending => Kind.DiskChangedWhilePending;

        /// <summary>
        /// The staged main registry content, if it differs from the saved content. Never modify it.
        /// </summary>
        internal static bool TryGetPendingData(out RegistryData<TagRegistryRow> _Data) => Kind.TryGetPendingData(out _Data);
        #endregion



        #region Save And Discard
        /// <summary>
        /// Write the staged edits of every registry kind: each changed main registry, the markers, then the
        /// generated code. Refused if a file changed on disk since the edits began, or without the lock.
        /// </summary>
        public static RegistryResult Save() => RegistrySession.Save();

        /// <summary>
        /// Drop the staged edits: load the main registries and the markers from disk again, and clear the undo
        /// history.
        /// </summary>
        public static void Discard() => RegistrySession.Discard();
        #endregion



        #region Operations
        /// <summary>
        /// Create an empty main registry with a new registry ID. Written immediately, since there is no file to
        /// stage against. Not undoable.
        /// </summary>
        public static RegistryResult CreateMainRegistry() => Kind.CreateMainRegistry();

        /// <summary>
        /// Add a tag, creating its missing parents as implicit tags. Adding a path that exists as an
        /// implicit tag makes it explicit and keeps its ID.
        /// </summary>
        public static RegistryResult Add(string _Path, string _Description = null)
        {
            if (!TagPath.IsValidPath(_Path, out string error))
                return RegistryResult.Fail(error);

            return RegistryEdit.Apply(Kind, "Add Gameplay Tag", data =>
            {
                var id = AddPath(data, _Path, _Description, out var outcome);

                return outcome == AddPathOutcome.ExistedExplicit
                    ? RegistryResult.Fail($"Tag \"{_Path}\" already exists.")
                    : RegistryResult.Ok(id);
            });
        }

        /// <summary>
        /// Make an implicit tag explicit, keeping its ID and description.
        /// </summary>
        public static RegistryResult MakeExplicit(RegistryGuid _Id)
        {
            return RegistryEdit.Apply(Kind, "Make Gameplay Tag Explicit", data =>
            {
                int index = data.IndexOf(_Id);
                if (index < 0) return RegistryResult.Fail(NOT_IN_MAIN);

                var row = data.rows[index];
                if (!row.data.isImplicit) return RegistryResult.Fail("The tag is already explicit.");

                row.data.isImplicit = false;
                data.rows[index] = row;
                return RegistryResult.Ok(_Id);
            });
        }

        /// <summary>
        /// Import tag file entries into the main registry as one operation, with one Undo step.
        /// </summary>
        /// <param name="_Remove"> Tags to delete with their subtrees, i.e. the tags not in the files. </param>
        public static RegistryResult ImportTagFiles(IReadOnlyList<TagFileEntry> _Entries, bool _OverwriteDescriptions, IReadOnlyCollection<RegistryGuid> _Remove)
        {
            foreach (var entry in _Entries)
                if (!TagPath.IsValidPath(entry.Path, out string error))
                    return RegistryResult.Fail(error);

            return RegistryEdit.Apply(Kind, "Import Gameplay Tag Files", data =>
            {
                TagFileImport.ApplyTo(data, _Entries, _OverwriteDescriptions, _Remove);
                return RegistryResult.Ok();
            });
        }

        /// <summary>
        /// Add the tags that assets imported from another project need, as one operation with one Undo
        /// step. Each tag keeps the ID its references use, if the main registry doesn't use it yet. An existing
        /// path is kept as it is.
        /// </summary>
        /// <param name="_Ids"> The ID of each path, as added or found. </param>
        public static RegistryResult AddWithIds(IReadOnlyList<(string Path, RegistryGuid Id)> _Tags, out Dictionary<string, RegistryGuid> _Ids)
        {
            var ids = new Dictionary<string, RegistryGuid>(StringComparer.Ordinal);
            _Ids = ids;

            foreach (var tag in _Tags)
                if (!TagPath.IsValidPath(tag.Path, out string error))
                    return RegistryResult.Fail(error);

            return RegistryEdit.Apply(Kind, "Add Imported Gameplay Tags", data =>
            {
                // Parents first, so a parent that is referenced too keeps its ID.
                foreach (var tag in _Tags.OrderBy(e => e.Path.Count(c => c == TagPath.SEPARATOR)).ThenBy(e => e.Path, StringComparer.Ordinal))
                    ids[tag.Path] = AddPath(data, tag.Path, null, out _, tag.Id);

                return RegistryResult.Ok();
            });
        }

        /// <summary>
        /// Rename a tag's last segment. Descendants and references follow, because they hold IDs.
        /// </summary>
        public static RegistryResult Rename(RegistryGuid _Id, string _NewSegment)
        {
            if (!TagPath.IsValidSegment(_NewSegment, out string error))
                return RegistryResult.Fail(error);

            return RegistryEdit.Apply(Kind, "Rename Gameplay Tag", data =>
            {
                int index = data.IndexOf(_Id);
                if (index < 0) return RegistryResult.Fail(NOT_IN_MAIN);

                var row = data.rows[index];
                if (row.data.segment == _NewSegment) return RegistryResult.Ok(_Id);

                if (FindChild(data, row.data.parent, _NewSegment) >= 0)
                    return RegistryResult.Fail($"A sibling tag is already named \"{_NewSegment}\".");

                row.data.segment = _NewSegment;
                data.rows[index] = row;

                return RegistryResult.Ok(_Id);
            });
        }

        /// <summary>
        /// Delete a tag and all of its descendants.
        /// </summary>
        /// <param name="_RemoveEmptyParents">
        /// If true, implicit ancestors left without children are removed too. Otherwise the parent left empty
        /// stays, with its ID, as an explicit tag.
        /// </param>
        public static RegistryResult Delete(RegistryGuid _Id, bool _RemoveEmptyParents)
        {
            return RegistryEdit.Apply(Kind, "Delete Gameplay Tag", data =>
            {
                int index = data.IndexOf(_Id);
                if (index < 0) return RegistryResult.Fail(NOT_IN_MAIN);

                var parent = data.rows[index].data.parent;
                var subtree = CollectSubtree(data, _Id);
                data.rows.RemoveAll(e => subtree.Contains(e.id));

                if (_RemoveEmptyParents)
                {
                    while (!parent.IsZero)
                    {
                        int parentIndex = data.IndexOf(parent);
                        var parentRow = data.rows[parentIndex];
                        if (!parentRow.data.isImplicit || HasChildren(data, parent)) break;

                        data.rows.RemoveAt(parentIndex);
                        parent = parentRow.data.parent;
                    }
                }
                else if (!parent.IsZero && !HasChildren(data, parent))
                {
                    int parentIndex = data.IndexOf(parent);
                    var parentRow = data.rows[parentIndex];
                    parentRow.data.isImplicit = false;
                    data.rows[parentIndex] = parentRow;
                }

                return RegistryResult.Ok(_Id);
            });
        }

        /// <summary>
        /// Change a tag's description.
        /// </summary>
        public static RegistryResult EditDescription(RegistryGuid _Id, string _Description)
        {
            return RegistryEdit.Apply(Kind, "Edit Gameplay Tag Description", data =>
            {
                int index = data.IndexOf(_Id);
                if (index < 0) return RegistryResult.Fail(NOT_IN_MAIN);

                var row = data.rows[index];
                row.data.description = _Description ?? string.Empty;
                data.rows[index] = row;

                return RegistryResult.Ok(_Id);
            });
        }

        /// <summary>
        /// Paths of the implicit ancestors that deleting a tag would leave without children.
        /// </summary>
        public static IReadOnlyList<string> GetParentsLeftEmpty(RegistryGuid _Id)
        {
            var result = new List<string>();
            var main = TagRegistryService.Snapshot.Main;
            if (main == null || main.AssetPath != Kind.MainRegistryPath || !main.TryGetRow(_Id, out var row)) return result;

            var removed = _Id;
            for (var parent = row.parent; !parent.IsZero; parent = row.parent)
            {
                if (!main.TryGetRow(parent, out row) || !row.isImplicit) break;

                var children = main.GetChildren(parent);
                if (children.Count != 1 || !children[0].Equals(removed)) break;

                result.Add(main.GetPath(parent));
                removed = parent;
            }

            return result;
        }
        #endregion



        #region Markers
        /// <summary>
        /// The markers to show: staged ones, or the saved ones before anything was staged.
        /// </summary>
        public static IReadOnlyList<RegistryMarker> Markers => Kind.Markers;

        /// <summary>
        /// The marker colour of a tag, if it has one.
        /// </summary>
        public static bool TryGetMarker(RegistryGuid _Registry, RegistryGuid _Tag, out Color _Color) => Kind.TryGetMarker(_Registry, _Tag, out _Color);

        /// <summary>
        /// Stage a tag's marker colour, for a tag of any registry. A fully transparent colour removes the marker.
        /// Markers are per user, so no lock is needed.
        /// </summary>
        public static void SetMarker(RegistryGuid _Registry, RegistryGuid _Tag, Color _Color) => Kind.SetMarker(_Registry, _Tag, _Color);
        #endregion



        #region Helpers
        /// <summary>
        /// What <see cref="AddPath"/> did with the last segment of the path.
        /// </summary>
        internal enum AddPathOutcome
        {
            /// <summary> A new row was created. </summary>
            Created,

            /// <summary> An implicit row was made explicit, keeping its ID. </summary>
            MadeExplicit,

            /// <summary> An explicit row already existed. Nothing was changed. </summary>
            ExistedExplicit,
        }

        /// <summary>
        /// Add a path to registry content, creating missing parents as implicit rows. The path must be valid.
        /// </summary>
        /// <param name="_Description"> Description of a created or newly explicit row. <see langword="null"/> keeps the current one. </param>
        /// <param name="_PreferredId">
        /// ID for a created row at <paramref name="_Path"/>, if no row uses it yet. Otherwise a new ID. To keep the
        /// IDs of parents too, add shorter paths first: a parent created implicitly gets a new ID.
        /// </param>
        /// <returns> ID of the row at <paramref name="_Path"/>. </returns>
        internal static RegistryGuid AddPath(RegistryData<TagRegistryRow> _Data, string _Path, string _Description, out AddPathOutcome _Outcome, RegistryGuid _PreferredId = default)
        {
            string[] segments = _Path.Split(TagPath.SEPARATOR);
            var parent = RegistryGuid.Zero;
            _Outcome = AddPathOutcome.Created;

            for (int i = 0; i < segments.Length; i++)
            {
                bool isLast = i == segments.Length - 1;
                int index = FindChild(_Data, parent, segments[i]);

                if (index >= 0)
                {
                    var row = _Data.rows[index];
                    if (isLast)
                    {
                        if (!row.data.isImplicit)
                        {
                            _Outcome = AddPathOutcome.ExistedExplicit;
                            return row.id;
                        }

                        row.data.isImplicit = false;
                        if (_Description != null)
                            row.data.description = _Description;

                        _Data.rows[index] = row;
                        _Outcome = AddPathOutcome.MadeExplicit;
                    }

                    parent = row.id;
                    continue;
                }

                var id = isLast && !_PreferredId.IsZero && _Data.IndexOf(_PreferredId) < 0 ? _PreferredId : NewUniqueId(_Data);
                _Data.rows.Add(TagRegistryRow.Create(id, parent, segments[i], !isLast, isLast ? _Description : null));
                parent = id;
            }

            return parent;
        }

        /// <summary>
        /// Remove a row and all of its descendants.
        /// </summary>
        internal static void RemoveSubtree(RegistryData<TagRegistryRow> _Data, RegistryGuid _Root)
        {
            if (_Data.IndexOf(_Root) < 0) return;

            var subtree = CollectSubtree(_Data, _Root);
            _Data.rows.RemoveAll(e => subtree.Contains(e.id));
        }

        private static int FindChild(RegistryData<TagRegistryRow> _Data, RegistryGuid _Parent, string _Segment)
        {
            for (int i = 0; i < _Data.rows.Count; i++)
                if (_Data.rows[i].data.parent.Equals(_Parent) && _Data.rows[i].data.segment == _Segment)
                    return i;

            return -1;
        }

        private static bool HasChildren(RegistryData<TagRegistryRow> _Data, RegistryGuid _Parent)
        {
            foreach (var row in _Data.rows)
                if (row.data.parent.Equals(_Parent))
                    return true;

            return false;
        }

        private static HashSet<RegistryGuid> CollectSubtree(RegistryData<TagRegistryRow> _Data, RegistryGuid _Root)
        {
            var result = new HashSet<RegistryGuid> { _Root };

            bool added = true;
            while (added)
            {
                added = false;
                foreach (var row in _Data.rows)
                    if (result.Contains(row.data.parent) && result.Add(row.id))
                        added = true;
            }

            return result;
        }

        private static RegistryGuid NewUniqueId(RegistryData<TagRegistryRow> _Data)
        {
            RegistryGuid id;
            do id = RegistryGuid.NewGuid();
            while (_Data.IndexOf(id) >= 0);

            return id;
        }
        #endregion
    }
}
