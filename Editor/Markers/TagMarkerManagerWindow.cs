using UnityEditor;
using UnityEngine;

using LMK.Editor.Styles;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Manage Markers: a small utility window with every marker, to rename, recolour, delete, add and reorder them.
    /// A window rather than a popup, because a colour picker takes focus, which closes a popup.
    /// </summary>
    internal sealed class TagMarkerManagerWindow : EditorWindow
    {
        private const string TITLE = "Gameplay Tag Markers";

        private static readonly Vector2 MIN_SIZE = new(340f, 220f);

        [MenuItem("Tools/LMK/Gameplay Tags/Manage Markers...", priority = 80)]
        public static void Open()
        {
            var window = GetWindow<TagMarkerManagerWindow>(true, TITLE, true);
            window.minSize = MIN_SIZE;
        }

        private void CreateGUI()
        {
            LMKTheme.Apply(rootVisualElement);

            var list = new TagMarkerListView();
            list.style.flexGrow = 1;
            rootVisualElement.Add(list);
        }
    }
}
