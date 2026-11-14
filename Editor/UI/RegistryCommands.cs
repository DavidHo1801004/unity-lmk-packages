using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEditor;
using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Registry commands with their dialogs, shared by the Tools menu and every registry window.
    /// </summary>
    public static class RegistryCommands
    {
        private const string MENU = "Tools/LMK/Registries/";
        private const int MAX_LISTED_ASSETS = 10;



        #region Save And Discard
        /// <summary>
        /// Save every kind's staged edits. If a main registry changed on disk since its edits began, offer to discard the
        /// edits instead, since saving would overwrite that change.
        /// </summary>
        /// <returns> Whether nothing is left unsaved. </returns>
        public static bool SaveWithDialogs()
        {
            if (!RegistrySession.HasPendingChanges) return true;

            var changed = RegistryKinds.All.Where(e => e.DiskChangedWhilePending).ToList();
            if (changed.Count > 0)
            {
                bool discard = EditorUtility.DisplayDialog(
                    "Main Registry Changed On Disk",
                    $"{string.Join(", ", changed.Select(e => $"\"{e.MainRegistryPath}\""))} changed on disk since you began editing, for example " +
                    "through a pull or a revert. Saving would overwrite that change.\n\nDiscard your unsaved registry changes and load the files?",
                    "Discard My Changes",
                    "Cancel");

                if (!discard) return false;

                RegistrySession.Discard();
                return true;
            }

            var result = RegistrySession.Save();
            if (result.Success) return true;

            EditorUtility.DisplayDialog("Save Registries", result.Error, "OK");
            return false;
        }

        /// <summary>
        /// Discard every kind's staged edits, after a confirmation.
        /// </summary>
        public static void DiscardWithDialog()
        {
            if (!RegistrySession.HasPendingChanges) return;

            string names = string.Join(" and ", RegistrySession.PendingKinds.Select(e => e.DisplayName));
            if (EditorUtility.DisplayDialog(
                    "Discard Registry Changes",
                    $"Discard every unsaved change to {names}? This can't be undone.",
                    "Discard",
                    "Cancel"))
                RegistrySession.Discard();
        }
        #endregion



        #region References
        /// <summary>
        /// The saved assets that reference any of a kind's rows, from the reference index, as a paragraph for a delete
        /// dialog. The index scans stale assets first, with a progress bar.
        /// </summary>
        /// <param name="_Assets"> The referencing assets, sorted by path. </param>
        /// <param name="_Complete"> False if the user cancelled the scan: assets may be missing. </param>
        public static string DescribeReferences(RegistryKind _Kind, IReadOnlyCollection<(RegistryGuid Registry, RegistryGuid Id)> _Rows,
                                                out List<string> _Assets, out bool _Complete)
        {
            _Complete = RegistryReferenceIndex.TryFindAssets(_Kind, _Rows, true, out _Assets);

            var message = new StringBuilder();
            if (_Assets.Count > 0)
                message.Append($"Referenced by {_Assets.Count} saved asset(s):\n\n{ListItems(_Assets, MAX_LISTED_ASSETS)}")
                       .Append("\n\nTheir references will show as (deleted). Undo restores them, and Relink Missing References can point " +
                               $"them to a {_Kind.RowNoun} with the same name later.");
            else
                message.Append("No saved asset references it.");

            if (!_Complete)
                message.Append("\n\nThe reference check was cancelled, so this list may be incomplete.");

            message.Append("\n\nUnsaved scenes and assets are not checked.");
            return message.ToString();
        }

        /// <summary>
        /// Up to <paramref name="_Max"/> items, one per line, quoted, then how many more there are.
        /// </summary>
        public static string ListItems(IReadOnlyList<string> _Items, int _Max)
        {
            string text = string.Join("\n", _Items.Take(_Max).Select(e => $"  \"{e}\""));
            if (_Items.Count > _Max)
                text += $"\n  (and {_Items.Count - _Max} more)";

            return text;
        }
        #endregion



        #region Menu
        [MenuItem(MENU + "Save All Registry Changes", false, 0)]
        private static void SaveAll() => SaveWithDialogs();

        [MenuItem(MENU + "Discard All Registry Changes...", false, 1)]
        private static void DiscardAll() => DiscardWithDialog();

        [MenuItem(MENU + "Save All Registry Changes", true)]
        [MenuItem(MENU + "Discard All Registry Changes...", true)]
        private static bool HasPending() => RegistrySession.HasPendingChanges;

        [MenuItem(MENU + "Find References...", false, 50)]
        private static void FindReferences() => RegistryReferenceWindow.ShowReferences(null, default);

        [MenuItem(MENU + "Relink Missing References...", false, 51)]
        private static void RelinkMissingReferences() => RegistryReferenceWindow.ShowRelink(null);

        [MenuItem(MENU + "Rebuild Reference Index", false, 52)]
        private static void RebuildReferenceIndex()
        {
            bool complete = RegistryReferenceIndex.Rebuild(true);
            Debug.Log(complete
                ? $"Registry references: indexed {RegistryReferenceIndex.LastScanCount} asset(s)."
                : "Registry references: the rebuild was cancelled. The next query scans the rest.");
        }

        [MenuItem(MENU + "Map Imported Assets", false, 60)]
        private static void MapImportedAssets() => RegistryImportMapping.MapPendingWithDialog();

        [MenuItem(MENU + "Map Imported Assets", true)]
        private static bool MapImportedAssetsValidate() => RegistryImportMapping.HasPending;

        [MenuItem(MENU + "Find References...", true)]
        [MenuItem(MENU + "Relink Missing References...", true)]
        [MenuItem(MENU + "Rebuild Reference Index", true)]
        private static bool HasKinds() => RegistryKinds.All.Count > 0;
        #endregion
    }
}
