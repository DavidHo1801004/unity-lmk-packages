using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace LMK.Runtime.GameplayTags
{
    /// <summary>
    /// A set of unique gameplay tags to be used during gameplay. <br/>
    /// Parent and child tags can be stored together, e.g. "A" and "A.B".
    /// </summary>
    /// <remarks>
    /// Queries come in two forms: <br/>
    /// - Hierarchical (<see cref="HasTag"/>, <see cref="HasAny"/>, <see cref="HasAll"/>):
    ///   a container holding "A.B" has "A". <br/>
    /// - Exact (<see cref="HasTagExact"/>, <see cref="HasAnyExact"/>, <see cref="HasAllExact"/>):
    ///   only the stored tags count. <br/>
    /// Queries compare resolved tags: entries that don't resolve are ignored. <br/>
    /// The serialized list may hold <see cref="GameplayTag.None"/> entries (written by the editor's prefab-safe
    /// removal). They are not counted, not enumerated, and match nothing.
    /// </remarks>
    [Serializable]
    public sealed class GameplayTagContainer : IReadOnlyCollection<GameplayTag>, ISerializationCallbackReceiver
    {
        /// <summary>
        /// Enumerates the stored tags in insertion order, skipping <see cref="GameplayTag.None"/> entries.
        /// </summary>
        public struct Enumerator : IEnumerator<GameplayTag>
        {
            private readonly List<GameplayTag> tags;
            private int index;

            internal Enumerator(List<GameplayTag> _Tags)
            {
                tags = _Tags;
                index = -1;
            }

            public GameplayTag Current => tags[index];

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                while (++index < tags.Count)
                    if (!tags[index].IsNone)
                        return true;

                return false;
            }

            public void Reset() => index = -1;

            public void Dispose() { }
        }



        [SerializeField]
        private List<GameplayTag> _tags = new();

        // Resolved handles of the stored tags, for one database generation.
        [NonSerialized]
        private TagHandleSet handleCache;

        [NonSerialized]
        private int handleCacheGeneration;

        /// <summary>
        /// Number of stored tags, not counting <see cref="GameplayTag.None"/> entries.
        /// </summary>
        public int Count
        {
            get
            {
                int count = 0;
                foreach (var tag in _tags)
                    if (!tag.IsNone)
                        count++;

                return count;
            }
        }

        public bool IsEmpty => Count == 0;



        public GameplayTagContainer() { }

        public GameplayTagContainer(params GameplayTag[] _Tags)
        {
            if (_Tags == null) return;

            foreach (var tag in _Tags)
                AddTag(tag);
        }

        public GameplayTagContainer(GameplayTagContainer _Other)
        {
            AddTags(_Other);
        }



        #region Modification
        /// <summary>
        /// Add <paramref name="_Tag"/> if it is assigned and not stored yet (data equality).
        /// </summary>
        /// <returns>
        /// <see langword="true"/> if the container was modified.
        /// </returns>
        public bool AddTag(GameplayTag _Tag)
        {
            if (_Tag.IsNone || _tags.Contains(_Tag)) return false;

            _tags.Add(_Tag);
            InvalidateCache();

            return true;
        }

        /// <summary>
        /// Add all tags of <paramref name="_Other"/>.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> if the container was modified.
        /// </returns>
        public bool AddTags(GameplayTagContainer _Other)
        {
            if (_Other == null) return false;

            bool result = false;
            foreach (var tag in _Other)
                result |= AddTag(tag);

            return result;
        }

        /// <summary>
        /// Remove <paramref name="_Tag"/> from the container.
        /// </summary>
        /// <param name="_IncludeChildren"> If true, also remove every stored descendant of <paramref name="_Tag"/>. </param>
        /// <returns>
        /// Whether any tag was removed.
        /// </returns>
        public bool RemoveTag(GameplayTag _Tag, bool _IncludeChildren = false)
        {
            if (_Tag.IsNone) return false;

            int removed = _IncludeChildren
                ? _tags.RemoveAll(e => !e.IsNone && (e.Equals(_Tag) || e.MatchesTag(_Tag)))
                : (_tags.Remove(_Tag) ? 1 : 0);

            if (removed > 0)
                InvalidateCache();

            return removed > 0;
        }

        /// <summary>
        /// Remove all tags of <paramref name="_Other"/> from this container (data equality).
        /// </summary>
        /// <returns>
        /// Whether any tag was removed.
        /// </returns>
        public bool RemoveTags(GameplayTagContainer _Other)
        {
            if (_Other == null) return false;

            bool result = false;
            foreach (var tag in _Other)
                result |= RemoveTag(tag);

            return result;
        }

        /// <summary>
        /// Remove all tags.
        /// </summary>
        public void Clear()
        {
            _tags.Clear();
            InvalidateCache();
        }
        #endregion



        #region Queries
        /// <summary>
        /// Whether the container has <paramref name="_Tag"/>, or a descendant of it. <br/>
        /// E.g. {"A.B"} has "A.B" and "A", but not "A.B.C".
        /// </summary>
        public bool HasTag(GameplayTag _Tag)
            => GetHandles().ContainsSelfOrDescendant(_Tag.Handle);

        /// <summary>
        /// Whether the container stores exactly <paramref name="_Tag"/>.
        /// </summary>
        public bool HasTagExact(GameplayTag _Tag)
            => GetHandles().ContainsExact(_Tag.Handle);

        /// <summary>
        /// Whether any tag of <paramref name="_Other"/> passes <see cref="HasTag"/>. <br/>
        /// <see langword="false"/> if <paramref name="_Other"/> is empty.
        /// </summary>
        public bool HasAny(GameplayTagContainer _Other)
        {
            if (_Other == null) return false;

            var handles = GetHandles();
            foreach (var tag in _Other)
                if (handles.ContainsSelfOrDescendant(tag.Handle)) return true;

            return false;
        }

        /// <summary>
        /// Whether any tag of <paramref name="_Other"/> passes <see cref="HasTagExact"/>. <br/>
        /// <see langword="false"/> if <paramref name="_Other"/> is empty.
        /// </summary>
        public bool HasAnyExact(GameplayTagContainer _Other)
        {
            if (_Other == null) return false;

            var handles = GetHandles();
            foreach (var tag in _Other)
                if (handles.ContainsExact(tag.Handle)) return true;

            return false;
        }

        /// <summary>
        /// Whether every tag of <paramref name="_Other"/> passes <see cref="HasTag"/>. <br/>
        /// <see langword="true"/> if <paramref name="_Other"/> is empty.
        /// </summary>
        public bool HasAll(GameplayTagContainer _Other)
        {
            if (_Other == null) return true;

            var handles = GetHandles();
            foreach (var tag in _Other)
                if (!handles.ContainsSelfOrDescendant(tag.Handle)) return false;

            return true;
        }

        /// <summary>
        /// Whether every tag of <paramref name="_Other"/> passes <see cref="HasTagExact"/>. <br/>
        /// <see langword="true"/> if <paramref name="_Other"/> is empty.
        /// </summary>
        public bool HasAllExact(GameplayTagContainer _Other)
        {
            if (_Other == null) return true;

            var handles = GetHandles();
            foreach (var tag in _Other)
                if (!handles.ContainsExact(tag.Handle)) return false;

            return true;
        }

        /// <summary>
        /// Add every stored tag matching any tag of <paramref name="_Filter"/> (see <see cref="GameplayTag.MatchesTag"/>)
        /// to <paramref name="_Result"/>.
        /// </summary>
        public void GetMatchingTags(GameplayTagContainer _Filter, GameplayTagContainer _Result)
        {
            if (_Filter == null || _Result == null) return;

            foreach (var tag in this)
                foreach (var filterTag in _Filter)
                    if (tag.MatchesTag(filterTag))
                    {
                        _Result.AddTag(tag);
                        break;
                    }
        }
        #endregion



        public Enumerator GetEnumerator() => new(_tags);

        IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString()
        {
            var names = new List<string>(_tags.Count);
            foreach (var tag in this)
                names.Add(tag.ToString());

            return string.Join(", ", names);
        }



        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        // Serialized data can change without the API (Inspector, Undo), so the cache is rebuilt on next query.
        // Only the cache is touched here: this may run on a loading thread.
        void ISerializationCallbackReceiver.OnAfterDeserialize() => InvalidateCache();

        private void InvalidateCache()
        {
            handleCache = null;
            handleCacheGeneration = 0;
        }

        private TagHandleSet GetHandles()
        {
            var database = GameplayTagDatabase.Active;
            int generation = GameplayTagDatabase.Generation;

            if (handleCache != null && generation != 0 && handleCacheGeneration == generation)
                return handleCache;

            var handles = TagHandleSet.Build(database, _tags);

            // Generation 0 means no database is ready yet: never cache under it.
            if (generation != 0)
            {
                handleCache = handles;
                handleCacheGeneration = generation;
            }

            return handles;
        }
    }
}
