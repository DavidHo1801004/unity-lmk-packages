using System.Collections.Generic;
using System.Text;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Serialized-property logic shared by the tag drawers: reading and writing references, how they display,
    /// and the removal of missing references. The reference fields and the prefab-safe removal are the registry
    /// core's (<see cref="RegistryProperties"/>, <see cref="RegistryDrawerHelper"/>).
    /// </summary>
    /// <remarks>
    /// Kept free of UI so it can be tested directly. Every write goes through <see cref="SerializedObject"/>,
    /// so Undo and prefab overrides behave as usual.
    /// </remarks>
    internal static class GameplayTagDrawerHelper
    {
        public const string REGISTRY_PROPERTY = RegistryProperties.REGISTRY_PROPERTY;
        public const string ID_PROPERTY = RegistryProperties.ID_PROPERTY;
        public const string NAME_PROPERTY = RegistryProperties.NAME_PROPERTY;
        public const string HI_PROPERTY = RegistryProperties.HI_PROPERTY;
        public const string LO_PROPERTY = RegistryProperties.LO_PROPERTY;

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
            => !_Property.isArray && RegistryProperties.KindOf(_Property) == TagKind.Instance;

        /// <summary>
        /// Read a <see cref="GameplayTag"/> property.
        /// </summary>
        public static GameplayTag Read(SerializedProperty _TagProperty)
            => TagAssetScanner.ToTag(RegistryProperties.Read(_TagProperty, TagKind.Instance));

        /// <summary>
        /// Write all three fields of a <see cref="GameplayTag"/> property: registry ID, tag ID and current name.
        /// Does not apply the modified properties.
        /// </summary>
        public static void Write(SerializedProperty _TagProperty, GameplayTag _Tag)
            => RegistryProperties.Write(_TagProperty, _Tag.RegistryId, _Tag.Id, _Tag.IsNone ? null : _Tag.Path);

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
                    _Label.tooltip = RegistryDrawerHelper.GetMissingTooltip(Reference(_Tag), (RegistryStatus)resolution.Status);
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
        public static string GetMissingLabel(GameplayTag _Tag) => RegistryDrawerHelper.GetMissingLabel(Reference(_Tag));

        private static RegistryReference Reference(GameplayTag _Tag) => new(TagKind.Instance, _Tag.RegistryId, _Tag.Id, _Tag.LastKnownName);
        #endregion



        #region Missing References
        /// <summary>
        /// Number of missing references (deleted, or in an unloaded registry) at <paramref name="_PropertyPath"/>
        /// across all targets. Each target is read on its own, since list contents can differ between targets.
        /// </summary>
        /// <param name="_IsContainer"> Whether the path points to a <see cref="GameplayTagContainer"/>, or to a single <see cref="GameplayTag"/>. </param>
        public static int CountMissing(IReadOnlyList<Object> _Targets, string _PropertyPath, bool _IsContainer)
            => RegistryDrawerHelper.CountMissing(_Targets, PathOf(_PropertyPath, _IsContainer), _IsContainer);

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
            => RegistryDrawerHelper.RemoveMissing(_Targets, PathOf(_PropertyPath, _IsContainer), _IsContainer,
                                                  _IsContainer ? "Remove Missing Gameplay Tags" : "Clear Missing Gameplay Tag");

        /// <summary>
        /// Whether a container holds None entries that <see cref="Compact"/> would remove.
        /// </summary>
        public static bool HasNoneEntries(SerializedProperty _ContainerProperty)
            => RegistryDrawerHelper.HasNoneEntries(_ContainerProperty.FindPropertyRelative(CONTAINER_TAGS_PROPERTY));

        /// <summary>
        /// Remove the None entries of a container, per target, in one Undo group. On a prefab asset this can move
        /// the overrides of instances and variants onto other entries: callers warn first, see
        /// <see cref="IsPrefabAssetContext"/>.
        /// </summary>
        public static void Compact(IReadOnlyList<Object> _Targets, string _PropertyPath)
            => RegistryDrawerHelper.Compact(_Targets, PathOf(_PropertyPath, true), "Compact Gameplay Tag List");

        /// <summary>
        /// Whether <paramref name="_Target"/> belongs to a prefab asset, a prefab variant asset, or the contents
        /// of the prefab open in Prefab Mode. Removing list entries there shifts the overrides of instances.
        /// </summary>
        public static bool IsPrefabAssetContext(Object _Target) => RegistryDrawerHelper.IsPrefabAssetContext(_Target);

        // A container's references are its tag list.
        private static string PathOf(string _PropertyPath, bool _IsContainer)
            => _IsContainer ? $"{_PropertyPath}.{CONTAINER_TAGS_PROPERTY}" : _PropertyPath;
        #endregion



        /// <summary>
        /// Whether <paramref name="_Property"/> can still be written, i.e. its target was not destroyed.
        /// </summary>
        public static bool IsAlive(SerializedProperty _Property) => RegistryDrawerHelper.IsAlive(_Property);
    }
}
