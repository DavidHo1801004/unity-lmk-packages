using System.Collections.Generic;

namespace LMK.Runtime.GameplayTags
{
    /// <summary>
    /// Resolved handles of a set of tags: the tags themselves, and the tags plus all of their ancestors. <br/>
    /// Built for one database generation. Unresolved and None tags are left out, so they never match.
    /// </summary>
    internal sealed class TagHandleSet
    {
        private readonly HashSet<int> exact = new();
        private readonly HashSet<int> withAncestors = new();



        public static TagHandleSet Build(GameplayTagDatabase _Database, List<GameplayTag> _Tags)
        {
            var result = new TagHandleSet();
            if (_Database == null || _Tags == null) return result;

            foreach (var tag in _Tags)
            {
                if (tag.IsNone) continue;

                var resolution = _Database.Resolve(tag.RegistryId, tag.Id);
                if (!resolution.IsLive)
                {
                    GameplayTagDatabase.ReportUnresolved(tag, resolution.Status);
                    continue;
                }

                if (!result.exact.Add(resolution.Handle.Index)) continue;

                // Parents of a tag that is already in the set are already added.
                for (var handle = resolution.Handle; handle.IsValid; handle = _Database.GetParent(handle))
                    if (!result.withAncestors.Add(handle.Index))
                        break;
            }

            return result;
        }

        /// <summary>
        /// Whether the set holds exactly this tag.
        /// </summary>
        public bool ContainsExact(TagHandle _Handle) => _Handle.IsValid && exact.Contains(_Handle.Index);

        /// <summary>
        /// Whether the set holds this tag or one of its descendants.
        /// </summary>
        public bool ContainsSelfOrDescendant(TagHandle _Handle) => _Handle.IsValid && withAncestors.Contains(_Handle.Index);
    }
}
