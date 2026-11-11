using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Options of <see cref="TagFileImport.Import"/>.
    /// </summary>
    internal sealed class TagFileImportOptions
    {
        /// <summary>
        /// Replace the descriptions of explicit tags that already exist with those in the files.
        /// Implicit tags made explicit always take the file's description.
        /// </summary>
        public bool OverwriteDescriptions;

        /// <summary>
        /// Delete the main registry's tags that the files don't list, after confirmation.
        /// </summary>
        public bool RemoveTagsNotInFiles;
    }



    /// <summary>
    /// What the user chose when an import would remove tags.
    /// </summary>
    internal enum TagFileRemovalChoice
    {
        Remove,
        KeepThem,
        Cancel,
    }



    /// <summary>
    /// The changes an import makes to the main registry. Built before anything is written, for previews and dialogs.
    /// </summary>
    internal sealed class TagFileImportPlan
    {
        /// <summary> Paths of the explicit tags the import creates. </summary>
        public readonly List<string> Added = new();

        /// <summary> Paths of the implicit parents the import creates. </summary>
        public readonly List<string> ImplicitParentsAdded = new();

        /// <summary> Paths of implicit tags that become explicit. They keep their IDs. </summary>
        public readonly List<string> MadeExplicit = new();

        /// <summary> Paths of tags whose description changes. </summary>
        public readonly List<string> DescriptionsUpdated = new();

        /// <summary> Paths of the main registry's tags that the files don't list, sorted. </summary>
        public readonly List<string> NotInFiles = new();

        /// <summary> Paths of the tags the import deletes. Only filled when removal is on. </summary>
        public readonly List<string> Removed = new();

        /// <summary> The top-most tags of <see cref="NotInFiles"/>: deleting them deletes the rest. </summary>
        public readonly List<TagGuid> RemovalRoots = new();

        public readonly List<string> Warnings = new();

        public bool HasChanges => Added.Count + ImplicitParentsAdded.Count + MadeExplicit.Count + DescriptionsUpdated.Count + Removed.Count > 0;

        public string Summary
        {
            get
            {
                if (!HasChanges) return "The main registry already matches the file(s).";

                var parts = new List<string>();
                if (Added.Count > 0) parts.Add($"{Added.Count} added");
                if (ImplicitParentsAdded.Count > 0) parts.Add($"{ImplicitParentsAdded.Count} implicit parent(s) added");
                if (MadeExplicit.Count > 0) parts.Add($"{MadeExplicit.Count} made explicit");
                if (DescriptionsUpdated.Count > 0) parts.Add($"{DescriptionsUpdated.Count} description(s) updated");
                if (Removed.Count > 0) parts.Add($"{Removed.Count} removed");

                return string.Join(", ", parts) + ".";
            }
        }
    }



    /// <summary>
    /// Result of <see cref="TagFileImport.Import"/>.
    /// </summary>
    internal sealed class TagFileImportResult
    {
        public bool Success;
        public bool Cancelled;
        public string Message;

        /// <summary> The applied plan, or <see langword="null"/> if the files couldn't be read. </summary>
        public TagFileImportPlan Plan;
    }



    /// <summary>
    /// Imports tag files into the main registry. An explicit command: it needs the lock, writes the main
    /// registry in one operation, and is undone in one step. Matching is by path, so a line renamed in a tag file
    /// is a new tag, never a rename.
    /// </summary>
    internal static class TagFileImport
    {
        private const int MAX_LISTED_TAGS = 10;



        #region Command
        /// <summary>
        /// Import tag files into the main registry.
        /// </summary>
        /// <param name="_Files"> Paths of <c>*.tags</c> files, relative to the project or absolute. </param>
        /// <param name="_ConfirmRemoval">
        /// Asked when the import would delete tags, with their paths. <see langword="null"/> shows the delete dialog.
        /// </param>
        public static TagFileImportResult Import(
            IReadOnlyList<string> _Files,
            TagFileImportOptions _Options,
            Func<IReadOnlyList<string>, TagFileRemovalChoice> _ConfirmRemoval = null)
        {
            _Options ??= new TagFileImportOptions();

            if (!TryRead(_Files, out var parsed, out string error))
                return new TagFileImportResult { Message = error };

            if (!TryGetMain(out var main, out error))
                return new TagFileImportResult { Message = error };

            var plan = Plan(main, parsed.Entries, _Options.OverwriteDescriptions, false);
            plan.Warnings.AddRange(parsed.Warnings);

            IReadOnlyCollection<TagGuid> remove = Array.Empty<TagGuid>();
            if (_Options.RemoveTagsNotInFiles && plan.NotInFiles.Count > 0)
            {
                var choice = (_ConfirmRemoval ?? ConfirmRemovalDialog)(plan.NotInFiles);
                if (choice == TagFileRemovalChoice.Cancel)
                    return new TagFileImportResult { Cancelled = true, Message = "Import cancelled.", Plan = plan };

                if (choice == TagFileRemovalChoice.Remove)
                {
                    plan = Plan(main, parsed.Entries, _Options.OverwriteDescriptions, true);
                    plan.Warnings.AddRange(parsed.Warnings);
                    remove = plan.RemovalRoots;
                }
            }

            if (!plan.HasChanges)
                return new TagFileImportResult { Success = true, Message = plan.Summary, Plan = plan };

            var result = TagOperations.ImportTagFiles(parsed.Entries, _Options.OverwriteDescriptions, remove);
            return new TagFileImportResult
            {
                Success = result.Success,
                Message = result.Success ? $"Imported {_Files.Count} tag file(s): {plan.Summary} Not saved yet: save in the Gameplay Tag Manager." : result.Error,
                Plan = plan,
            };
        }

        /// <summary>
        /// The changes importing <paramref name="_Files"/> would make, without writing anything.
        /// </summary>
        public static bool TryPreview(IReadOnlyList<string> _Files, TagFileImportOptions _Options, out TagFileImportPlan _Plan, out string _Error)
        {
            _Plan = null;
            if (!TryRead(_Files, out var parsed, out _Error)) return false;
            if (!TryGetMain(out var main, out _Error)) return false;

            _Plan = Plan(main, parsed.Entries, _Options.OverwriteDescriptions, _Options.RemoveTagsNotInFiles);
            _Plan.Warnings.AddRange(parsed.Warnings);
            return true;
        }

        private static bool TryRead(IReadOnlyList<string> _Files, out TagFileParseResult _Parsed, out string _Error)
        {
            _Parsed = null;

            if (_Files == null || _Files.Count == 0)
            {
                _Error = "No tag files selected.";
                return false;
            }

            var files = new List<(string, string)>(_Files.Count);
            foreach (string file in _Files)
            {
                try
                {
                    files.Add((Path.GetFileName(file), File.ReadAllText(file)));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    _Error = $"Could not read \"{file}\": {exception.Message}";
                    return false;
                }
            }

            _Parsed = TagFile.Parse(files);
            if (!_Parsed.IsValid)
            {
                _Error = $"The tag file(s) have errors. Nothing was imported.\n{string.Join("\n", _Parsed.Errors)}";
                return false;
            }

            _Error = null;
            return true;
        }

        private static bool TryGetMain(out TagRegistryData _Main, out string _Error)
        {
            var main = TagRegistryService.Snapshot.Main;
            if (main == null || main.IsReadOnly)
            {
                _Main = null;
                _Error = "There is no main registry yet. Create it in the Gameplay Tag Manager.";
                return false;
            }

            _Main = main.Data;
            _Error = null;
            return true;
        }

        private static TagFileRemovalChoice ConfirmRemovalDialog(IReadOnlyList<string> _Paths)
        {
            string list = string.Join("\n", _Paths.Take(MAX_LISTED_TAGS).Select(e => $"  \"{e}\""));
            if (_Paths.Count > MAX_LISTED_TAGS)
                list += $"\n  (and {_Paths.Count - MAX_LISTED_TAGS} more)";

            int choice = EditorUtility.DisplayDialogComplex(
                title: "Import Tag Files",
                message: $"{_Paths.Count} tag(s) of the main registry are not in the file(s) and will be deleted:\n\n{list}\n\n" +
                         "References to deleted tags show as (deleted). Undo restores them.",
                ok: "Import And Delete",
                cancel: "Cancel",
                alt: "Import Without Deleting");

            return choice switch
            {
                0 => TagFileRemovalChoice.Remove,
                2 => TagFileRemovalChoice.KeepThem,
                _ => TagFileRemovalChoice.Cancel,
            };
        }
        #endregion



        #region Plan
        /// <summary>
        /// Work out what importing <paramref name="_Entries"/> into <paramref name="_Main"/> changes.
        /// <paramref name="_Main"/> is not modified.
        /// </summary>
        public static TagFileImportPlan Plan(TagRegistryData _Main, IReadOnlyList<TagFileEntry> _Entries, bool _OverwriteDescriptions, bool _Remove)
        {
            var plan = new TagFileImportPlan();
            var before = ComputePaths(_Main);

            // Everything the files list, and every parent of it, stays.
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in _Entries)
                for (string path = entry.Path; path.Length > 0; path = TagPath.GetParent(path))
                    keep.Add(path);

            var notInFiles = new HashSet<TagGuid>();
            foreach (var row in _Main.rows)
                if (before.TryGetValue(row.id, out string path) && !keep.Contains(path))
                    notInFiles.Add(row.id);

            foreach (var id in notInFiles)
            {
                plan.NotInFiles.Add(before[id]);

                int index = _Main.IndexOf(id);
                if (!notInFiles.Contains(_Main.rows[index].parent))
                    plan.RemovalRoots.Add(id);
            }

            plan.NotInFiles.Sort(string.CompareOrdinal);
            plan.RemovalRoots.Sort((a, b) => string.CompareOrdinal(before[a], before[b]));

            var after = _Main.Clone();
            ApplyTo(after, _Entries, _OverwriteDescriptions, _Remove ? plan.RemovalRoots : Array.Empty<TagGuid>());

            // Diff by ID, so the plan says exactly what the transaction will do.
            var oldRows = _Main.rows.ToDictionary(e => e.id);
            var newPaths = ComputePaths(after);

            foreach (var row in after.rows)
            {
                if (!oldRows.TryGetValue(row.id, out var old))
                {
                    (row.isImplicit ? plan.ImplicitParentsAdded : plan.Added).Add(newPaths[row.id]);
                    continue;
                }

                if (old.isImplicit && !row.isImplicit) plan.MadeExplicit.Add(newPaths[row.id]);
                if (old.description != row.description) plan.DescriptionsUpdated.Add(newPaths[row.id]);
            }

            var remaining = new HashSet<TagGuid>(after.rows.Select(e => e.id));
            foreach (var row in _Main.rows)
                if (!remaining.Contains(row.id))
                    plan.Removed.Add(before[row.id]);

            plan.Added.Sort(string.CompareOrdinal);
            plan.ImplicitParentsAdded.Sort(string.CompareOrdinal);
            plan.MadeExplicit.Sort(string.CompareOrdinal);
            plan.DescriptionsUpdated.Sort(string.CompareOrdinal);
            plan.Removed.Sort(string.CompareOrdinal);

            return plan;
        }

        /// <summary>
        /// Apply an import to registry content. The entries' paths must be valid.
        /// </summary>
        /// <param name="_Remove"> Tags to delete first, with their subtrees. </param>
        internal static void ApplyTo(TagRegistryData _Data, IReadOnlyList<TagFileEntry> _Entries, bool _OverwriteDescriptions, IReadOnlyCollection<TagGuid> _Remove)
        {
            foreach (var id in _Remove)
                TagOperations.RemoveSubtree(_Data, id);

            foreach (var entry in _Entries)
            {
                var id = TagOperations.AddPath(_Data, entry.Path, entry.Description, out var outcome);
                if (outcome != TagOperations.AddPathOutcome.ExistedExplicit || !_OverwriteDescriptions || !entry.HasDescription)
                    continue;

                int index = _Data.IndexOf(id);
                var row = _Data.rows[index];
                row.description = entry.Description;
                _Data.rows[index] = row;
            }
        }

        private static Dictionary<TagGuid, string> ComputePaths(TagRegistryData _Data)
        {
            var rows = _Data.rows.ToDictionary(e => e.id);
            var paths = new Dictionary<TagGuid, string>(rows.Count);

            string PathOf(TagGuid _Id, int _Depth)
            {
                if (paths.TryGetValue(_Id, out string known)) return known;
                if (_Depth > rows.Count || !rows.TryGetValue(_Id, out var row)) return null;

                string parent = row.parent.IsZero ? string.Empty : PathOf(row.parent, _Depth + 1);
                if (parent == null) return null;

                return paths[_Id] = TagPath.Combine(parent, row.segment);
            }

            foreach (var id in rows.Keys)
                PathOf(id, 0);

            return paths;
        }
        #endregion
    }
}
