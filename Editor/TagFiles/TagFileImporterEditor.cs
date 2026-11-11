using System;
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.Utilities;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Inspector of a tag file: its tags as a table with alternating rows, each with its status against the main
    /// registry, problems in the file, and a button to import it. The file is read from disk when it changes.
    /// Nothing is stored.
    /// </summary>
    [CustomEditor(typeof(TagFileImporter))]
    internal sealed class TagFileImporterEditor : ScriptedImporterEditor
    {
        private const string USS_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/TagFileInspectorUSS.uss";

        private const string USS_ROOT = "tag-file";
        private const string USS_ROW = USS_ROOT + "__row";
        private const string USS_ROW_ODD = USS_ROW + "--odd";
        private const string USS_ROW_LEAF = USS_ROW + "--leaf";
        private const string USS_ROW_PARENT = USS_ROW + "--parent";
        private const string USS_STATUS = USS_ROOT + "__status";

        private const float INDENT = 15f;
        private const long POLL_INTERVAL_MS = 500;

        protected override bool needsApplyRevert => false;

        public override bool showImportedObject => false;

        // In-memory only: rebuilt when the file or the registries change.
        private string cachedPath;
        private DateTime cachedWriteTime;
        private TagRegistrySnapshot cachedSnapshot;
        private TagFilePreview preview;
        private string readError;

        private readonly HashSet<string> collapsed = new(StringComparer.Ordinal);

        private VisualElement content;

        /// <summary>
        /// One row per tag, in display order, with the index of its parent row (-1 for a top level tag).
        /// </summary>
        private readonly List<(VisualElement Row, int Parent, string Path)> rows = new();



        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            LMKTheme.Apply(root);
            root.AddToClassList(USS_ROOT);

            var styleSheet = LMKAssetUtility.Load<StyleSheet>(USS_PATH);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            content = new VisualElement();
            root.Add(content);

            // Draws nothing here (no import settings), but every importer inspector must call it.
            root.Add(new IMGUIContainer(() => ApplyRevertGUI()));

            root.RegisterCallback<AttachToPanelEvent>(_ => TagRegistryService.OnSnapshotChanged += Rebuild);
            root.RegisterCallback<DetachFromPanelEvent>(_ => TagRegistryService.OnSnapshotChanged -= Rebuild);

            // The file can change outside Unity without a reimport (auto refresh off): check it now and then.
            root.schedule.Execute(() =>
            {
                if (Refresh())
                    Build();
            }).Every(POLL_INTERVAL_MS);

            Refresh();
            Build();
            return root;
        }

        private void Rebuild()
        {
            if (content?.panel == null) return;

            Refresh();
            Build();
        }

        /// <summary>
        /// Read and parse the file again if it or the registries changed.
        /// </summary>
        /// <returns> Whether anything changed. </returns>
        private bool Refresh()
        {
            if (target == null) return false;

            string path = ((AssetImporter)target).assetPath;
            var snapshot = TagRegistryService.Snapshot;
            DateTime writeTime = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

            if ((preview != null || readError != null) && path == cachedPath && writeTime == cachedWriteTime && snapshot == cachedSnapshot)
                return false;

            cachedPath = path;
            cachedWriteTime = writeTime;
            cachedSnapshot = snapshot;
            readError = null;

            try
            {
                var parsed = TagFile.Parse(File.ReadAllText(path), Path.GetFileName(path));
                preview = TagFilePreview.Build(parsed, snapshot.Main);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                preview = null;
                readError = $"Could not read \"{path}\": {exception.Message}";
            }

            return true;
        }



        #region Building
        private void Build()
        {
            if (content == null) return;

            content.Clear();
            rows.Clear();

            if (targets.Length > 1)
            {
                content.Add(HelpBox("Select one tag file to see its tags.", HelpBoxMessageType.Info));
                return;
            }

            if (readError != null)
            {
                content.Add(HelpBox(readError, HelpBoxMessageType.Error));
                return;
            }

            var summary = new Label(preview.Summary);
            summary.AddToClassList(USS_ROOT + "__summary");
            content.Add(summary);

            if (preview.Errors.Count > 0)
                content.Add(HelpBox($"Fix these lines before importing:\n{string.Join("\n", preview.Errors)}", HelpBoxMessageType.Error));

            if (preview.Warnings.Count > 0)
                content.Add(HelpBox(string.Join("\n", preview.Warnings), HelpBoxMessageType.Warning));

            if (!preview.HasMainRegistry)
                content.Add(HelpBox("There is no main registry yet. Create it in the Gameplay Tag Manager to compare and import.", HelpBoxMessageType.Info));

            var table = new VisualElement();
            table.AddToClassList(USS_ROOT + "__table");
            content.Add(table);

            foreach (var node in preview.Roots)
                AddRows(table, node, 0, -1);

            if (rows.Count == 0)
            {
                var empty = new Label("The file lists no tags.");
                empty.AddToClassList(USS_ROOT + "__empty");
                table.Add(empty);
            }

            UpdateRows();

            var actions = new VisualElement();
            actions.AddToClassList(USS_ROOT + "__actions");

            string path = cachedPath;
            var importButton = new Button(() => TagFileImportWindow.Open(new[] { path })) { text = "Import Into Main Registry..." };
            importButton.AddToClassList(USS_ROOT + "__import-button");
            importButton.SetEnabled(preview.CanImport);
            actions.Add(importButton);
            content.Add(actions);
        }

        private void AddRows(VisualElement _Table, TagFilePreview.Node _Node, int _Depth, int _Parent)
        {
            var row = new VisualElement { tooltip = _Node.IsListed ? _Node.Path : $"{_Node.Path} (not listed: a parent of listed tags)" };
            row.AddToClassList(USS_ROW);
            row.EnableInClassList(USS_ROW_LEAF, _Node.Children.Count == 0);
            row.EnableInClassList(USS_ROW_PARENT, !_Node.IsListed);

            // A header-only foldout: the native arrow, with the children as the following rows.
            var name = new Foldout { text = _Node.Segment, value = !collapsed.Contains(_Node.Path) };
            name.AddToClassList(USS_ROOT + "__name");
            name.style.paddingLeft = _Depth * INDENT;
            if (_Node.Children.Count > 0)
            {
                string path = _Node.Path;
                name.RegisterValueChangedCallback(e =>
                {
                    if (e.target != name) return;

                    if (e.newValue) collapsed.Remove(path);
                    else collapsed.Add(path);
                    UpdateRows();
                });
            }
            row.Add(name);

            var (statusText, statusTooltip, statusClass) = StatusLabel(_Node);
            var status = new Label(statusText) { tooltip = statusTooltip };
            status.AddToClassList(USS_STATUS);
            if (statusClass != null)
                status.AddToClassList(statusClass);
            row.Add(status);

            if (!string.IsNullOrEmpty(_Node.Description))
            {
                var description = new Label(_Node.Description) { tooltip = _Node.Description };
                description.AddToClassList(USS_ROOT + "__description");
                row.Add(description);
            }

            _Table.Add(row);
            int index = rows.Count;
            rows.Add((row, _Parent, _Node.Path));

            foreach (var child in _Node.Children)
                AddRows(_Table, child, _Depth + 1, index);
        }

        /// <summary>
        /// Hide the rows under collapsed tags, and alternate the background of the visible ones.
        /// </summary>
        private void UpdateRows()
        {
            var visible = new bool[rows.Count];
            int shown = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                var (row, parent, _) = rows[i];
                visible[i] = parent < 0 || (visible[parent] && !collapsed.Contains(rows[parent].Path));

                row.style.display = visible[i] ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible[i]) continue;

                row.EnableInClassList(USS_ROW_ODD, shown % 2 == 1);
                shown++;
            }
        }

        private static HelpBox HelpBox(string _Text, HelpBoxMessageType _Type)
        {
            var helpBox = new HelpBox(_Text, _Type);
            helpBox.AddToClassList(USS_ROOT + "__help-box");
            return helpBox;
        }

        /// <returns> Text, tooltip, and the USS class of its colour, if any. </returns>
        private static (string, string, string) StatusLabel(TagFilePreview.Node _Node)
        {
            const string NEW = USS_STATUS + "--new";
            const string CHANGE = USS_STATUS + "--change";

            switch (_Node.Status)
            {
                case TagFileTagStatus.New:
                    return _Node.IsListed
                        ? ("new", "Not in the main registry: importing adds it.", NEW)
                        : ("new parent", "Not in the main registry: importing adds it as an implicit parent.", NEW);

                case TagFileTagStatus.MakesExplicit:
                    return ("made explicit", "An implicit tag of the main registry: importing makes it explicit and keeps its ID.", CHANGE);

                case TagFileTagStatus.DescriptionDiffers:
                    return ("description differs",
                            $"The main registry says \"{_Node.RegistryDescription}\". Importing replaces it only with \"Overwrite descriptions\".",
                            CHANGE);

                case TagFileTagStatus.Exists:
                    return ("exists", "Already in the main registry.", null);

                default:
                    return (string.Empty, string.Empty, null);
            }
        }
        #endregion
    }
}
