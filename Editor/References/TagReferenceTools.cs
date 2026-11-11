using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using LMK.Runtime.GameplayTags;

using Object = UnityEngine.Object;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// One reference that Relink or Replace would rewrite, shown in the preview.
    /// </summary>
    internal sealed class TagReferenceChange
    {
        public string AssetPath;

        /// <summary>
        /// <see cref="GlobalObjectId"/> of the object holding the reference, to find it again when applying.
        /// </summary>
        public string ObjectId;

        public string ObjectName;
        public string PropertyPath;
        public GameplayTag From;
        public GameplayTag To;

        /// <summary>
        /// Whether the user confirmed this change. Only confirmed changes are applied.
        /// </summary>
        public bool Selected = true;
    }



    /// <summary>
    /// Outcome of <see cref="TagReferenceTools.Apply"/>.
    /// </summary>
    internal sealed class TagReferenceApplyResult
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
    /// Explicit commands that rewrite tag references in assets: Relink by name, and Replace X → Y.
    /// Both find the assets through the <see cref="TagReferenceIndex"/>, preview every change, and rewrite only
    /// the confirmed ones. They write assets, never registries, so they need no lock.
    /// </summary>
    /// <remarks>
    /// Values a prefab instance inherits are left alone: they are rewritten in the prefab, and the instance follows.
    /// Closed scenes are opened, rewritten, saved and closed again. Open scenes are changed with Undo, and left
    /// for the user to save.
    /// </remarks>
    internal static class TagReferenceTools
    {
        public const string RELINK_UNDO = "Relink Gameplay Tag References";
        public const string REPLACE_UNDO = "Replace Gameplay Tag References";

        private const string PROGRESS_TITLE = "Gameplay Tag References";



        #region Planning
        /// <summary>
        /// Relink by name: references that resolve as deleted or in an unloaded registry, whose last known
        /// name is a live tag, in the same registry first, then in the main registry.
        /// </summary>
        /// <returns> Whether the reference index was complete. False if the user cancelled its scan. </returns>
        public static bool PlanRelink(bool _ShowProgress, out List<TagReferenceChange> _Changes)
        {
            var snapshot = TagRegistryService.Snapshot;
            bool complete = TagReferenceIndex.TryFindAssets(key => key.IsMissingIn(snapshot), _ShowProgress, out var assets);

            _Changes = Collect(assets, _ShowProgress, tag =>
            {
                if (tag.IsNone || !tag.Resolve().IsMissing) return GameplayTag.None;
                return FindRelinkTarget(snapshot, tag);
            });

            return complete;
        }

        /// <summary>
        /// The live tag a missing reference would be relinked to, or <see cref="GameplayTag.None"/>.
        /// </summary>
        internal static GameplayTag FindRelinkTarget(TagRegistrySnapshot _Snapshot, GameplayTag _Tag)
        {
            string name = _Tag.LastKnownName;
            if (string.IsNullOrEmpty(name)) return GameplayTag.None;

            var sameRegistry = _Snapshot.Find(_Tag.RegistryId);
            if (sameRegistry != null && sameRegistry.TryFind(name, out var id))
                return GameplayTag.FromId(sameRegistry.Id, id, name);

            var main = _Snapshot.Main;
            if (main != null && main != sameRegistry && main.TryFind(name, out id))
                return GameplayTag.FromId(main.Id, id, name);

            return GameplayTag.None;
        }

        /// <summary>
        /// Replace references: every reference to <paramref name="_From"/> becomes <paramref name="_To"/>.
        /// <paramref name="_From"/> itself is not deleted: the system never decides that two tags are the same.
        /// </summary>
        /// <returns> Whether the reference index was complete. False if the user cancelled its scan. </returns>
        public static bool PlanReplace(GameplayTag _From, GameplayTag _To, bool _ShowProgress, out List<TagReferenceChange> _Changes)
        {
            _Changes = new List<TagReferenceChange>();
            if (_From.IsNone || _To.IsNone || _From.Equals(_To)) return true;

            bool complete = TagReferenceIndex.TryFindAssets(new[] { _From }, _ShowProgress, out var assets);
            var to = GameplayTag.FromId(_To.RegistryId, _To.Id, _To.Path);

            _Changes = Collect(assets, _ShowProgress, tag => tag.Equals(_From) ? to : GameplayTag.None);
            return complete;
        }

        /// <summary>
        /// Walk the assets and list a change for every reference <paramref name="_Target"/> maps to a tag, other than
        /// None. Values prefab instances inherit are left to their prefab.
        /// </summary>
        internal static List<TagReferenceChange> Collect(IReadOnlyList<string> _Assets, bool _ShowProgress, Func<GameplayTag, GameplayTag> _Target)
        {
            var changes = new List<TagReferenceChange>();
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
                        foreach (var property in TagPropertyWalker.Enumerate(serializedObject, true))
                        {
                            var from = GameplayTagDrawerHelper.Read(property);
                            var to = _Target(from);
                            if (to.IsNone) continue;

                            changes.Add(new TagReferenceChange
                            {
                                AssetPath = path,
                                ObjectId = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString(),
                                ObjectName = TagPropertyWalker.Describe(target),
                                PropertyPath = property.propertyPath,
                                From = from,
                                To = to,
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
        /// Rewrite the selected changes, in one Undo group. A reference that no longer holds the previewed tag is
        /// skipped. Assets are saved, and so are scenes that had to be opened.
        /// </summary>
        public static TagReferenceApplyResult Apply(IEnumerable<TagReferenceChange> _Changes, string _UndoName, bool _ShowProgress = false)
        {
            var result = new TagReferenceApplyResult();
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
        private static bool ApplyToAsset(IGrouping<string, TagReferenceChange> _Asset, TagReferenceApplyResult _Result)
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

        private static int Rewrite(Object _Target, List<TagReferenceChange> _Changes, bool _WithUndo)
        {
            using var serializedObject = new SerializedObject(_Target);
            int count = 0;

            foreach (var change in _Changes)
            {
                var property = serializedObject.FindProperty(change.PropertyPath);
                if (property == null || !GameplayTagDrawerHelper.IsTagProperty(property)) continue;

                // Changed since the preview: leave it.
                if (!GameplayTagDrawerHelper.Read(property).Equals(change.From)) continue;

                GameplayTagDrawerHelper.Write(property, change.To);
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
        /// Call <paramref name="_Action"/> for each object of an asset that can hold tags. A closed scene is opened
        /// for the call and closed again: saved first if <paramref name="_Save"/> and an action reported a change.
        /// An open scene with changes is marked dirty and added to <paramref name="_UnsavedScenes"/>.
        /// </summary>
        /// <param name="_Action"> Returns whether it changed something. </param>
        /// <returns> Whether the asset could be visited. Closed scenes can't be opened in Play Mode. </returns>
        private static bool ForEachObject(string _Path, bool _Save, Func<Object, bool> _Action, List<string> _UnsavedScenes = null)
        {
            if (!IsScene(_Path))
            {
                foreach (var target in TagPropertyWalker.GetAssetObjects(_Path).ToList())
                    _Action(target);

                return true;
            }

            var scene = SceneManager.GetSceneByPath(_Path);
            bool wasLoaded = scene.isLoaded;

            if (!wasLoaded && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning($"Gameplay tag references: \"{_Path}\" can't be opened in Play Mode, so it was skipped.");
                return false;
            }

            bool inSetup = scene.IsValid();
            if (!wasLoaded)
                scene = EditorSceneManager.OpenScene(_Path, OpenSceneMode.Additive);

            try
            {
                bool changed = false;
                foreach (var component in TagPropertyWalker.GetSceneObjects(scene).ToList())
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
