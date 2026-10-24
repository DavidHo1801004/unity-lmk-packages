using System.Diagnostics;

using UnityEngine;

namespace LMK.Runtime.Attributes
{
    /// <summary>
    /// Creates and serializes a per-object instance for this reference field.
    /// </summary>
    /// <remarks>
    /// <b>NOTE:</b> The target field must have <see cref="SerializeReference"/> attribute.
    /// On arrays and lists, the drawer is applied to each element.
    /// </remarks>
    [Conditional("UNITY_EDITOR")] // only read by the editor, stripped from player builds
    public class InlineInstancedAttribute : PropertyAttribute { }
}
