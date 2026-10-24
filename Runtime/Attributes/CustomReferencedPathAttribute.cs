using System;
using System.Diagnostics;

namespace LMK.Runtime.Attributes
{
    /// <summary>
    /// Define a custom path/name for abstract classes derived from any classes marked with <see cref="CanReferenceInlineAttribute"/>. <br/>
    /// This will be used in UnityEditor to display the path/name of the target class on dropdown selection.
    /// </summary>
    [Conditional("UNITY_EDITOR")] // only read by the editor, stripped from player builds
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class CustomReferencedPathAttribute : Attribute
    {
        /// <summary>
        /// Custom name of the class in the editor. <br/>
        /// If not set, default name will be the typename of the owner class.
        /// </summary>
        public string CustomName { get; private set; }

        /// <summary>
        /// Tool tip to display while hover over select option in <see cref="InlineInstancedAttribute"/> dropdown.
        /// </summary>
        public string ToolTip { get; private set; }

        /// <summary>
        /// Optional icon displayed next to the item in <see cref="InlineInstancedAttribute"/> dropdown. Resolved in order as: <br/>
        /// 1. A built-in editor icon name, e.g. <c>"d_ScriptableObject Icon"</c>. <br/>
        /// 2. A texture path inside a <c>Resources</c> folder, without extension. <br/>
        /// 3. A texture asset path, e.g. <c>"Assets/Icons/MyIcon.png"</c>.
        /// </summary>
        public string IconPath { get; private set; }

        public CustomReferencedPathAttribute(string _CustomName = null, string _ToolTip = null, string _IconPath = null)
        {
            CustomName = _CustomName;
            ToolTip = _ToolTip;
            IconPath = _IconPath;
        }
    }
}
