using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using LMK.Runtime.Registry;

using Object = UnityEngine.Object;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// One reference that Relink, Replace or import mapping would rewrite, shown in the preview.
    /// </summary>
    public sealed class RegistryReferenceChange
    {
        public string AssetPath;

        /// <summary>
        /// <see cref="GlobalObjectId"/> of the object holding the reference, to find it again when applying.
        /// </summary>
        public string ObjectId;

        public string ObjectName;
        public string PropertyPath;

        /// <summary>
        /// The reference as previewed. Its kind is the kind of both references.
        /// </summary>
        public RegistryReference From;

        public RegistryReference To;

        /// <summary>
        /// Whether the user confirmed this change. Only confirmed changes are applied.
        /// </summary>
        public bool Selected = true;

        public string Location => $"{AssetPath} > {ObjectName} > {PropertyPath}";
    }



    /// <summary>
    /// Outcome of <see cref="RegistryReferenceTools.Apply"/>.
    /// </summary>
    public sealed class RegistryReferenceApplyResult
    {
        /// <summary> References rewritten. </summary>
        public int Rewritten;

        /// <summary> Confirmed changes that weren't applied: the reference changed since the preview, or the scene couldn't be opened. </summary>
        public int Skipped;

        /// <summary> Assets that changed. </summary>
        public int Assets;

        /// <summary> Scenes that were open: changed, but not saved. </summary>
        public readonly List<string> UnsavedScenes = new();

        public string Message
        {
            get
            {
                string message = $"Rewrote {Rewritten} reference(s) in {Assets} asset(s).";
                if (Skipped > 0)
                    message += $" {Skipped} reference(s) changed since the preview and were skipped.";
                if (UnsavedScenes.Count > 0)
                    message += $" Open scene(s) changed but not saved: {string.Join(", ", UnsavedScenes)}.";

                return message;
            }
        }
    }



    /// <summary>
    /// Explicit commands that rewrite references in assets, for every kind: Relink by name, and Replace X → Y.
    /// Both find the assets through the <see cref="RegistryReferenceIndex"/>, preview every change, and rewrite only
    /// the confirmed ones. They write assets, never registries, so they need no lock.
    /// </summary>
    /// <remarks>
    /// Values a prefab instance inherits are left alone: they are rewritten in the prefab, and the instance follows.
    /// Closed scenes are opened, rewritten, saved and closed again. Open scenes are changed with Undo, and left for the
    /// user to save.
    /// </remarks>
    public static class RegistryReferenceTools
    {
        private const string PROGRESS_TITLE = "Registry References";



        #region Planning
        /// <summary>
        /// Relink by name, for one kind: references that resolve as deleted or in an unloaded registry, whose last
        /// known name is a live row, in the same registry first, then in the main registry.
        /// </summary>
        /// <returns> Whether the reference index was complete. False if the user cancelled its scan. </returns>
        public static bool PlanRelink(RegistryKind _Kind, bool _ShowProgress, out List<RegistryReferenceChange> _Changes)
        {
            bool complete = RegistryReferenceIndex.TryFindAssets(key => key.Kind == _Kind.Id && _Kind.IsMissing(key.Registry, key.Id),
                                                                 _ShowProgress, out var assets);

            _Changes = Collect(assets, _ShowProgress, reference =>
            {
                if (reference.Kind != _Kind || !_Kind.IsMissing(reference.Registry, reference.Id)) return null;

                return _Kind.TryFindRelinkTarget(reference.Registry, reference.Name, out var registry, out var id)
                    ? new RegistryReference(_Kind, registry, id, reference.Name)
                    : null;
            });

            return complete;
        }

        /// <summary>
        /// Replace references: every reference to row <paramref name="_From"/> of a kind becomes <paramref name="_To"/>.
        /// <paramref name="_From"/> itself is not deleted: the system never decides that two rows are the same.
        /// </summary>
        /// <returns> Whether the reference index was complete. False if the user cancelled its scan. </returns>
        public static bool PlanReplace(RegistryKind _Kind, (RegistryGuid Registry, RegistryGuid Id) _From, (RegistryGuid Registry, RegistryGuid Id) _To,
                                       bool _ShowProgress, out List<RegistryReferenceChange> _Changes)
        {
            _Changes = new List<RegistryReferenceChange>();
            if (_From.Id.IsZero || _To.Id.IsZero || _From == _To) return true;

            bool complete = RegistryReferenceIndex.TryFindAssets(_Kind, new[] { _From }, _ShowProgress, out var assets);
            var to = new RegistryReference(_Kind, _To.Registry, _To.Id, _Kind.GetRowName(_To.Registry, _To.Id));

            _Changes = Collect(assets, _ShowProgress, reference =>
                reference.Kind == _Kind && reference.Registry == _From.Registry && reference.Id == _From.Id ? to : null);

            return complete;
        }

        /// <summary>
        /// Walk the assets and list a change for every reference <paramref name="_Target"/> maps to another reference.
        /// Values prefab instances inherit are left to their prefab.
        /// </summary>
        /// <param name="_Target"> The new reference, or <see langword="null"/> to leave it. </param>
        public static List<RegistryReferenceChange> Collect(IReadOnlyList<string> _Assets, bool _ShowProgress, Func<RegistryReference, RegistryReference?> _Target)
        {
            var changes = new List<RegistryReferenceChange>();
            bool showBar = _ShowProgress && _Assets.Count > 1;

            try
            {
                for (int i = 0; i < _Assets.Count; i++)
                {
                    string path = _Assets[i];
                    if (showBar && EditorUtility.DisplayCancelableProgressBar(PROGRESS_TITLE, $"Reading {path}", i / (float)_Assets.Count))
                        break;

                    ForEachObject(path, false, target =>
                    {
                        using var serializedObject = new SerializedObject(target);
                        foreach (var (property, kind) in RegistryPropertyWalker.Enumerate(serializedObject, true))
                        {
                            var from = RegistryProperties.Read(property, kind);
                            if (from.Id.IsZero) continue;

                            var to = _Target(from);
                            if (to == null || to.Value.Id.IsZero) continue;

                            changes.Add(new RegistryReferenceChange
                            {
                                AssetPath = path,
                                ObjectId = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString(),
                                ObjectName = RegistryPropertyWalker.Describe(target),
                                PropertyPath = property.propertyPath,
                                From = from,
                                To = to.Value,
                            });
                        }

                        return false;
                    });
                }
            }
            finally
            {
                if (showBar)
                    EditorUtility.ClearProgressBar();
            }

            return changes;
        }
        #endregion



        #region Applying
        /// <summary>
        /// Rewrite the selected changes, in one Undo group. A reference that no longer holds the previewed row is
        /// skipped. Assets are saved, and so are scenes that had to be opened.
        /// </summary>
        public static RegistryReferenceApplyResult Apply(IEnumerable<RegistryReferenceChange> _Changes, string _UndoName, bool _ShowProgress = false)
        {
            var result = new RegistryReferenceApplyResult();
            var selected = _Changes.Where(e => e.Selected).ToList();
            if (selected.Count == 0) return result;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(_UndoName);

            bool saveAssets = false;
            var assets = selected.GroupBy(e => e.AssetPath).ToList();
            bool showBar = _ShowProgress && assets.Count > 1;

            try
            {
                for (int i = 0; i < assets.Count; i++)
                {
                    var asset = assets[i];
                    if (showBar)
                        EditorUtility.DisplayProgressBar(PROGRESS_TITLE, $"Rewriting {asset.Key}", i / (float)assets.Count);

                    saveAssets |= ApplyToAsset(asset, result);
                }

                if (saveAssets)
                {
                    if (showBar)
                        EditorUtility.DisplayProgressBar(PROGRESS_TITLE, "Saving assets", 1f);

                    AssetDatabase.SaveAssets();
                }
            }
            finally
            {
                if (showBar)
                    EditorUtility.ClearProgressBar();
            }

            result.Skipped = selected.Count - result.Rewritten;

            Undo.CollapseUndoOperations(group);
            return result;
        }

        /// <returns> Whether a non-scene asset changed and must be saved. </returns>
        private static bool ApplyToAsset(IGrouping<string, RegistryReferenceChange> _Asset, RegistryReferenceApplyResult _Result)
        {
            string path = _Asset.Key;
            var byObject = _Asset.GroupBy(e => e.ObjectId).ToDictionary(e => e.Key, e => e.ToList());
            int rewritten = 0;

            // Scenes opened only for this are closed again, so their changes can't be undone.
            bool withUndo = !IsScene(path) || SceneManager.GetSceneByPath(path).isLoaded;

            bool done = ForEachObject(path, true, target =>
            {
                string id = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString();
                if (!byObject.TryGetValue(id, out var changes)) return false;

                int count = Rewrite(target, changes, withUndo);
                rewritten += count;
                return count > 0;
            }, _Result.UnsavedScenes);

            if (!done || rewritten == 0) return false;

            _Result.Rewritten += rewritten;
            _Result.Assets++;
            return !IsScene(path);
        }

        private static int Rewrite(Object _Target, List<RegistryReferenceChange> _Changes, bool _WithUndo)
        {
            using var serializedObject = new SerializedObject(_Target);
            int count = 0;

            foreach (var change in _Changes)
            {
                var property = serializedObject.FindProperty(change.PropertyPath);
                if (property == null || RegistryProperties.KindOf(property) != change.From.Kind) continue;

                // Changed since the preview: leave it.
                if (!RegistryProperties.Read(property, change.From.Kind).Equals(change.From)) continue;

                RegistryProperties.Write(property, change.To.Registry, change.To.Id, change.To.Name);
                count++;
            }

            if (count > 0)
            {
                if (_WithUndo) serializedObject.ApplyModifiedProperties();
                else serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            return count;
        }
        #endregion



        #region Assets And Scenes
        private static bool IsScene(string _Path) => _Path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Call <paramref name="_Action"/> for each object of an asset that can hold references. A closed scene is
        /// opened for the call and closed again: saved first if <paramref name="_Save"/> and an action reported a change.
        /// An open scene with changes is marked dirty and added to <paramref name="_UnsavedScenes"/>.
        /// </summary>
        /// <param name="_Action"> Returns whether it changed something. </param>
        /// <returns> Whether the asset could be visited. Closed scenes can't be opened in Play Mode. </returns>
        private static bool ForEachObject(string _Path, bool _Save, Func<Object, bool> _Action, List<string> _UnsavedScenes = null)
        {
            if (!IsScene(_Path))
            {
                foreach (var target in RegistryPropertyWalker.GetAssetObjects(_Path).ToList())
                    _Action(target);

                return true;
            }

            var scene = SceneManager.GetSceneByPath(_Path);
            bool wasLoaded = scene.isLoaded;

            if (!wasLoaded && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning($"Registry references: \"{_Path}\" can't be opened in Play Mode, so it was skipped.");
                return false;
            }

            bool inSetup = scene.IsValid();
            if (!wasLoaded)
                scene = EditorSceneManager.OpenScene(_Path, OpenSceneMode.Additive);

            try
            {
                bool changed = false;
                foreach (var component in RegistryPropertyWalker.GetSceneObjects(scene).ToList())
                    changed |= _Action(component);

                if (changed && _Save)
                {
                    if (wasLoaded)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        _UnsavedScenes?.Add(_Path);
                    }
                    else
                    {
                        EditorSceneManager.SaveScene(scene);
                    }
                }
            }
            finally
            {
                if (!wasLoaded)
                    EditorSceneManager.CloseScene(scene, !inSetup);
            }

            return true;
        }
        #endregion
    }
}
