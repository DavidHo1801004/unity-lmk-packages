using System.Collections.Generic;

using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Finds the <see cref="GameplayTag"/> properties of objects: fields, list entries, nested structs and managed
    /// references. Shared by builds, the reference index and the reference tools.
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
            var target = _SerializedObject.targetObject;
            bool inherited = _DefinedHereOnly && IsInheritedFromPrefab(target);

            var iterator = _SerializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                // Cheap checks first. Then read through a copy: relative lookups on the live iterator are unreliable.
                bool candidate = iterator.propertyType == SerializedPropertyType.Generic && !iterator.isArray && iterator.type == nameof(GameplayTag);
                var property = candidate ? iterator.Copy() : null;
                if (property == null || !GameplayTagDrawerHelper.IsTagProperty(property))
                {
                    enterChildren = iterator.propertyType != SerializedPropertyType.String;
                    continue;
                }

                // A tag: don't walk into its fields.
                enterChildren = false;

                if (inherited && !property.prefabOverride) continue;

                yield return property;
            }
        }

        /// <summary>
        /// Whether the values of <paramref name="_Target"/> come from a prefab, unless overridden: it is part of a
        /// prefab instance, and not a component added to that instance.
        /// </summary>
        public static bool IsInheritedFromPrefab(Object _Target)
            => _Target != null
            && PrefabUtility.IsPartOfPrefabInstance(_Target)
            && !(_Target is Component component && PrefabUtility.IsAddedComponentOverride(component));

        /// <summary>
        /// The objects of an asset file that can hold tags: its scripts (ScriptableObjects, and the MonoBehaviours
        /// of a prefab), except the baked database. Native objects can't hold tags, and walking them can be slow.
        /// </summary>
        public static IEnumerable<Object> GetAssetObjects(string _AssetPath)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(_AssetPath))
            {
                if (asset is not (MonoBehaviour or ScriptableObject) || asset is GameplayTagDatabase) continue;
                yield return asset;
            }
        }

        /// <summary>
        /// The scripts of a loaded scene, inactive ones included. Missing scripts are skipped.
        /// </summary>
        public static IEnumerable<MonoBehaviour> GetSceneObjects(Scene _Scene)
        {
            foreach (var root in _Scene.GetRootGameObjects())
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour != null)
                        yield return behaviour;
        }

        /// <summary>
        /// A readable name for an object holding tags: its hierarchy path and type for a component, its name otherwise.
        /// </summary>
        public static string Describe(Object _Object)
        {
            if (_Object is not Component component)
                return _Object != null ? $"{_Object.name} ({_Object.GetType().Name})" : "(missing)";

            var names = new List<string>();
            for (var current = component.transform; current != null; current = current.parent)
                names.Add(current.name);

            names.Reverse();
            return $"{string.Join("/", names)} ({component.GetType().Name})";
        }
    }
}
