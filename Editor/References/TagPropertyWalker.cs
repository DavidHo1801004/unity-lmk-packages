using System.Collections.Generic;

using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Finds the <see cref="GameplayTag"/> properties of objects: fields, list entries, nested structs and managed
    /// references. The tag properties of the registry core's <see cref="RegistryPropertyWalker"/>.
    /// </summary>
    internal static class TagPropertyWalker
    {
        /// <summary>
        /// Every tag property of <paramref name="_SerializedObject"/>, as copies that stay valid while walking on.
        /// </summary>
        /// <param name="_DefinedHereOnly">
        /// Skip values a prefab instance inherits from its prefab: only overridden values, and objects that are not
        /// part of a prefab instance, are returned. The prefab holds the others itself.
        /// </param>
        public static IEnumerable<SerializedProperty> Enumerate(SerializedObject _SerializedObject, bool _DefinedHereOnly = false)
        {
            foreach (var (property, kind) in RegistryPropertyWalker.Enumerate(_SerializedObject, _DefinedHereOnly))
                if (kind == TagKind.Instance)
                    yield return property;
        }

        /// <summary>
        /// Whether the values of <paramref name="_Target"/> come from a prefab, unless overridden.
        /// </summary>
        public static bool IsInheritedFromPrefab(Object _Target) => RegistryPropertyWalker.IsInheritedFromPrefab(_Target);

        /// <summary>
        /// The objects of an asset file that can hold tags.
        /// </summary>
        public static IEnumerable<Object> GetAssetObjects(string _AssetPath) => RegistryPropertyWalker.GetAssetObjects(_AssetPath);

        /// <summary>
        /// The scripts of a loaded scene, inactive ones included. Missing scripts are skipped.
        /// </summary>
        public static IEnumerable<MonoBehaviour> GetSceneObjects(Scene _Scene) => RegistryPropertyWalker.GetSceneObjects(_Scene);

        /// <summary>
        /// A readable name for an object holding tags: its hierarchy path and type for a component, its name otherwise.
        /// </summary>
        public static string Describe(Object _Object) => RegistryPropertyWalker.Describe(_Object);
    }
}
