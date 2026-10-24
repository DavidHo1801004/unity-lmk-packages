using System;
using System.Diagnostics;

namespace LMK.Runtime.Attributes
{
    /// <summary>
    /// Override <see cref="CanReferenceInlineAttribute"/> propagated from parent class.
    /// </summary>
    [Conditional("UNITY_EDITOR")] // only read by the editor, stripped from player builds
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class CanNotReferenceInlineAttribute : Attribute { }
}
