using System;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// A tag test, as UE's <c>FGameplayTagRequirements</c>: every required tag present, no ignored tag present, and
    /// the optional query matching. Empty requirements always pass.
    /// </summary>
    [Serializable]
    public sealed class TagRequirements
    {
        [Tooltip("Every one of these tags must be present (hierarchical).")]
        public GameplayTagContainer requireTags = new();

        [Tooltip("None of these tags may be present (hierarchical).")]
        public GameplayTagContainer ignoreTags = new();

        [Tooltip("Optional: must match too. Leave empty for no extra test.")]
        public GameplayTagQuery query = new();

        public bool IsEmpty => (requireTags == null || requireTags.IsEmpty) && (ignoreTags == null || ignoreTags.IsEmpty) && (query == null || query.IsEmpty);

        public TagRequirements() { }

        public TagRequirements(GameplayTagContainer _Require, GameplayTagContainer _Ignore = null, GameplayTagQuery _Query = null)
        {
            requireTags = _Require ?? new GameplayTagContainer();
            ignoreTags = _Ignore ?? new GameplayTagContainer();
            query = _Query ?? new GameplayTagQuery();
        }

        /// <summary>
        /// Whether <paramref name="_Tags"/> passes. Empty requirements always pass.
        /// </summary>
        public bool RequirementsMet(IGameplayTagSet _Tags)
        {
            if (requireTags != null)
                foreach (var tag in requireTags)
                    if (_Tags == null || !_Tags.HasTag(tag))
                        return false;

            if (ignoreTags != null && _Tags != null)
                foreach (var tag in ignoreTags)
                    if (_Tags.HasTag(tag))
                        return false;

            return query == null || query.IsEmpty || query.Matches(_Tags);
        }
    }
}
