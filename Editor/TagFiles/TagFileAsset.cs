using UnityEngine;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Main object of an imported tag file (<c>*.tags</c>). It holds nothing: the inspector reads the file itself,
    /// so tag files stay plain import data and no copy of their tags is stored.
    /// </summary>
    internal sealed class TagFileAsset : ScriptableObject
    {
    }
}
