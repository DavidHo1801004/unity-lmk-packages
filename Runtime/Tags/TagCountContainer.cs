using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// When a tag event fires.
    /// </summary>
    public enum TagEventType
    {
        /// <summary> Only when the count goes from 0 to 1 or from 1 to 0. </summary>
        NewOrRemoved,

        /// <summary> On every change of the count. </summary>
        AnyCountChange,
    }



    /// <summary>
    /// A unit's tags: loose tags, tags granted by active effects and tags owned by active abilities, in
    /// one reference-counted container. A tag counts for itself and every ancestor, within its own registry.
    /// </summary>
    /// <remarks>
    /// Counts are kept by tag identity (registry and tag ID), so they survive database changes. A tag that doesn't
    /// resolve is never counted, and a counted tag that stops resolving matches nothing until it resolves again. <br/>
    /// Events are dispatched after the whole update, so a listener always sees a consistent container.
    /// </remarks>
    public sealed class TagCountContainer : IGameplayTagSet
    {
        private sealed class Listener
        {
            public GameplayTag Tag;
            public TagEventType Type;
            public Action<GameplayTag, int> Callback;
            public bool Removed;
        }

        private readonly Dictionary<GameplayTag, int> explicitCounts = new();
        private readonly Dictionary<GameplayTag, int> totalCounts = new();
        private readonly List<Listener> listeners = new();
        private readonly List<(GameplayTag Tag, int Old, int New)> pending = new();
        private int updateDepth;

        /// <summary>
        /// Called after every update that changed a count, after the tag events: the unit re-checks what depends on
        /// its tags.
        /// </summary>
        internal event Action Changed;



        #region Queries
        /// <summary>
        /// Whether the tag, or a descendant of it, is present.
        /// </summary>
        public bool HasTag(GameplayTag _Tag) => _Tag.IsLive && totalCounts.TryGetValue(_Tag, out int count) && count > 0;

        /// <summary>
        /// Whether exactly this tag is present.
        /// </summary>
        public bool HasTagExact(GameplayTag _Tag) => _Tag.IsLive && explicitCounts.TryGetValue(_Tag, out int count) && count > 0;

        public bool HasAll(GameplayTagContainer _Tags)
        {
            if (_Tags == null) return true;

            foreach (var tag in _Tags)
                if (!HasTag(tag))
                    return false;

            return true;
        }

        public bool HasAny(GameplayTagContainer _Tags)
        {
            if (_Tags == null) return false;

            foreach (var tag in _Tags)
                if (HasTag(tag))
                    return true;

            return false;
        }

        /// <summary>
        /// How many times the tag or its descendants were added, net of removals.
        /// </summary>
        public int GetTagCount(GameplayTag _Tag) => _Tag.IsLive && totalCounts.TryGetValue(_Tag, out int count) ? count : 0;

        /// <summary>
        /// How many times exactly this tag was added, net of removals.
        /// </summary>
        public int GetExplicitTagCount(GameplayTag _Tag) => _Tag.IsLive && explicitCounts.TryGetValue(_Tag, out int count) ? count : 0;

        /// <summary>
        /// The tags added explicitly and present now, as a new container.
        /// </summary>
        public GameplayTagContainer Snapshot()
        {
            var result = new GameplayTagContainer();
            foreach (var (tag, count) in explicitCounts)
                if (count > 0 && tag.IsLive)
                    result.AddTag(tag);

            return result;
        }
        #endregion



        #region Updates
        /// <summary>
        /// Add <paramref name="_Count"/> to a tag and its ancestors. Unresolved tags are not counted.
        /// </summary>
        internal void Add(GameplayTag _Tag, int _Count = 1)
        {
            if (_Count <= 0) return;

            BeginUpdate();
            try
            {
                Change(_Tag, _Count);
            }
            finally
            {
                EndUpdate();
            }
        }

        /// <summary>
        /// Remove <paramref name="_Count"/> from a tag and its ancestors. A count never goes below zero; asking for it
        /// logs an error.
        /// </summary>
        internal void Remove(GameplayTag _Tag, int _Count = 1)
        {
            if (_Count <= 0) return;

            BeginUpdate();
            try
            {
                Change(_Tag, -_Count);
            }
            finally
            {
                EndUpdate();
            }
        }

        internal void AddTags(GameplayTagContainer _Tags)
        {
            if (_Tags == null || _Tags.IsEmpty) return;

            BeginUpdate();
            try
            {
                foreach (var tag in _Tags)
                    Change(tag, 1);
            }
            finally
            {
                EndUpdate();
            }
        }

        internal void RemoveTags(GameplayTagContainer _Tags)
        {
            if (_Tags == null || _Tags.IsEmpty) return;

            BeginUpdate();
            try
            {
                foreach (var tag in _Tags)
                    Change(tag, -1);
            }
            finally
            {
                EndUpdate();
            }
        }

        /// <summary>
        /// Group several updates: events are dispatched once the outermost group ends.
        /// </summary>
        internal void BeginUpdate() => updateDepth++;

        internal void EndUpdate()
        {
            if (--updateDepth > 0) return;

            Dispatch();
        }

        private void Change(GameplayTag _Tag, int _Delta)
        {
            var resolution = _Tag.Resolve();
            if (!resolution.IsLive) return;

            int oldExplicit = explicitCounts.TryGetValue(_Tag, out int count) ? count : 0;
            int newExplicit = oldExplicit + _Delta;
            if (newExplicit < 0)
            {
                Debug.LogError($"Gameplay tag \"{_Tag}\" was removed {-_Delta} time(s) but only added {oldExplicit} time(s): a tag count below zero is refused, so it stays at 0.");
                _Delta = -oldExplicit;
                newExplicit = 0;
                if (_Delta == 0) return;
            }

            if (newExplicit == 0) explicitCounts.Remove(_Tag);
            else explicitCounts[_Tag] = newExplicit;

            // The tag and every ancestor, within its registry.
            var database = GameplayTagDatabase.Active;
            for (var handle = resolution.Handle; handle.IsValid; handle = database.GetParent(handle))
            {
                var tag = database.GetTag(handle);
                int old = totalCounts.TryGetValue(tag, out int total) ? total : 0;
                int updated = Math.Max(0, old + _Delta);

                if (updated == 0) totalCounts.Remove(tag);
                else totalCounts[tag] = updated;

                pending.Add((tag, old, updated));
            }
        }

        private void Dispatch()
        {
            if (pending.Count == 0) return;

            var changes = new List<(GameplayTag Tag, int Old, int New)>(pending);
            pending.Clear();

            foreach (var listener in new List<Listener>(listeners))
            {
                if (listener.Removed) continue;

                foreach (var (tag, old, updated) in changes)
                {
                    if (!tag.Equals(listener.Tag) || old == updated) continue;

                    bool fires = listener.Type == TagEventType.AnyCountChange || (old == 0) != (updated == 0);
                    if (!fires) continue;

                    try
                    {
                        listener.Callback(tag, updated);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }

            listeners.RemoveAll(e => e.Removed);
            Changed?.Invoke();
        }
        #endregion



        #region Events
        /// <summary>
        /// Call <paramref name="_Callback"/> with the tag and its new count when the count of <paramref name="_Tag"/>
        /// (itself and its descendants) changes. Dispose the result to stop.
        /// </summary>
        public IDisposable RegisterTagEvent(GameplayTag _Tag, TagEventType _Type, Action<GameplayTag, int> _Callback)
        {
            if (_Callback == null) throw new ArgumentNullException(nameof(_Callback));

            var listener = new Listener { Tag = _Tag, Type = _Type, Callback = _Callback };
            listeners.Add(listener);
            return new Registration(() => listener.Removed = true);
        }

        private sealed class Registration : IDisposable
        {
            private Action dispose;

            public Registration(Action _Dispose) => dispose = _Dispose;

            public void Dispose()
            {
                dispose?.Invoke();
                dispose = null;
            }
        }
        #endregion
    }
}
