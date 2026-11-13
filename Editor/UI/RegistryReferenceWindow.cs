using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// The reference tools of every kind: Find References lists the saved assets that use a row. Relink and Replace
    /// preview every reference they would rewrite, and rewrite only the selected ones.
    /// </summary>
    public sealed class RegistryReferenceWindow : EditorWindow
    {
        private enum Mode
        {
            References,
            Relink,
            Replace,
        }

        [SerializeField] private Mode mode;
        [SerializeField] private string kindId;

        // The row whose references are listed, or replaced, and the row references are replaced with: (registry, ID).
        [SerializeField] private string subjectRegistry, subjectId;
        [SerializeField] private string replacementRegistry, replacementId;

        private readonly List<string> assets = new();
        private readonly List<RegistryReferenceChange> changes = new();
        private bool scanned;
        private string statusText;
        private HelpBoxMessageType statusType;

        private PopupField<string> kindField;
        private Label heading;
        private HelpBox info;
        private VisualElement replaceRow;
        private Label replaceLabel, withLabel;
        private Label fromLabel;
        private Button toButton;
        private ListView assetList;
        private ListView changeList;
        private VisualElement selectionButtons;
        private HelpBox status;
        private Button applyButton;

        private RegistryKind Kind => RegistryKinds.Find(kindId) ?? RegistryKinds.All.FirstOrDefault();

        private (RegistryGuid Registry, RegistryGuid Id) Subject
        {
            get => (Parse(subjectRegistry), Parse(subjectId));
            set => (subjectRegistry, subjectId) = (value.Registry.ToString(), value.Id.ToString());
        }

        private (RegistryGuid Registry, RegistryGuid Id) Replacement
        {
            get => (Parse(replacementRegistry), Parse(replacementId));
            set => (replacementRegistry, replacementId) = (value.Registry.ToString(), value.Id.ToString());
        }

        private static RegistryGuid Parse(string _Text) => RegistryGuid.TryParse(_Text, out var id) ? id : RegistryGuid.Zero;



        #region Opening
        /// <summary>
        /// List the saved assets that reference a row of a kind. A <see langword="null"/> kind opens on the first kind,
        /// with no row.
        /// </summary>
        public static void ShowReferences(RegistryKind _Kind, (RegistryGuid Registry, RegistryGuid Id) _Row) => Open(Mode.References, _Kind, _Row, true);

        /// <summary>
        /// Preview relinking the missing references of a kind whose last known name is a live row.
        /// </summary>
        public static void ShowRelink(RegistryKind _Kind) => Open(Mode.Relink, _Kind, default, true);

        /// <summary>
        /// Replace the references to a row of a kind with a row the user picks.
        /// </summary>
        public static void ShowReplace(RegistryKind _Kind, (RegistryGuid Registry, RegistryGuid Id) _From) => Open(Mode.Replace, _Kind, _From, false);

        private static void Open(Mode _Mode, RegistryKind _Kind, (RegistryGuid Registry, RegistryGuid Id) _Subject, bool _Scan)
        {
            var window = GetWindow<RegistryReferenceWindow>(false, "Registry References");
            window.minSize = new Vector2(480, 320);

            window.mode = _Mode;
            window.kindId = (_Kind ?? RegistryKinds.All.FirstOrDefault())?.Id;
            window.Subject = _Subject;
            window.Replacement = default;
            window.ClearResults();

            if (_Scan && !(_Mode == Mode.References && _Subject.Id.IsZero))
                window.Scan();
            else
                window.RefreshView();
        }
        #endregion



        #region GUI
        private void CreateGUI()
        {
            var root = rootVisualElement;
            LMKTheme.Apply(root);
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 6;

            var kinds = RegistryKinds.All.Select(e => e.Id).ToList();
            if (kinds.Count == 0) kinds.Add(string.Empty);
            kindField = new PopupField<string>("Kind", kinds, Kind?.Id ?? kinds[0], e => RegistryKinds.Find(e)?.DisplayName ?? "(none)", e => RegistryKinds.Find(e)?.DisplayName ?? "(none)")
            {
                name = "kind-field",
                style = { marginBottom = 4 },
            };
            kindField.RegisterValueChangedCallback(e =>
            {
                kindId = e.newValue;
                Subject = default;
                Replacement = default;
                ClearResults();
                if (mode == Mode.Relink) Scan();
                else RefreshView();
            });
            root.Add(kindField);

            heading = new Label { name = "heading", style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13, marginBottom = 4 } };
            root.Add(heading);

            info = new HelpBox(string.Empty, HelpBoxMessageType.Info) { style = { marginBottom = 4 } };
            root.Add(info);

            replaceRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
            replaceLabel = new Label("Replace") { style = { marginRight = 4 } };
            replaceRow.Add(replaceLabel);
            fromLabel = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 4 } };
            replaceRow.Add(fromLabel);
            withLabel = new Label("with") { style = { marginRight = 4 } };
            replaceRow.Add(withLabel);
            toButton = new Button(ChooseReplacement) { name = "replacement-button", tooltip = "Choose the row the references will point to." };
            replaceRow.Add(toButton);
            root.Add(replaceRow);

            assetList = new ListView(assets, 18, () => new Label(), (e, i) => ((Label)e).text = assets[i])
            {
                name = "asset-list",
                selectionType = SelectionType.Single,
                showBorder = true,
                style = { flexGrow = 1, minHeight = 80 },
            };
            assetList.selectionChanged += _ => PingAsset(assetList.selectedIndex >= 0 ? assets[assetList.selectedIndex] : null);
            root.Add(assetList);

            changeList = new ListView(changes, 20, MakeChangeRow, BindChangeRow)
            {
                name = "change-list",
                selectionType = SelectionType.Single,
                showBorder = true,
                style = { flexGrow = 1, minHeight = 80 },
            };
            changeList.selectionChanged += _ => PingAsset(changeList.selectedIndex >= 0 ? changes[changeList.selectedIndex].AssetPath : null);
            root.Add(changeList);

            var footer = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };

            selectionButtons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            selectionButtons.Add(new Button(() => SelectAll(true)) { text = "Select All" });
            selectionButtons.Add(new Button(() => SelectAll(false)) { text = "Select None" });
            footer.Add(selectionButtons);

            footer.Add(new VisualElement { style = { flexGrow = 1 } });
            footer.Add(new Button(Scan) { text = "Scan Again", tooltip = "Find the references again, scanning assets that changed." });
            applyButton = new Button(ApplySelected) { name = "apply-button" };
            footer.Add(applyButton);
            root.Add(footer);

            status = new HelpBox(string.Empty, HelpBoxMessageType.Info) { name = "status", style = { marginTop = 4 } };
            root.Add(status);

            RefreshView();
        }

        private VisualElement MakeChangeRow()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            var toggle = new Toggle { style = { marginRight = 4 } };
            toggle.RegisterValueChangedCallback(e =>
            {
                if (toggle.userData is RegistryReferenceChange change)
                    change.Selected = e.newValue;

                RefreshApplyButton();
            });
            row.Add(toggle);

            row.Add(new Label { style = { flexShrink = 1, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis } });
            return row;
        }

        private void BindChangeRow(VisualElement _Row, int _Index)
        {
            var change = changes[_Index];

            var toggle = _Row.Q<Toggle>();
            toggle.userData = change;
            toggle.SetValueWithoutNotify(change.Selected);

            var label = _Row.Q<Label>();
            label.text = $"{change.AssetPath}  >  {change.ObjectName}  >  {change.PropertyPath}:  {Describe(change.From)}  →  {Describe(change.To)}";
            label.tooltip = label.text;
        }

        private string Describe(RegistryGuid _Registry, RegistryGuid _Id, string _LastKnownName = null)
        {
            var kind = Kind;
            if (_Id.IsZero || kind == null) return "None";

            return kind.GetRowName(_Registry, _Id) ?? RegistryDrawerHelper.GetMissingLabel(new RegistryReference(kind, _Registry, _Id, _LastKnownName));
        }

        private string Describe(in RegistryReference _Reference) => Describe(_Reference.Registry, _Reference.Id, _Reference.Name);

        /// <summary>
        /// Show the parts of the current mode, and its results.
        /// </summary>
        private void RefreshView()
        {
            if (heading == null) return;

            var kind = Kind;
            string noun = kind?.RowNoun ?? "row";
            bool listsAssets = mode == Mode.References;
            bool replacing = mode == Mode.Replace;
            var subject = Subject;
            var replacement = Replacement;

            switch (mode)
            {
                case Mode.References:
                    heading.text = subject.Id.IsZero ? "Find References" : $"References to \"{Describe(subject.Registry, subject.Id)}\"";
                    info.text = $"Saved assets that reference this {noun}. Values a prefab instance inherits are listed for the prefab. " +
                                "Unsaved scenes and assets are not included. Select an asset to ping it.";
                    break;

                case Mode.Relink:
                    heading.text = "Relink Missing References";
                    info.text = $"References that show as (deleted), whose last known name is a live {noun}: in their own registry first, " +
                                "then in the main registry. Only the selected references are rewritten. Assets are saved; open scenes " +
                                "are changed but not saved.";
                    break;

                case Mode.Replace:
                    heading.text = "Replace References";
                    info.text = $"Every saved reference to the first {noun} is rewritten to the second. The first {noun} is not deleted. " +
                                "Only the selected references are rewritten. Assets are saved; open scenes are changed but not saved.";
                    break;
            }

            kindField?.SetValueWithoutNotify(kind?.Id ?? kindField.value);
            replaceRow.style.display = replacing || (listsAssets && subject.Id.IsZero) ? DisplayStyle.Flex : DisplayStyle.None;
            fromLabel.text = Describe(subject.Registry, subject.Id);
            replaceLabel.style.display = withLabel.style.display = fromLabel.style.display = replacing ? DisplayStyle.Flex : DisplayStyle.None;
            toButton.text = replacing
                ? replacement.Id.IsZero ? $"Choose {noun}..." : Describe(replacement.Registry, replacement.Id)
                : $"Choose {noun}...";

            assetList.style.display = listsAssets ? DisplayStyle.Flex : DisplayStyle.None;
            changeList.style.display = listsAssets ? DisplayStyle.None : DisplayStyle.Flex;
            selectionButtons.style.display = listsAssets ? DisplayStyle.None : DisplayStyle.Flex;
            applyButton.style.display = listsAssets ? DisplayStyle.None : DisplayStyle.Flex;
            applyButton.text = replacing ? "Replace Selected" : "Relink Selected";

            assetList.RefreshItems();
            changeList.RefreshItems();

            status.text = scanned || !string.IsNullOrEmpty(statusText) ? statusText : "Press Scan Again to find the references.";
            status.messageType = statusType;

            RefreshApplyButton();
        }

        private void RefreshApplyButton() => applyButton?.SetEnabled(changes.Any(e => e.Selected));
        #endregion



        #region Actions
        private void ClearResults()
        {
            assets.Clear();
            changes.Clear();
            scanned = false;
            statusText = null;
            statusType = HelpBoxMessageType.Info;
        }

        /// <summary>
        /// Find the references for the current mode. Stale index entries are scanned first, with progress.
        /// </summary>
        private void Scan()
        {
            ClearResults();
            var kind = Kind;
            bool complete = true;

            if (kind == null)
            {
                SetStatus("No registry kind is registered.");
            }
            else switch (mode)
            {
                case Mode.References:
                {
                    var subject = Subject;
                    if (subject.Id.IsZero)
                    {
                        SetStatus($"Choose the {kind.RowNoun} to find the references of.");
                        break;
                    }

                    complete = RegistryReferenceIndex.TryFindAssets(kind, new[] { subject }, true, out var found);
                    assets.AddRange(found);
                    SetStatus($"{assets.Count} saved asset(s) reference \"{Describe(subject.Registry, subject.Id)}\".");
                    break;
                }

                case Mode.Relink:
                {
                    complete = RegistryReferenceTools.PlanRelink(kind, true, out var planned);
                    changes.AddRange(planned);
                    SetStatus(changes.Count == 0
                        ? $"No missing reference matches a live {kind.RowNoun} by name."
                        : $"{changes.Count} missing reference(s) in {CountAssets()} asset(s) can be relinked.");
                    break;
                }

                case Mode.Replace:
                {
                    var subject = Subject;
                    var replacement = Replacement;
                    if (replacement.Id.IsZero)
                    {
                        SetStatus($"Choose the {kind.RowNoun} to replace the references with.");
                        break;
                    }

                    if (replacement == subject)
                    {
                        SetStatus($"Choose a different {kind.RowNoun}.", HelpBoxMessageType.Warning);
                        break;
                    }

                    complete = RegistryReferenceTools.PlanReplace(kind, subject, replacement, true, out var planned);
                    changes.AddRange(planned);
                    SetStatus($"{changes.Count} reference(s) to \"{Describe(subject.Registry, subject.Id)}\" in {CountAssets()} asset(s).");
                    break;
                }
            }

            scanned = true;
            if (!complete)
                SetStatus(statusText + " The scan was cancelled, so this list may be incomplete.", HelpBoxMessageType.Warning);

            RefreshView();
        }

        private int CountAssets() => changes.Select(e => e.AssetPath).Distinct().Count();

        private void SetStatus(string _Text, HelpBoxMessageType _Type = HelpBoxMessageType.Info)
        {
            statusText = _Text;
            statusType = _Type;
        }

        private void SelectAll(bool _Selected)
        {
            foreach (var change in changes)
                change.Selected = _Selected;

            changeList.RefreshItems();
            RefreshApplyButton();
        }

        private void ChooseReplacement()
        {
            var kind = Kind;
            if (kind == null) return;

            kind.ShowRowPicker(toButton.worldBound, (registry, id) =>
            {
                if (mode == Mode.Replace) Replacement = (registry, id);
                else Subject = (registry, id);

                Scan();
            });
        }

        private void ApplySelected()
        {
            var selected = changes.Where(e => e.Selected).ToList();
            if (selected.Count == 0) return;

            var kind = Kind;
            string title = mode == Mode.Replace ? "Replace References" : "Relink Missing References";
            int assetCount = selected.Select(e => e.AssetPath).Distinct().Count();

            if (!EditorUtility.DisplayDialog(
                    title,
                    $"Rewrite {selected.Count} reference(s) in {assetCount} asset(s)?\n\nAssets and closed scenes are saved. " +
                    "Open scenes are changed but not saved.",
                    "Rewrite",
                    "Cancel"))
                return;

            var result = RegistryReferenceTools.Apply(selected, $"{title} ({kind.DisplayName})");

            string message = result.Message;
            if (mode == Mode.Replace)
                message += $" \"{Describe(Subject.Registry, Subject.Id)}\" was not deleted. Delete it in the {kind.EditorName} if it's no longer needed.";

            Debug.Log($"Registry references: {message}");

            Scan();
            SetStatus(message, result.Skipped > 0 ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
            RefreshView();
        }

        private static void PingAsset(string _Path)
        {
            if (string.IsNullOrEmpty(_Path)) return;

            var asset = AssetDatabase.LoadMainAssetAtPath(_Path);
            if (asset != null)
                EditorGUIUtility.PingObject(asset);
        }
        #endregion
    }
}
