using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// The reference tools: Find References lists the saved assets that use a tag. Relink and
    /// Replace preview every reference they would rewrite, and rewrite only the selected ones.
    /// </summary>
    internal sealed class TagReferenceWindow : EditorWindow
    {
        private enum Mode
        {
            References,
            Relink,
            Replace,
        }

        [SerializeField]
        private Mode mode;

        /// <summary>
        /// The tag whose references are listed, or replaced.
        /// </summary>
        [SerializeField]
        private GameplayTag subject;

        /// <summary>
        /// The tag references are replaced with.
        /// </summary>
        [SerializeField]
        private GameplayTag replacement;

        private readonly List<string> assets = new();
        private readonly List<TagReferenceChange> changes = new();
        private bool scanned;
        private string statusText;
        private HelpBoxMessageType statusType;

        private Label heading;
        private HelpBox info;
        private VisualElement replaceRow;
        private Label fromLabel;
        private Button toButton;
        private ListView assetList;
        private ListView changeList;
        private VisualElement selectionButtons;
        private HelpBox status;
        private Button applyButton;



        #region Opening
        /// <summary>
        /// List the saved assets that reference <paramref name="_Tag"/>.
        /// </summary>
        public static void ShowReferences(GameplayTag _Tag) => Open(Mode.References, _Tag, true);

        /// <summary>
        /// Preview relinking the missing references whose last known name is a live tag.
        /// </summary>
        public static void ShowRelink() => Open(Mode.Relink, GameplayTag.None, true);

        /// <summary>
        /// Replace the references to <paramref name="_From"/> with a tag the user picks.
        /// </summary>
        public static void ShowReplace(GameplayTag _From) => Open(Mode.Replace, _From, false);

        private static void Open(Mode _Mode, GameplayTag _Subject, bool _Scan)
        {
            var window = GetWindow<TagReferenceWindow>(false, "Tag References");
            window.minSize = new Vector2(480, 320);

            window.mode = _Mode;
            window.subject = _Subject;
            window.replacement = GameplayTag.None;
            window.ClearResults();

            if (_Scan)
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

            heading = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13, marginBottom = 4 } };
            root.Add(heading);

            info = new HelpBox(string.Empty, HelpBoxMessageType.Info) { style = { marginBottom = 4 } };
            root.Add(info);

            replaceRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
            replaceRow.Add(new Label("Replace") { style = { marginRight = 4 } });
            fromLabel = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 4 } };
            replaceRow.Add(fromLabel);
            replaceRow.Add(new Label("with") { style = { marginRight = 4 } });
            toButton = new Button(ChooseReplacement) { tooltip = "Choose the tag the references will point to." };
            replaceRow.Add(toButton);
            root.Add(replaceRow);

            assetList = new ListView(assets, 18, () => new Label(), (e, i) => ((Label)e).text = assets[i])
            {
                selectionType = SelectionType.Single,
                showBorder = true,
                style = { flexGrow = 1, minHeight = 80 },
            };
            assetList.selectionChanged += _ => PingAsset(assetList.selectedIndex >= 0 ? assets[assetList.selectedIndex] : null);
            root.Add(assetList);

            changeList = new ListView(changes, 20, MakeChangeRow, BindChangeRow)
            {
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
            applyButton = new Button(ApplySelected);
            footer.Add(applyButton);
            root.Add(footer);

            status = new HelpBox(string.Empty, HelpBoxMessageType.Info) { style = { marginTop = 4 } };
            root.Add(status);

            RefreshView();
        }

        private VisualElement MakeChangeRow()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            var toggle = new Toggle { style = { marginRight = 4 } };
            toggle.RegisterValueChangedCallback(e =>
            {
                if (toggle.userData is TagReferenceChange change)
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

        private static string Describe(GameplayTag _Tag)
        {
            if (_Tag.IsNone) return "None";
            return _Tag.IsLive ? _Tag.Path : GameplayTagDrawerHelper.GetMissingLabel(_Tag);
        }

        /// <summary>
        /// Show the parts of the current mode, and its results.
        /// </summary>
        private void RefreshView()
        {
            if (heading == null) return;

            bool listsAssets = mode == Mode.References;
            bool replacing = mode == Mode.Replace;

            switch (mode)
            {
                case Mode.References:
                    heading.text = $"References to \"{Describe(subject)}\"";
                    info.text = "Saved assets that reference this tag. Values a prefab instance inherits are listed for the prefab. " +
                                "Unsaved scenes and assets are not included. Select an asset to ping it.";
                    break;

                case Mode.Relink:
                    heading.text = "Relink Missing References";
                    info.text = "References that show as (deleted), whose last known name is a live tag: in their own registry first, " +
                                "then in the main registry. Only the selected references are rewritten. Assets are saved; open scenes " +
                                "are changed but not saved.";
                    break;

                case Mode.Replace:
                    heading.text = "Replace References";
                    info.text = "Every saved reference to the first tag is rewritten to the second. The first tag is not deleted. " +
                                "Only the selected references are rewritten. Assets are saved; open scenes are changed but not saved.";
                    break;
            }

            replaceRow.style.display = replacing ? DisplayStyle.Flex : DisplayStyle.None;
            fromLabel.text = Describe(subject);
            toButton.text = replacement.IsNone ? "Choose Tag..." : Describe(replacement);

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
            bool complete = true;

            switch (mode)
            {
                case Mode.References:
                {
                    if (subject.IsNone) break;

                    complete = TagReferenceIndex.TryFindAssets(new[] { subject }, true, out var found);
                    assets.AddRange(found);
                    SetStatus($"{assets.Count} saved asset(s) reference \"{Describe(subject)}\".");
                    break;
                }

                case Mode.Relink:
                {
                    complete = TagReferenceTools.PlanRelink(true, out var planned);
                    changes.AddRange(planned);
                    SetStatus(changes.Count == 0
                        ? "No missing reference matches a live tag by name."
                        : $"{changes.Count} missing reference(s) in {CountAssets()} asset(s) can be relinked.");
                    break;
                }

                case Mode.Replace:
                {
                    if (replacement.IsNone)
                    {
                        SetStatus("Choose the tag to replace the references with.");
                        break;
                    }

                    if (replacement.Equals(subject))
                    {
                        SetStatus("Choose a different tag.", HelpBoxMessageType.Warning);
                        break;
                    }

                    complete = TagReferenceTools.PlanReplace(subject, replacement, true, out var planned);
                    changes.AddRange(planned);
                    SetStatus($"{changes.Count} reference(s) to \"{Describe(subject)}\" in {CountAssets()} asset(s).");
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
            GameplayTagSelectionWindow.Open(
                _GUILocation: new Vector2(toButton.worldBound.xMin, toButton.worldBound.yMax),
                _WindowLabel: "REPLACE WITH",
                _SingleSelection: true,
                _SelectedTags: replacement.IsNone ? null : new[] { replacement },
                _OnSelectionChanged: tags =>
                {
                    replacement = tags.Length > 0 ? tags[0] : GameplayTag.None;
                    Scan();
                });
        }

        private void ApplySelected()
        {
            var selected = changes.Where(e => e.Selected).ToList();
            if (selected.Count == 0) return;

            string title = mode == Mode.Replace ? "Replace References" : "Relink Missing References";
            int assetCount = selected.Select(e => e.AssetPath).Distinct().Count();

            if (!EditorUtility.DisplayDialog(
                    title,
                    $"Rewrite {selected.Count} reference(s) in {assetCount} asset(s)?\n\nAssets and closed scenes are saved. " +
                    "Open scenes are changed but not saved.",
                    "Rewrite",
                    "Cancel"))
                return;

            var result = TagReferenceTools.Apply(selected, mode == Mode.Replace ? TagReferenceTools.REPLACE_UNDO : TagReferenceTools.RELINK_UNDO);

            string message = result.Message;
            if (mode == Mode.Replace)
                message += $" \"{Describe(subject)}\" was not deleted. Delete it in the Gameplay Tag Manager if it's no longer needed.";

            Debug.Log($"Gameplay tag references: {message}");

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
