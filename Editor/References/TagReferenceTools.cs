using System;
using System.Collections.Generic;
using System.Linq;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// One tag reference that Relink or Replace would rewrite, shown in the preview: a
    /// <see cref="RegistryReferenceChange"/> of the tag kind, with its tags.
    /// </summary>
    internal sealed class TagReferenceChange
    {
        /// <summary>
        /// The change as the registry core applies it.
        /// </summary>
        public RegistryReferenceChange Core { get; }

        public string AssetPath => Core.AssetPath;

        /// <summary>
        /// <see cref="UnityEditor.GlobalObjectId"/> of the object holding the reference, to find it again when applying.
        /// </summary>
        public string ObjectId => Core.ObjectId;

        public string ObjectName => Core.ObjectName;
        public string PropertyPath => Core.PropertyPath;
        public GameplayTag From => TagAssetScanner.ToTag(Core.From);
        public GameplayTag To
        {
            get => TagAssetScanner.ToTag(Core.To);
            set => Core.To = new RegistryReference(TagKind.Instance, value.RegistryId, value.Id, value.LastKnownName);
        }

        /// <summary>
        /// Whether the user confirmed this change. Only confirmed changes are applied.
        /// </summary>
        public bool Selected
        {
            get => Core.Selected;
            set => Core.Selected = value;
        }

        public TagReferenceChange(RegistryReferenceChange _Core)
        {
            Core = _Core ?? throw new ArgumentNullException(nameof(_Core));
        }
    }



    /// <summary>
    /// Explicit commands that rewrite tag references in assets: Relink by name, and Replace X → Y, through the
    /// registry core's <see cref="RegistryReferenceTools"/>. Both find the assets through the reference index,
    /// preview every change, and rewrite only the confirmed ones. They write assets, never registries, so they need
    /// no lock.
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

        /// <summary>
        /// Relink by name: references that resolve as deleted or in an unloaded registry, whose last known
        /// name is a live tag, in the same registry first, then in the main registry.
        /// </summary>
        /// <returns> Whether the reference index was complete. False if the user cancelled its scan. </returns>
        public static bool PlanRelink(bool _ShowProgress, out List<TagReferenceChange> _Changes)
        {
            bool complete = RegistryReferenceTools.PlanRelink(TagKind.Instance, _ShowProgress, out var changes);
            _Changes = Wrap(changes);
            return complete;
        }

        /// <summary>
        /// Replace references: every reference to <paramref name="_From"/> becomes <paramref name="_To"/>.
        /// <paramref name="_From"/> itself is not deleted: the system never decides that two tags are the same.
        /// </summary>
        /// <returns> Whether the reference index was complete. False if the user cancelled its scan. </returns>
        public static bool PlanReplace(GameplayTag _From, GameplayTag _To, bool _ShowProgress, out List<TagReferenceChange> _Changes)
        {
            bool complete = RegistryReferenceTools.PlanReplace(TagKind.Instance, (_From.RegistryId, _From.Id), (_To.RegistryId, _To.Id),
                                                               _ShowProgress, out var changes);
            _Changes = Wrap(changes);
            return complete;
        }

        /// <summary>
        /// Walk the assets and list a change for every tag reference <paramref name="_Target"/> maps to a tag, other
        /// than None. Values prefab instances inherit are left to their prefab.
        /// </summary>
        internal static List<TagReferenceChange> Collect(IReadOnlyList<string> _Assets, bool _ShowProgress, Func<GameplayTag, GameplayTag> _Target)
        {
            var changes = RegistryReferenceTools.Collect(_Assets, _ShowProgress, reference =>
            {
                if (reference.Kind != TagKind.Instance) return null;

                var to = _Target(TagAssetScanner.ToTag(reference));
                return to.IsNone ? null : new RegistryReference(TagKind.Instance, to.RegistryId, to.Id, to.LastKnownName);
            });

            return Wrap(changes);
        }

        /// <summary>
        /// Rewrite the selected changes, in one Undo group. A reference that no longer holds the previewed tag is
        /// skipped. Assets are saved, and so are scenes that had to be opened.
        /// </summary>
        public static RegistryReferenceApplyResult Apply(IEnumerable<TagReferenceChange> _Changes, string _UndoName, bool _ShowProgress = false)
            => RegistryReferenceTools.Apply(_Changes.Select(e => e.Core), _UndoName, _ShowProgress);

        private static List<TagReferenceChange> Wrap(List<RegistryReferenceChange> _Changes)
            => _Changes.Select(e => new TagReferenceChange(e)).ToList();
    }
}
