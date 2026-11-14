using System;
using System.Collections.Generic;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// A list that may change while it is iterated, the equivalent of UE's scoped active effect and
    /// ability list locks. While locked, an added item waits outside the iteration and a removed item is skipped;
    /// both are applied when the last lock is released.
    /// </summary>
    /// <remarks>
    /// An item counts as removed as soon as <c>isRemoved</c> says so: <see cref="Find"/> and iteration skip it at once.
    /// Only its slot waits.
    /// </remarks>
    internal sealed class LockableList<T> where T : class
    {
        private readonly List<T> items = new();
        private readonly List<T> pendingAdds = new();
        private readonly Func<T, bool> isRemoved;
        private int lockCount;

        public LockableList(Func<T, bool> _IsRemoved)
        {
            isRemoved = _IsRemoved ?? throw new ArgumentNullException(nameof(_IsRemoved));
        }

        public bool IsLocked => lockCount > 0;

        /// <summary>
        /// The live items, pending adds included.
        /// </summary>
        public int LiveCount
        {
            get
            {
                int count = 0;
                foreach (var item in items) if (!isRemoved(item)) count++;
                foreach (var item in pendingAdds) if (!isRemoved(item)) count++;
                return count;
            }
        }

        public void Lock() => lockCount++;

        public void Unlock()
        {
            if (lockCount == 0) throw new InvalidOperationException("The list is not locked.");
            if (--lockCount > 0) return;

            items.RemoveAll(e => isRemoved(e));
            foreach (var item in pendingAdds)
                if (!isRemoved(item))
                    items.Add(item);
            pendingAdds.Clear();
        }

        public void Add(T _Item)
        {
            if (lockCount > 0) pendingAdds.Add(_Item);
            else items.Add(_Item);
        }

        /// <summary>
        /// Erase an item that <c>isRemoved</c> already reports as removed: now, or when the list unlocks.
        /// </summary>
        public void Erase(T _Item)
        {
            if (lockCount > 0) return;

            items.Remove(_Item);
            pendingAdds.Remove(_Item);
        }

        public T Find(Predicate<T> _Match)
        {
            foreach (var item in items)
                if (!isRemoved(item) && _Match(item))
                    return item;

            foreach (var item in pendingAdds)
                if (!isRemoved(item) && _Match(item))
                    return item;

            return null;
        }

        /// <summary>
        /// The live items in order, pending adds last, as a new list.
        /// </summary>
        public List<T> Snapshot()
        {
            var result = new List<T>(items.Count + pendingAdds.Count);
            foreach (var item in items) if (!isRemoved(item)) result.Add(item);
            foreach (var item in pendingAdds) if (!isRemoved(item)) result.Add(item);
            return result;
        }

        /// <summary>
        /// Iterate the items in place, skipping removed ones and never seeing pending adds. Lock the list first if the
        /// visit may change it.
        /// </summary>
        public void ForEach(Action<T> _Visit)
        {
            for (int i = 0; i < items.Count; i++)
                if (!isRemoved(items[i]))
                    _Visit(items[i]);
        }

        /// <summary>
        /// Drop every item at once, without unlock processing.
        /// </summary>
        public void Clear()
        {
            items.Clear();
            pendingAdds.Clear();
        }
    }
}
