using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEditor;
using UnityEngine;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Explicit tag commands, shared by the Tools menu and the Gameplay Tag Manager. Saving, discarding and the commands
    /// for every registry kind are the registry core's (<see cref="RegistryCommands"/>).
    /// </summary>
    internal static class GameplayTagCommands
    {
        private const string MENU = "Tools/LMK/Gameplay Tags/";



        [MenuItem(MENU + "Import Tag Files...", priority = 20)]
        public static void ImportTagFiles() => TagFileImportWindow.Open();

        [MenuItem(MENU + "Export Main Registry To Tag File...", priority = 21)]
        public static void ExportMainRegistry()
        {
            var main = TagRegistryService.Snapshot.Main;
            if (main == null)
            {
                EditorUtility.DisplayDialog("Export Tag File", "There is no main registry yet. Create it in the Gameplay Tag Manager.", "OK");
                return;
            }

            TagFileExport.ExportWithDialog(main);
        }

        private const string ASSETS_EXPORT_MENU = "Assets/Gameplay Tags/Export To Tag File...";

        /// <summary>
        /// Export the registry files selected in the Project window: main, package or generated.
        /// </summary>
        [MenuItem(ASSETS_EXPORT_MENU, false, 2001)]
        public static void ExportSelectedRegistries() => TagFileExport.ExportSelected(SelectedRegistryFiles());

        [MenuItem(ASSETS_EXPORT_MENU, true)]
        private static bool ExportSelectedRegistriesValidate() => SelectedRegistryFiles().Count > 0;

        private static List<string> SelectedRegistryFiles()
            => Selection.assetGUIDs
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(e => e.EndsWith("." + TagRegistryFile.EXTENSION, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

        [MenuItem(MENU + "Generate Code", priority = 40)]
        public static void GenerateCode()
        {
            var result = TagCodeGen.Generate();
            if (!result.Success)
                EditorUtility.DisplayDialog("Generate Gameplay Tag Code", result.Error, "OK");
        }

        [MenuItem(MENU + "Prune Unused Code Aliases", priority = 41)]
        public static void PruneCodeAliases()
        {
            var result = TagCodeGen.PruneAliases(out var pruned);
            if (!result.Success)
            {
                EditorUtility.DisplayDialog("Prune Unused Code Aliases", result.Error, "OK");
                return;
            }

            Debug.Log(pruned.Count == 0
                ? "Gameplay tag code: no unused aliases."
                : $"Gameplay tag code: removed {pruned.Count} unused alias(es): {string.Join(", ", pruned)}.");
        }

        // Map Imported Assets, Rebuild Reference Index and Generate Registries For Package are the registry core's
        // commands, for every kind: see the Tools and Assets menus.
        public static void MapImportedAssets() => ProjectAssetImport.MapPendingWithDialog();

        /// <summary>
        /// Generate the registry a package without one needs, or regenerate it, after a confirmation.
        /// Regenerating keeps the existing rows, unless the user also removes the ones the package no longer uses.
        /// </summary>
        public static void GeneratePackageRegistryWithDialog(RegistryPackage _Package)
        {
            if (_Package == null) return;

            bool regenerate = TagRegistryService.Snapshot.Registries
                .Any(e => e.Kind == TagRegistryKind.Generated && e.Data.package == _Package.Name);

            string title = regenerate ? "Regenerate Package Tag Registry" : "Generate Package Tag Registry";
            string message = regenerate
                ? $"Scan \"{_Package.Name}\" {_Package.Version} again and add the tags it now references to its generated registry? " +
                  "Existing tags are kept, unless you also remove the ones the package no longer references or lists."
                : $"Scan \"{_Package.Name}\" for gameplay tag references and generate a registry for those that don't resolve? " +
                  $"It's written to \"{PackageRegistryService.GeneratedFolder}\". The package itself is not changed.";

            int choice = regenerate
                ? EditorUtility.DisplayDialogComplex(title, message, "Regenerate", "Cancel", "Regenerate And Remove Unused")
                : EditorUtility.DisplayDialog(title, message, "Generate", "Cancel") ? 0 : 1;

            if (choice == 1) return;

            var result = PackageRegistryService.Generate(_Package, choice == 2);
            if (result.Success) Debug.Log(result.Message);
            EditorUtility.DisplayDialog(title, result.Message, "OK");
        }

        [MenuItem(MENU + "Relink Missing References...", priority = 60)]
        public static void RelinkMissingReferences() => RegistryReferenceWindow.ShowRelink(TagKind.Instance);

        public static void RebuildReferenceIndex()
        {
            bool complete = TagReferenceIndex.Rebuild(true);
            Debug.Log(complete
                ? $"Gameplay tag references: indexed {TagReferenceIndex.LastScanCount} asset(s)."
                : "Gameplay tag references: the rebuild was cancelled. The next query scans the rest.");
        }



        #region Delete
        private const int MAX_LISTED_TAGS = 5;
        private const int MAX_LISTED_ASSETS = 10;

        /// <summary>
        /// What deleting a tag of the main registry involves: its sub tags, the implicit parents it would
        /// leave empty, and the saved assets that reference any of them.
        /// </summary>
        internal sealed class DeletePlan
        {
            public string Path;
            public List<string> SubTags = new();
            public IReadOnlyList<string> EmptyParents = Array.Empty<string>();

            /// <summary>
            /// Saved assets referencing the tag, its sub tags or the parents left empty, from the reference index.
            /// </summary>
            public List<string> ReferencingAssets = new();

            /// <summary>
            /// False if the user cancelled the reference index scan: assets may be missing from the list.
            /// </summary>
            public bool ReferenceCheckComplete = true;

            public string Message;
        }

        /// <summary>
        /// Plan the deletion of a main registry tag, or <see langword="null"/> if it isn't there.
        /// </summary>
        internal static DeletePlan PlanDelete(RegistryGuid _Id, bool _ShowProgress)
        {
            var main = TagRegistryService.Snapshot.Main;
            if (main == null || !main.Contains(_Id)) return null;

            var plan = new DeletePlan
            {
                Path = main.GetPath(_Id),
                EmptyParents = TagOperations.GetParentsLeftEmpty(_Id),
            };

            var ids = new List<RegistryGuid> { _Id };
            for (int i = 0; i < ids.Count; i++)
                ids.AddRange(main.GetChildren(ids[i]));

            plan.SubTags = ids.Skip(1).Select(main.GetPath).OrderBy(e => e, StringComparer.Ordinal).ToList();

            foreach (string parent in plan.EmptyParents)
                if (main.TryFind(parent, out var parentId))
                    ids.Add(parentId);

            var tags = ids.Select(e => GameplayTag.FromId(main.Id, e)).ToList();
            plan.ReferenceCheckComplete = TagReferenceIndex.TryFindAssets(tags, _ShowProgress, out plan.ReferencingAssets);

            plan.Message = BuildDeleteMessage(plan);
            return plan;
        }

        private static string BuildDeleteMessage(DeletePlan _Plan)
        {
            var message = new StringBuilder(_Plan.SubTags.Count > 0
                ? $"Delete \"{_Plan.Path}\" and its {_Plan.SubTags.Count} sub tag(s)?\n\n{ListItems(_Plan.SubTags, MAX_LISTED_TAGS)}"
                : $"Delete \"{_Plan.Path}\"?");

            if (_Plan.EmptyParents.Count > 0)
                message.Append($"\n\nThese implicit parents would be left empty:\n\n{ListItems(_Plan.EmptyParents, MAX_LISTED_TAGS)}");

            if (_Plan.ReferencingAssets.Count > 0)
                message.Append($"\n\nReferenced by {_Plan.ReferencingAssets.Count} saved asset(s):\n\n{ListItems(_Plan.ReferencingAssets, MAX_LISTED_ASSETS)}")
                       .Append("\n\nTheir references will show as (deleted). Undo restores them, and Relink Missing References can point them to a tag with the same name later.");
            else
                message.Append("\n\nNo saved asset references it.");

            if (!_Plan.ReferenceCheckComplete)
                message.Append("\n\nThe reference check was cancelled, so this list may be incomplete.");

            message.Append("\n\nUnsaved scenes and assets are not checked.");
            return message.ToString();
        }

        /// <summary>
        /// Delete a main registry tag after a confirmation that lists its sub tags, the parents it leaves empty and
        /// the assets that reference them. Referenced tags can still be deleted.
        /// </summary>
        /// <returns> The result, or <see langword="null"/> if the user cancelled. </returns>
        public static RegistryResult? DeleteWithDialog(RegistryGuid _Id)
        {
            var plan = PlanDelete(_Id, true);
            if (plan == null)
                return RegistryResult.Fail("This tag is not in the main registry. Package and generated registries are read-only.");

            bool referenced = plan.ReferencingAssets.Count > 0;
            bool removeEmptyParents;

            if (plan.EmptyParents.Count > 0)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    title: "Delete Gameplay Tag",
                    message: plan.Message,
                    ok: referenced ? "Delete Anyway, With Empty Parents" : "Delete With Empty Parents",
                    cancel: "Cancel",
                    alt: referenced ? "Delete Anyway, Only This Tag" : "Delete Only This Tag");

                if (choice == 1) return null;
                removeEmptyParents = choice == 0;
            }
            else
            {
                string ok = referenced ? "Delete Anyway" : plan.SubTags.Count > 0 ? "Delete Subtree" : "Delete";
                if (!EditorUtility.DisplayDialog("Delete Gameplay Tag", plan.Message, ok, "Cancel"))
                    return null;

                removeEmptyParents = false;
            }

            return TagOperations.Delete(_Id, removeEmptyParents);
        }

        private static string ListItems(IReadOnlyList<string> _Items, int _Max) => RegistryCommands.ListItems(_Items, _Max);
        #endregion
    }
}
