using System.Collections.Generic;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Drawer helpers shared by every kind's reference fields: how a missing reference reads, and prefab-safe removal
    /// of missing references from a single field or a list of references.
    /// </summary>
    /// <remarks>
    /// In a prefab asset (or Prefab Mode), a missing list entry is set to None instead of removed, so list indices
    /// don't shift under the overrides of instances and variants. <see cref="Compact"/> removes None entries, which
    /// callers warn about in that context.
    /// </remarks>
    public static class RegistryDrawerHelper
    {
        #region Display
        /// <summary>
        /// "&lt;last known name&gt; (deleted)".
        /// </summary>
        public static string GetMissingLabel(in RegistryReference _Reference)
            => $"{(string.IsNullOrEmpty(_Reference.Name) ? $"Unknown {_Reference.Kind?.RowNoun ?? "row"}" : _Reference.Name)} (deleted)";

        /// <summary>
        /// Why a missing reference doesn't resolve, for a tooltip.
        /// </summary>
        public static string GetMissingTooltip(in RegistryReference _Reference, RegistryStatus _Status)
        {
            string noun = _Reference.Kind?.RowNoun ?? "row";
            string reason = _Status == RegistryStatus.MissingRegistry
                ? $"Registry {_Reference.Registry} is not loaded."
                : $"This {noun} was deleted from its registry.";

            return $"{GetMissingLabel(_Reference)}\n{reason}\n{char.ToUpperInvariant(noun[0]) + noun[1..]} ID: {_Reference.Id}\nClick to remove it.";
        }
        #endregion



        #region Missing References
        /// <summary>
        /// Number of missing references (deleted, or in an unloaded registry) at <paramref name="_PropertyPath"/> across
        /// all targets. Each target is read on its own, since list contents can differ between targets.
        /// </summary>
        /// <param name="_IsList"> Whether the path points to a list of references, or to a single reference. </param>
        public static int CountMissing(IReadOnlyList<Object> _Targets, string _PropertyPath, bool _IsList)
        {
            int count = 0;
            foreach (var target in _Targets)
            {
                if (target == null) continue;

                using var serializedObject = new SerializedObject(target);
                foreach (int _ in FindMissing(serializedObject, _PropertyPath, _IsList))
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Remove every missing reference at <paramref name="_PropertyPath"/>, per target, in one Undo group: clear a
        /// single field, or remove list entries (set them to None in a prefab asset context).
        /// </summary>
        public static void RemoveMissing(IReadOnlyList<Object> _Targets, string _PropertyPath, bool _IsList, string _UndoName)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(_UndoName);

            foreach (var target in _Targets)
            {
                if (target == null) continue;

                using var serializedObject = new SerializedObject(target);
                var missing = new List<int>(FindMissing(serializedObject, _PropertyPath, _IsList));
                if (missing.Count == 0) continue;

                var property = serializedObject.FindProperty(_PropertyPath);

                if (!_IsList)
                {
                    RegistryProperties.Write(property, RegistryGuid.Zero, RegistryGuid.Zero, null);
                }
                else
                {
                    bool keepIndices = IsPrefabAssetContext(target);

                    // Highest index first, so earlier indices stay valid.
                    for (int i = missing.Count - 1; i >= 0; i--)
                    {
                        if (keepIndices)
                            RegistryProperties.Write(property.GetArrayElementAtIndex(missing[i]), RegistryGuid.Zero, RegistryGuid.Zero, null);
                        else
                            property.DeleteArrayElementAtIndex(missing[i]);
                    }
                }

                serializedObject.ApplyModifiedProperties();
            }

            Undo.CollapseUndoOperations(group);
        }

        /// <summary>
        /// Whether a list of references holds None entries that <see cref="Compact"/> would remove.
        /// </summary>
        public static bool HasNoneEntries(SerializedProperty _ListProperty)
        {
            for (int i = 0; i < _ListProperty.arraySize; i++)
                if (ReadId(_ListProperty.GetArrayElementAtIndex(i)).IsZero)
                    return true;

            return false;
        }

        /// <summary>
        /// Remove the None entries of a list of references, per target, in one Undo group. On a prefab asset this can
        /// move the overrides of instances and variants onto other entries: callers warn first, see
        /// <see cref="IsPrefabAssetContext"/>.
        /// </summary>
        public static void Compact(IReadOnlyList<Object> _Targets, string _ListPath, string _UndoName)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(_UndoName);

            foreach (var target in _Targets)
            {
                if (target == null) continue;

                using var serializedObject = new SerializedObject(target);
                var list = serializedObject.FindProperty(_ListPath);
                if (list == null || !list.isArray) continue;

                for (int i = list.arraySize - 1; i >= 0; i--)
                    if (ReadId(list.GetArrayElementAtIndex(i)).IsZero)
                        list.DeleteArrayElementAtIndex(i);

                serializedObject.ApplyModifiedProperties();
            }

            Undo.CollapseUndoOperations(group);
        }

        /// <summary>
        /// Whether <paramref name="_Target"/> belongs to a prefab asset, a prefab variant asset, or the contents of the
        /// prefab open in Prefab Mode. Removing list entries there shifts the overrides of instances.
        /// </summary>
        public static bool IsPrefabAssetContext(Object _Target)
        {
            if (_Target == null) return false;
            if (PrefabUtility.IsPartOfPrefabAsset(_Target)) return true;

            // Objects open in Prefab Mode are not prefab assets for PrefabUtility.
            var gameObject = _Target as GameObject ?? (_Target as Component)?.gameObject;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();

            return gameObject != null && stage != null && stage.IsPartOfPrefabContents(gameObject);
        }

        /// <summary>
        /// Whether <paramref name="_Property"/> can still be written, i.e. its target was not destroyed.
        /// </summary>
        public static bool IsAlive(SerializedProperty _Property)
        {
            try
            {
                return _Property.serializedObject.targetObject != null;
            }
            catch (System.Exception)
            {
                // The serialized object was disposed.
                return false;
            }
        }

        private static IEnumerable<int> FindMissing(SerializedObject _SerializedObject, string _PropertyPath, bool _IsList)
        {
            var property = _SerializedObject.FindProperty(_PropertyPath);
            if (property == null) yield break;

            if (!_IsList)
            {
                if (IsMissing(property))
                    yield return 0;

                yield break;
            }

            if (!property.isArray) yield break;

            for (int i = 0; i < property.arraySize; i++)
                if (IsMissing(property.GetArrayElementAtIndex(i)))
                    yield return i;
        }

        private static bool IsMissing(SerializedProperty _Property)
        {
            var kind = RegistryProperties.KindOf(_Property);
            if (kind == null) return false;

            var reference = RegistryProperties.Read(_Property, kind);
            return kind.IsMissing(reference.Registry, reference.Id);
        }

        private static RegistryGuid ReadId(SerializedProperty _Property)
        {
            var kind = RegistryProperties.KindOf(_Property);
            return kind != null ? RegistryProperties.Read(_Property, kind).Id : RegistryGuid.Zero;
        }
        #endregion
    }
}
