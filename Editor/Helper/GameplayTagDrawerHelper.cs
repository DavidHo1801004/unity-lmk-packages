using System.Collections.Generic;
using System.Text;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Serialized-property logic shared by the tag drawers: reading and writing references, how they display,
    /// and the removal of missing references.
    /// </summary>
    /// <remarks>
    /// Kept free of UI so it can be tested directly. Every write goes through <see cref="SerializedObject"/>,
    /// so Undo and prefab overrides behave as usual.
    /// </remarks>
    internal static class GameplayTagDrawerHelper
    {
        public const string REGISTRY_PROPERTY = "_registry";
        public const string ID_PROPERTY = "_id";
        public const string NAME_PROPERTY = "_lastKnownName";
        public const string HI_PROPERTY = "_hi";
        public const string LO_PROPERTY = "_lo";

        /// <summary>
        /// Serialized field name of the tag list in <see cref="GameplayTagContainer"/>.
        /// </summary>
        public const string CONTAINER_TAGS_PROPERTY = "_tags";

        public const string USS_CLASS_NONE = "gameplay-tag--none";
        public const string USS_CLASS_IMPLICIT = "gameplay-tag--implicit";
        public const string USS_CLASS_MISSING = "gameplay-tag--missing";



        #region Read and Write
        /// <summary>
        /// Whether <paramref name="_Property"/> is a single <see cref="GameplayTag"/> (not a list of them).
        /// </summary>
        public static bool IsTagProperty(SerializedProperty _Property)
            => _Property.propertyType == SerializedPropertyType.Generic
            && !_Property.isArray
            && _Property.type == nameof(GameplayTag)
            && _Property.FindPropertyRelative(ID_PROPERTY) != null
            && _Property.FindPropertyRelative(REGISTRY_PROPERTY) != null;

        /// <summary>
        /// Read a <see cref="GameplayTag"/> property.
        /// </summary>
        public static GameplayTag Read(SerializedProperty _TagProperty)
        {
            var registry = ReadGuid(_TagProperty.FindPropertyRelative(REGISTRY_PROPERTY));
            var id = ReadGuid(_TagProperty.FindPropertyRelative(ID_PROPERTY));

            return GameplayTag.FromId(registry, id, _TagProperty.FindPropertyRelative(NAME_PROPERTY).stringValue);
        }

        /// <summary>
        /// Write all three fields of a <see cref="GameplayTag"/> property: registry ID, tag ID and current name.
        /// Does not apply the modified properties.
        /// </summary>
        public static void Write(SerializedProperty _TagProperty, GameplayTag _Tag)
        {
            WriteGuid(_TagProperty.FindPropertyRelative(REGISTRY_PROPERTY), _Tag.IsNone ? TagGuid.Zero : _Tag.RegistryId);
            WriteGuid(_TagProperty.FindPropertyRelative(ID_PROPERTY), _Tag.Id);
            _TagProperty.FindPropertyRelative(NAME_PROPERTY).stringValue = _Tag.IsNone ? string.Empty : _Tag.Path;
        }

        private static TagGuid ReadGuid(SerializedProperty _Property)
            => new(_Property.FindPropertyRelative(HI_PROPERTY).ulongValue, _Property.FindPropertyRelative(LO_PROPERTY).ulongValue);

        private static void WriteGuid(SerializedProperty _Property, TagGuid _Value)
        {
            _Property.FindPropertyRelative(HI_PROPERTY).ulongValue = _Value.Hi;
            _Property.FindPropertyRelative(LO_PROPERTY).ulongValue = _Value.Lo;
        }

        /// <summary>
        /// The stored tags of a container's tag list, None entries included, in list order.
        /// </summary>
        public static List<GameplayTag> ReadList(SerializedProperty _TagsProperty)
        {
            var result = new List<GameplayTag>(_TagsProperty.arraySize);
            for (int i = 0; i < _TagsProperty.arraySize; i++)
                result.Add(Read(_TagsProperty.GetArrayElementAtIndex(i)));

            return result;
        }
        #endregion



        #region Display
        /// <summary>
        /// Show a tag in a label: its current path, or "(deleted)" with an explanation when it doesn't resolve.
        /// </summary>
        public static void ApplyDisplay(Label _Label, GameplayTag _Tag)
        {
            var resolution = _Tag.Resolve();

            _Label.EnableInClassList(USS_CLASS_NONE, resolution.Status == TagStatus.None);
            _Label.EnableInClassList(USS_CLASS_MISSING, resolution.IsMissing);
            _Label.EnableInClassList(USS_CLASS_IMPLICIT, false);

            switch (resolution.Status)
            {
                case TagStatus.None:
                    _Label.text = "None";
                    _Label.tooltip = string.Empty;
                    break;

                case TagStatus.Live:
                    ApplyLiveDisplay(_Label, _Tag, resolution);
                    break;

                default:
                    _Label.text = GetMissingLabel(_Tag);
                    _Label.tooltip = GetMissingTooltip(_Tag, resolution.Status);
                    break;
            }
        }

        private static void ApplyLiveDisplay(Label _Label, GameplayTag _Tag, TagResolution _Resolution)
        {
            var database = GameplayTagDatabase.Active;
            string path = database.GetPath(_Resolution.Handle);
            bool isImplicit = database.IsImplicit(_Resolution.Handle);

            var tooltip = new StringBuilder(path);
            tooltip.Append("\nRegistry: ").Append(database.GetRegistryName(_Tag.RegistryId));

            var registry = TagRegistryService.Snapshot.Find(_Tag.RegistryId);
            if (registry != null && registry.TryGetRow(_Tag.Id, out var row) && !string.IsNullOrWhiteSpace(row.description))
                tooltip.Append('\n').Append(row.description);

            if (isImplicit)
                tooltip.Append("\nImplicit tag: exists only as the parent of added tags.");

            _Label.text = path;
            _Label.tooltip = tooltip.ToString();
            _Label.EnableInClassList(USS_CLASS_IMPLICIT, isImplicit);
        }

        /// <summary>
        /// "&lt;last known name&gt; (deleted)".
        /// </summary>
        public static string GetMissingLabel(GameplayTag _Tag)
            => $"{(string.IsNullOrEmpty(_Tag.LastKnownName) ? "Unknown tag" : _Tag.LastKnownName)} (deleted)";

        private static string GetMissingTooltip(GameplayTag _Tag, TagStatus _Status)
        {
            string reason = _Status == TagStatus.MissingRegistry
                ? $"Registry {_Tag.RegistryId} is not loaded."
                : "This tag was deleted from its registry.";

            return $"{GetMissingLabel(_Tag)}\n{reason}\nTag ID: {_Tag.Id}\nClick to remove it.";
        }
        #endregion



        #region Missing References
        /// <summary>
        /// Number of missing references (deleted, or in an unloaded registry) at <paramref name="_PropertyPath"/>
        /// across all targets. Each target is read on its own, since list contents can differ between targets.
        /// </summary>
        /// <param name="_IsContainer"> Whether the path points to a <see cref="GameplayTagContainer"/>, or to a single <see cref="GameplayTag"/>. </param>
        public static int CountMissing(IReadOnlyList<Object> _Targets, string _PropertyPath, bool _IsContainer)
        {
            int count = 0;
            foreach (var target in _Targets)
            {
                if (target == null) continue;

                using var serializedObject = new SerializedObject(target);
                foreach (int index in FindMissing(serializedObject, _PropertyPath, _IsContainer))
                    count++;
            }

            return count;
        }

        /// <summary>
        /// The one menu item offered while missing references are present.
        /// </summary>
        public static string GetRemoveMissingLabel(bool _IsContainer, int _Count)
            => _IsContainer ? $"Remove {_Count} missing tag(s)" : "Clear missing tag";

        /// <summary>
        /// Remove every missing reference at <paramref name="_PropertyPath"/>, per target, in one Undo group. <br/>
        /// In a prefab asset (or Prefab Mode) container, a missing entry is set to None instead of removed, so list
        /// indices don't shift under the overrides of instances and variants.
        /// </summary>
        public static void RemoveMissing(IReadOnlyList<Object> _Targets, string _PropertyPath, bool _IsContainer)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(_IsContainer ? "Remove Missing Gameplay Tags" : "Clear Missing Gameplay Tag");

            foreach (var target in _Targets)
            {
                if (target == null) continue;

                using var serializedObject = new SerializedObject(target);
                var missing = new List<int>(FindMissing(serializedObject, _PropertyPath, _IsContainer));
                if (missing.Count == 0) continue;

                var property = serializedObject.FindProperty(_PropertyPath);

                if (!_IsContainer)
                {
                    Write(property, GameplayTag.None);
                }
                else
                {
                    var tags = property.FindPropertyRelative(CONTAINER_TAGS_PROPERTY);
                    bool keepIndices = IsPrefabAssetContext(target);

                    // Highest index first, so earlier indices stay valid.
                    for (int i = missing.Count - 1; i >= 0; i--)
                    {
                        if (keepIndices)
                            Write(tags.GetArrayElementAtIndex(missing[i]), GameplayTag.None);
                        else
                            tags.DeleteArrayElementAtIndex(missing[i]);
                    }
                }

                serializedObject.ApplyModifiedProperties();
            }

            Undo.CollapseUndoOperations(group);
        }

        /// <summary>
        /// Whether a container holds None entries that <see cref="Compact"/> would remove.
        /// </summary>
        public static bool HasNoneEntries(SerializedProperty _ContainerProperty)
        {
            var tags = _ContainerProperty.FindPropertyRelative(CONTAINER_TAGS_PROPERTY);
            for (int i = 0; i < tags.arraySize; i++)
                if (Read(tags.GetArrayElementAtIndex(i)).IsNone)
                    return true;

            return false;
        }

        /// <summary>
        /// Remove the None entries of a container, per target, in one Undo group. On a prefab asset this can move
        /// the overrides of instances and variants onto other entries: callers warn first, see
        /// <see cref="IsPrefabAssetContext"/>.
        /// </summary>
        public static void Compact(IReadOnlyList<Object> _Targets, string _PropertyPath)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Compact Gameplay Tag List");

            foreach (var target in _Targets)
            {
                if (target == null) continue;

                using var serializedObject = new SerializedObject(target);
                var tags = serializedObject.FindProperty(_PropertyPath)?.FindPropertyRelative(CONTAINER_TAGS_PROPERTY);
                if (tags == null) continue;

                for (int i = tags.arraySize - 1; i >= 0; i--)
                    if (Read(tags.GetArrayElementAtIndex(i)).IsNone)
                        tags.DeleteArrayElementAtIndex(i);

                serializedObject.ApplyModifiedProperties();
            }

            Undo.CollapseUndoOperations(group);
        }

        /// <summary>
        /// Whether <paramref name="_Target"/> belongs to a prefab asset, a prefab variant asset, or the contents
        /// of the prefab open in Prefab Mode. Removing list entries there shifts the overrides of instances.
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

        private static IEnumerable<int> FindMissing(SerializedObject _SerializedObject, string _PropertyPath, bool _IsContainer)
        {
            var property = _SerializedObject.FindProperty(_PropertyPath);
            if (property == null) yield break;

            if (!_IsContainer)
            {
                if (Read(property).Resolve().IsMissing)
                    yield return 0;

                yield break;
            }

            var tags = property.FindPropertyRelative(CONTAINER_TAGS_PROPERTY);
            if (tags == null) yield break;

            for (int i = 0; i < tags.arraySize; i++)
                if (Read(tags.GetArrayElementAtIndex(i)).Resolve().IsMissing)
                    yield return i;
        }
        #endregion



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
    }
}
