using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// The "Import Tag Files" command: pick tag files and options, preview the changes, then import them
    /// into the main registry.
    /// </summary>
    internal sealed class TagFileImportWindow : EditorWindow
    {
        private const string SESSION_OVERWRITE = "LMK.GameplayTags.Import.OverwriteDescriptions";
        private const string SESSION_REMOVE = "LMK.GameplayTags.Import.RemoveTagsNotInFiles";
        private const string ASSETS_MENU = "Assets/Gameplay Tags/Import Into Main Registry...";

        [SerializeField]
        private List<string> files = new();

        private ListView fileList;
        private Toggle overwriteToggle;
        private Toggle removeToggle;
        private HelpBox preview;
        private Button importButton;



        #region Opening
        /// <summary>
        /// Open the window with these files listed.
        /// </summary>
        public static void Open(IEnumerable<string> _Files = null)
        {
            var window = GetWindow<TagFileImportWindow>(true, "Import Tag Files");
            window.minSize = new Vector2(420, 320);

            if (_Files != null)
                foreach (string file in _Files)
                    window.AddFile(file);

            window.Refresh();
        }

        [MenuItem(ASSETS_MENU, false, 2000)]
        private static void ImportSelected() => Open(SelectedTagFiles());

        [MenuItem(ASSETS_MENU, true)]
        private static bool ImportSelectedValidate() => SelectedTagFiles().Any();

        private static IEnumerable<string> SelectedTagFiles()
            => Selection.assetGUIDs
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(e => e.EndsWith("." + TagFile.EXTENSION, StringComparison.OrdinalIgnoreCase));
        #endregion



        #region GUI
        private void CreateGUI()
        {
            var root = rootVisualElement;
            LMKTheme.Apply(root);
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 6;

            root.Add(new Label("Tag files") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 2 } });

            fileList = new ListView(files, 18, () => new Label(), (e, i) => ((Label)e).text = files[i])
            {
                selectionType = SelectionType.Multiple,
                showBorder = true,
                style = { flexGrow = 1, minHeight = 80 },
            };
            root.Add(fileList);

            var fileButtons = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 2 } };
            fileButtons.Add(new Button(BrowseFile) { text = "Add File..." });
            fileButtons.Add(new Button(RemoveSelectedFiles) { text = "Remove" });
            root.Add(fileButtons);

            overwriteToggle = new Toggle("Overwrite descriptions")
            {
                tooltip = "Replace the descriptions of existing tags with those in the files. " +
                          "Implicit tags made explicit always take the file's description.",
                value = SessionState.GetBool(SESSION_OVERWRITE, false),
            };
            overwriteToggle.RegisterValueChangedCallback(e => { SessionState.SetBool(SESSION_OVERWRITE, e.newValue); Refresh(); });
            root.Add(overwriteToggle);

            removeToggle = new Toggle("Remove tags not in the file(s)")
            {
                tooltip = "Delete the main registry's tags that the files don't list. You confirm the list before anything is deleted.",
                value = SessionState.GetBool(SESSION_REMOVE, false),
            };
            removeToggle.RegisterValueChangedCallback(e => { SessionState.SetBool(SESSION_REMOVE, e.newValue); Refresh(); });
            root.Add(removeToggle);

            preview = new HelpBox(string.Empty, HelpBoxMessageType.Info) { style = { marginTop = 6 } };
            root.Add(preview);

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 6 } };
            actions.Add(new Button(Close) { text = "Close" });
            importButton = new Button(Import) { text = "Import" };
            actions.Add(importButton);
            root.Add(actions);

            Refresh();
        }

        private void OnFocus() => Refresh();

        private TagFileImportOptions Options => new()
        {
            OverwriteDescriptions = SessionState.GetBool(SESSION_OVERWRITE, false),
            RemoveTagsNotInFiles = SessionState.GetBool(SESSION_REMOVE, false),
        };

        /// <summary>
        /// Show what the import would change, from the files as they are now.
        /// </summary>
        private void Refresh()
        {
            if (preview == null) return;

            fileList.RefreshItems();

            if (files.Count == 0)
            {
                ShowPreview("Add the tag files to import. Nothing changes until you press Import.", HelpBoxMessageType.Info, false);
                return;
            }

            if (!TagFileImport.TryPreview(files, Options, out var plan, out string error))
            {
                ShowPreview(error, HelpBoxMessageType.Error, false);
                return;
            }

            var lines = new List<string> { plan.Summary };
            if (!Options.RemoveTagsNotInFiles && plan.NotInFiles.Count > 0)
                lines.Add($"{plan.NotInFiles.Count} tag(s) of the main registry are not in the file(s). They are kept.");
            lines.AddRange(plan.Warnings);

            ShowPreview(string.Join("\n", lines), plan.Warnings.Count > 0 ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info, plan.HasChanges);
        }

        private void ShowPreview(string _Text, HelpBoxMessageType _Type, bool _CanImport)
        {
            preview.text = _Text;
            preview.messageType = _Type;
            importButton.SetEnabled(_CanImport);
        }
        #endregion



        #region Actions
        private void AddFile(string _Path)
        {
            if (string.IsNullOrEmpty(_Path)) return;

            string path = TagFileExport.ToAssetPath(_Path) ?? _Path.Replace('\\', '/');
            if (!files.Contains(path))
                files.Add(path);
        }

        private void BrowseFile()
        {
            string folder = AssetDatabase.IsValidFolder(TagFileExport.DEFAULT_FOLDER) ? TagFileExport.DEFAULT_FOLDER : "Assets";
            string path = EditorUtility.OpenFilePanel("Add Tag File", folder, TagFile.EXTENSION);
            if (string.IsNullOrEmpty(path)) return;

            AddFile(path);
            Refresh();
        }

        private void RemoveSelectedFiles()
        {
            foreach (int index in fileList.selectedIndices.OrderByDescending(e => e).ToList())
                files.RemoveAt(index);

            fileList.ClearSelection();
            Refresh();
        }

        private void Import()
        {
            var result = TagFileImport.Import(files, Options);
            if (result.Cancelled) return;

            if (!result.Success)
            {
                ShowPreview(result.Message, HelpBoxMessageType.Error, true);
                return;
            }

            Debug.Log(result.Message);
            ShowPreview(result.Message, HelpBoxMessageType.Info, false);
        }
        #endregion
    }
}
