using System;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Which effects to find, remove or block (UE's <c>FGameplayEffectQuery</c>): by the effect's own tags, the tags it
    /// grants, its source's tags, its definition, its source unit, or a custom test. Every part that is set must match;
    /// a query with no part set matches nothing.
    /// </summary>
    [Serializable]
    public sealed class GameplayEffectQuery
    {
        [Tooltip("Matched against the effect's own tags (asset tags).")]
        public GameplayTagQuery assetTagQuery = new();

        [Tooltip("Matched against the tags the effect grants its target.")]
        public GameplayTagQuery grantedTagQuery = new();

        [Tooltip("Matched against the source's tags, captured when the spec was made.")]
        public GameplayTagQuery sourceTagQuery = new();

        [Tooltip("Only this effect.")]
        public GameplayEffect effectDefinition;

        /// <summary>
        /// Only effects from this unit. Set from code.
        /// </summary>
        [NonSerialized]
        public GameplayAbilitySystem source;

        /// <summary>
        /// A custom test. Set from code.
        /// </summary>
        [NonSerialized]
        public Func<GameplayEffectSpec, bool> customMatch;

        public bool IsEmpty => IsUnset(assetTagQuery) && IsUnset(grantedTagQuery) && IsUnset(sourceTagQuery)
                            && effectDefinition == null && source == null && customMatch == null;

        public static GameplayEffectQuery MatchingAssetTags(GameplayTagQuery _Query) => new() { assetTagQuery = _Query };

        public static GameplayEffectQuery MatchingGrantedTags(GameplayTagQuery _Query) => new() { grantedTagQuery = _Query };

        public static GameplayEffectQuery MatchingAnyAssetTag(params GameplayTag[] _Tags)
            => MatchingAssetTags(GameplayTagQuery.Build(GameplayTagQueryExpression.AnyTags(_Tags)));

        public static GameplayEffectQuery MatchingAnyGrantedTag(params GameplayTag[] _Tags)
            => MatchingGrantedTags(GameplayTagQuery.Build(GameplayTagQueryExpression.AnyTags(_Tags)));

        /// <summary>
        /// Whether an application matches every part that is set.
        /// </summary>
        public bool Matches(GameplayEffectSpec _Spec)
        {
            if (_Spec == null || IsEmpty) return false;

            if (!IsUnset(assetTagQuery) && !assetTagQuery.Matches(_Spec.GetAllAssetTags())) return false;
            if (!IsUnset(grantedTagQuery) && !grantedTagQuery.Matches(_Spec.GetAllGrantedTags())) return false;
            if (!IsUnset(sourceTagQuery) && !sourceTagQuery.Matches(_Spec.CapturedSourceTags)) return false;
            if (effectDefinition != null && _Spec.Def != effectDefinition) return false;
            if (source != null && _Spec.Context.InstigatorEvenIfShutDown != source) return false;
            if (customMatch != null && !customMatch(_Spec)) return false;

            return true;
        }

        private static bool IsUnset(GameplayTagQuery _Query) => _Query == null || _Query.IsEmpty;
    }
}
