using System;
using System.Diagnostics;

namespace LMK.Runtime.Attributes
{
    /// <summary>
    /// Indicates that instances of this class and its child classes can be created directly from the editor
    /// in fields marked with <see cref="UnityEngine.SerializeReference"/> and <see cref="InlineInstancedAttribute"/>. <br/>
    /// The field type can be this class, any of its base classes or interfaces, or an intermediate child class. <br/>
    /// Child classes marked with <see cref="CanNotReferenceInlineAttribute"/> are excluded;
    /// the closest attribute in the inheritance chain wins.
    /// </summary>
    /// <remarks>
    /// <b>NOTE:</b> Only non-abstract, non-generic <see cref="SerializableAttribute"/> classes with a parameterless
    /// constructor (any visibility) are listed. Classes from editor-only assemblies are ignored.
    /// </remarks>
    [Conditional("UNITY_EDITOR")] // only read by the editor, stripped from player builds
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class CanReferenceInlineAttribute : Attribute { }
}
