using UnityEngine;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Main object of an imported tag file (<c>*.tags</c>). It holds nothing: the inspector reads the file itself,
    /// so tag files stay plain import data and no copy of their tags is stored.
    /// </summary>
    /// <remarks>
    /// The importer gives the asset its Project window icon; the class's icon is the one its Inspector header shows.
    /// </remarks>
    [Icon(ICON_PATH)]
    internal sealed class TagFileAsset : ScriptableObject
    {
        internal const string ICON_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/Icons/TagFile.png";
    }
}
