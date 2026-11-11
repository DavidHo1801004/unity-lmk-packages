using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;
using LMK.Editor.Utilities;
using LMK.Editor.Styles;
using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    using UIElements;

    /// <summary>
    /// Display all gameplay tags selection controls. <br/>
    /// Every tag is selected on its own: selecting "A.B" does not select "A". Only live tags are listed, grouped by registry.
    /// </summary>
    internal sealed class GameplayTagSelectionWindow : EditorWindow
    {
        private const string UXML_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/GameplayTagSelectionWindowUXML.uxml";

        private static readonly Vector2 WINDOW_SIZE = new(350, 550);

        /// <summary>
        /// Called with the selected full tags, in selection order, every time the selection changes.
        /// </summary>
        public event Action<GameplayTag[]> OnTagSelectionChanged;


        private NamedSeparator windowLabel;
        private GameplayTagControlPanel controlPanel;

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
        public static GameplayTagSelectionWindow Open(
            Vector2 _GUILocation,
            string _WindowLabel,
            bool _SingleSelection,
            IEnumerable<GameplayTag> _SelectedTags,
            Action<GameplayTag[]> _OnSelectionChanged)
        {
            var window = CreateInstance<GameplayTagSelectionWindow>();
            window.SingleSelection = _SingleSelection;
            window.WindowLabel = _WindowLabel;
            window.OnTagSelectionChanged += _OnSelectionChanged;

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

            // Clear selection button
            root.Q<Button>("clear-selection").clicked += ClearSelection;

            // Open gameplay tag manager window
            root.Q<Button>("open-manager-window").clicked += GameplayTagManagerWindow.Open;

            // Tag tree
            controlPanel = root.Q<GameplayTagControlPanel>();
            controlPanel.ShowHelpBoxMessage = MessageBoxDisplay.Never;

            controlPanel.RegisterTagTreeRedrawnCallback(SyncSelection);
            controlPanel.RegisterTagSelectBoxCallback(OnSelectBoxValueChanged); // redraws the tree
        }



        #region Callbacks
        private void ClearSelection()
        {
            selectedTags.Clear();
            SyncSelection();

            NotifySelectionChanged();
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
        #endregion
    }
}
