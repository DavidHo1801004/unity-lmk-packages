using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;
using LMK.Editor.Utilities;
using LMK.Editor.Styles;
using LMK.Runtime.GameplayTags;

using ToolbarButton = LMK.Editor.UIElements.ToolbarButton;

namespace LMK.Editor.GameplayTags
{
    using UIElements;

    /// <summary>
    /// Display all gameplay tags selection controls. <br/>
    /// Every tag is selected on its own: selecting "A.B" does not select "A". Only live tags are listed, grouped by registry.
    /// </summary>
    /// <remarks>
    /// One toolbar above the tag tree: search, Clear, and an options menu of its own.
    /// </remarks>
    internal sealed class GameplayTagSelectionWindow : EditorWindow
    {
        private const string UXML_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/GameplayTagSelectionWindowUXML.uxml";

        private static readonly Vector2 WINDOW_SIZE = new(350, 550);

        /// <summary>
        /// Called with the selected full tags, in selection order, every time the selection changes.
        /// </summary>
        public event Action<GameplayTag[]> OnTagSelectionChanged;

        /// <summary>
        /// Called by Clear instead of <see cref="OnTagSelectionChanged"/>, when set. For callers whose selection is not
        /// everything they hold, e.g. a container edited on several objects.
        /// </summary>
        public event Action OnSelectionCleared;


        private NamedSeparator windowLabel;
        private GameplayTagControlPanel controlPanel;
        private ToolbarSearchField searchField;
        private ToolbarButton clearButton;

        private readonly List<GameplayTag> selectedTags = new();

        public bool SingleSelection { get; set; }

        private string windowLabelValue;
        public string WindowLabel
        {
            get => windowLabelValue;
            set
            {
                windowLabelValue = value;

                if (windowLabel != null)
                    windowLabel.Text = windowLabelValue;
            }
        }



        /// <summary>
        /// Open a selection popup below the given GUI position.
        /// </summary>
        /// <param name="_GUILocation"> Top left corner of the window, in GUI coordinates. </param>
        /// <param name="_WindowLabel"> Title displayed in the window. </param>
        /// <param name="_SingleSelection"> Whether at most one tag can be selected. </param>
        /// <param name="_SelectedTags"> Initially selected full tags. </param>
        /// <param name="_OnSelectionChanged"> Called with the selected tags every time the selection changes. </param>
        /// <param name="_OnCleared"> Called by Clear instead of <paramref name="_OnSelectionChanged"/>, when set. </param>
        public static GameplayTagSelectionWindow Open(
            Vector2 _GUILocation,
            string _WindowLabel,
            bool _SingleSelection,
            IEnumerable<GameplayTag> _SelectedTags,
            Action<GameplayTag[]> _OnSelectionChanged,
            Action _OnCleared = null)
        {
            var window = CreateInstance<GameplayTagSelectionWindow>();
            window.SingleSelection = _SingleSelection;
            window.WindowLabel = _WindowLabel;
            window.OnTagSelectionChanged += _OnSelectionChanged;
            if (_OnCleared != null)
                window.OnSelectionCleared += _OnCleared;

            if (_SelectedTags != null)
                foreach (var tag in _SelectedTags)
                    window.AddSelection(tag);

            window.ShowPopup();
            window.position = new Rect(GUIUtility.GUIToScreenPoint(_GUILocation), WINDOW_SIZE);

            return window;
        }



        private void OnLostFocus()
        {
            Close();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            LMKTheme.Apply(root);

            root.Add(LMKAssetUtility.Load<VisualTreeAsset>(UXML_PATH)?.Instantiate());

            // Window label
            windowLabel = root.Q<NamedSeparator>("window-label");
            windowLabel.Text = WindowLabel;

            // Tag tree
            controlPanel = root.Q<GameplayTagControlPanel>();
            controlPanel.ShowHelpBoxMessage = MessageBoxDisplay.Never;

            CreateToolbar(root.Q("toolbar"));

            controlPanel.RegisterTagTreeRedrawnCallback(SyncSelection);
            controlPanel.RegisterTagSelectBoxCallback(OnSelectBoxValueChanged); // redraws the tree

            UpdateClearButton();

            // Type to search as soon as the popup opens.
            searchField.schedule.Execute(() => searchField.Q<TextField>()?.Focus());
        }

        /// <summary>
        /// Search, Clear and options, in Unity's toolbar.
        /// </summary>
        private void CreateToolbar(VisualElement _Toolbar)
        {
            searchField = new ToolbarSearchField { name = "search-field", tooltip = GameplayTagControlPanel.SEARCH_HELP };
            searchField.RegisterValueChangedCallback(e => controlPanel.SetFilter(e.newValue));
            _Toolbar.Add(searchField);

            clearButton = new ToolbarButton(false, ClearSelection) { name = "clear-button", text = "Clear", tooltip = "Clear selection" };
            _Toolbar.Add(clearButton);

            ToolbarButton optionsButton = null;
            optionsButton = new ToolbarButton(false, () => ShowOptionsMenu(optionsButton))
            {
                name = "options-button",
                iconImage = LMKAssetUtility.Load<Texture2D>(GameplayTagManagerToolbar.GEAR_ICON_PATH),
                tooltip = "Options",
            };
            _Toolbar.Add(optionsButton);
        }

        private void ShowOptionsMenu(VisualElement _Anchor)
        {
            GenericDropdownMenu menu = new();

            menu.AddItem("Collapse All", false, () => controlPanel?.TagTree?.SetExpanded(false));
            menu.AddItem("Expand All", false, () => controlPanel?.TagTree?.SetExpanded(true));
            menu.AddSeparator(null);
            menu.AddItem("Open Tag Manager", false, GameplayTagManagerWindow.Open);

            menu.DropDown(
                _Anchor.ChangeCoordinatesTo(_Anchor.panel.visualTree, _Anchor.contentRect),
                _Anchor,
                DropdownMenuSizeMode.Auto);
        }



        #region Callbacks
        private void ClearSelection()
        {
            selectedTags.Clear();
            SyncSelection();

            if (OnSelectionCleared != null)
                OnSelectionCleared.Invoke();
            else
                NotifySelectionChanged();

            UpdateClearButton();
        }

        private void OnSelectBoxValueChanged(TagElementSelectCallback _Callback, bool _NewValue)
        {
            var tag = _Callback.tagNode.Tag;

            if (_NewValue)
            {
                if (SingleSelection)
                    selectedTags.Clear();

                AddSelection(tag);
            }
            else
            {
                selectedTags.Remove(tag);
            }

            SyncSelection();
            NotifySelectionChanged();
            UpdateClearButton();
        }
        #endregion

        #region Utilities
        private void AddSelection(GameplayTag _Tag)
        {
            if (_Tag.IsNone || selectedTags.Contains(_Tag)) return;

            if (SingleSelection)
                selectedTags.Clear();

            selectedTags.Add(_Tag);
        }

        // Update every select box, and the "has selected sub tags" state of branches.
        private void SyncSelection()
        {
            controlPanel?.TagTree?.SyncSelection(selectedTags);
        }

        private void NotifySelectionChanged()
        {
            OnTagSelectionChanged?.Invoke(selectedTags.ToArray());
        }

        // Clear stays available while the caller handles it: its targets can hold tags the picker doesn't show.
        private void UpdateClearButton()
        {
            clearButton?.SetEnabled(selectedTags.Count > 0 || OnSelectionCleared != null);
        }
        #endregion
    }
}
