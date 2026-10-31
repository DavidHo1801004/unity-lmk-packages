using UnityEngine;

namespace LMK.Editor.Tests
{
    /// <summary>
    /// Target of the object reference tests. In its own file so it has a script asset.
    /// </summary>
    public class ObjectLabelTestHost : ScriptableObject
    {
        public Texture2D texture;
        public ObjectLabelTestHost self;
        public float number;
    }
}
