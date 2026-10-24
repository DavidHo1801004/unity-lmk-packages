using System;
using System.Collections.Generic;

using UnityEngine.UIElements;

using LMK.Editor.Utilities;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// When a hierarchy event callback runs, relative to the item's children.
    /// </summary>
    public enum HierarchyEventPhase
    {
        /// <summary>
        /// Before the event reaches the item's children (parents first).
        /// </summary>
        BeforeChildren,

        /// <summary>
        /// After the event has reached all of the item's descendants (children first).
        /// Use it to act on results gathered from the descendants.
        /// </summary>
        AfterChildren,
    }

    /// <summary>
    /// Base class for visual elements that form a logical item hierarchy, e.g. nested menu items. <br/>
    /// Keeps parent/child links separate from the visual tree, and distributes <see cref="HierarchyItemEvent"/>s
    /// to the callbacks registered on each item.
    /// </summary>
    /// <remarks>
    /// Adding items and placing them in the visual tree is left to subclasses.
    /// </remarks>
    public abstract class HierarchyItem : VisualElement
    {
        private readonly List<HierarchyItem> childItems = new();

        // Multicast Action<TEvent> per event type and phase. Created on first registration.
        private Dictionary<(Type, HierarchyEventPhase), Delegate> callbacks;

        /// <summary>
        /// The parent item in the hierarchy, or <see langword="null"/> for the root item.
        /// </summary>
        public HierarchyItem ParentItem { get; private set; }

        /// <summary>
        /// Direct children of this item, in insertion order.
        /// </summary>
        public IReadOnlyList<HierarchyItem> ChildItems => childItems;

        /// <summary>
        /// The top item of the hierarchy this item belongs to.
        /// </summary>
        public HierarchyItem RootItem
        {
            get
            {
                var item = this;
                while (item.ParentItem != null)
                    item = item.ParentItem;
                return item;
            }
        }

        /// <summary>
        /// Whether this item has no parent item.
        /// </summary>
        public bool IsRoot => ParentItem == null;



        #region Events
        /// <summary>
        /// Add <paramref name="_Callback"/> to the callbacks invoked when this item receives a <typeparamref name="TEvent"/>.
        /// </summary>
        public void RegisterHierarchyCallback<TEvent>(Action<TEvent> _Callback,
            HierarchyEventPhase _Phase = HierarchyEventPhase.BeforeChildren)
            where TEvent : HierarchyItemEvent<TEvent>
        {
            if (_Callback == null) return;

            callbacks ??= new();
            var key = (typeof(TEvent), _Phase);
            callbacks[key] = Delegate.Combine(callbacks.TryGetValue(key, out var existing) ? existing : null, _Callback);
        }

        /// <summary>
        /// Remove <paramref name="_Callback"/>, registered with the same <paramref name="_Phase"/>.
        /// </summary>
        public void UnregisterHierarchyCallback<TEvent>(Action<TEvent> _Callback,
            HierarchyEventPhase _Phase = HierarchyEventPhase.BeforeChildren)
            where TEvent : HierarchyItemEvent<TEvent>
        {
            if (_Callback == null || callbacks == null) return;

            var key = (typeof(TEvent), _Phase);
            if (!callbacks.TryGetValue(key, out var existing)) return;

            var remaining = Delegate.Remove(existing, _Callback);
            if (remaining == null)
                callbacks.Remove(key);
            else
                callbacks[key] = remaining;
        }

        /// <summary>
        /// Send <paramref name="_Event"/> to this item and all of its descendants, depth first. <br/>
        /// Each item runs its <see cref="HierarchyEventPhase.BeforeChildren"/> callbacks, then its children,
        /// then its <see cref="HierarchyEventPhase.AfterChildren"/> callbacks, until propagation is stopped.
        /// </summary>
        public void DispatchHierarchyEvent(HierarchyItemEvent _Event)
        {
            if (_Event == null) return;

            DispatchRecursive(_Event);
        }

        /// <summary>
        /// Send <paramref name="_Event"/> to every item in the hierarchy, starting from <see cref="RootItem"/>.
        /// </summary>
        public void BroadcastHierarchyEvent(HierarchyItemEvent _Event)
        {
            RootItem.DispatchHierarchyEvent(_Event);
        }

        private void DispatchRecursive(HierarchyItemEvent _Event)
        {
            InvokeCallbacks(_Event, HierarchyEventPhase.BeforeChildren);

            for (int i = 0; i < childItems.Count && !_Event.IsPropagationStopped; i++)
                childItems[i].DispatchRecursive(_Event);

            if (!_Event.IsPropagationStopped)
                InvokeCallbacks(_Event, HierarchyEventPhase.AfterChildren);
        }

        private void InvokeCallbacks(HierarchyItemEvent _Event, HierarchyEventPhase _Phase)
        {
            if (callbacks == null) return;

            if (callbacks.TryGetValue((_Event.GetType(), _Phase), out var callback))
                _Event.Invoke(callback);
        }
        #endregion



        #region Links
        /// <summary>
        /// Called after <see cref="ParentItem"/> changes.
        /// </summary>
        protected virtual void OnParentItemChanged() { }

        /// <summary>
        /// Link <paramref name="_Child"/> as a child of this item. <br/>
        /// Only the logical hierarchy is changed: the subclass decides where the child goes in the visual tree.
        /// </summary>
        protected void AddChildItem(HierarchyItem _Child)
        {
            if (_Child == null || _Child == this || _Child.ParentItem == this) return;

            _Child.ParentItem?.childItems.Remove(_Child);
            childItems.Add(_Child);

            _Child.ParentItem = this;
            _Child.OnParentItemChanged();
        }

        /// <summary>
        /// Unlink all children of this item. They become roots of their own hierarchies.
        /// </summary>
        protected void ClearChildItems()
        {
            foreach (var child in childItems)
            {
                child.ParentItem = null;
                child.OnParentItemChanged();
            }
            childItems.Clear();
        }
        #endregion
    }



    /// <summary>
    /// Event distributed between <see cref="HierarchyItem"/>s. Derive from <see cref="HierarchyItemEvent{TEvent}"/>.
    /// </summary>
    public abstract class HierarchyItemEvent
    {
        /// <summary>
        /// Whether the remaining items and phases will not receive this event.
        /// </summary>
        public bool IsPropagationStopped { get; private set; }

        /// <summary>
        /// Stop sending this event to the remaining items and phases.
        /// </summary>
        public void StopPropagation() => IsPropagationStopped = true;

        // Invoke a multicast Action<TEvent> with this event, without reflection.
        internal abstract void Invoke(Delegate _Callback);
    }

    /// <summary>
    /// Typed base of hierarchy events, so callbacks receive the concrete event type.
    /// </summary>
    public abstract class HierarchyItemEvent<TEvent> : HierarchyItemEvent
        where TEvent : HierarchyItemEvent<TEvent>
    {
        internal sealed override void Invoke(Delegate _Callback) => ((Action<TEvent>)_Callback).Invoke((TEvent)this);
    }

    /// <summary>
    /// Ask items to expand or collapse their content.
    /// </summary>
    public sealed class SetExpandedEvent : HierarchyItemEvent<SetExpandedEvent>
    {
        public readonly bool expanded;

        public SetExpandedEvent(bool _Expanded)
        {
            expanded = _Expanded;
        }
    }

    /// <summary>
    /// Ask items to filter themselves by a search value. <br/>
    /// Items decide how they match and how they are hidden. The event keeps the shared rule:
    /// an item is kept if it matched, or if any of its descendants matched.
    /// </summary>
    /// <remarks>
    /// Mark matches in <see cref="HierarchyEventPhase.BeforeChildren"/>, and read <see cref="IsKept"/> in
    /// <see cref="HierarchyEventPhase.AfterChildren"/>, when every descendant has been processed.
    /// </remarks>
    public sealed class FilterEvent : HierarchyItemEvent<FilterEvent>
    {
        /// <summary>
        /// The raw search value.
        /// </summary>
        public readonly string filter;

        /// <summary>
        /// <see cref="filter"/> parsed once for every item. See <see cref="SearchQuery"/> for the syntax.
        /// </summary>
        public readonly SearchQuery query;

        private readonly HashSet<HierarchyItem> keptItems = new();

        /// <summary>
        /// Whether the filter has no terms, i.e. every item should be kept.
        /// </summary>
        public bool IsEmpty => query.IsEmpty;

        /// <summary>
        /// Number of <see cref="MarkMatched"/> calls during this dispatch.
        /// </summary>
        public int MatchedCount { get; private set; }

        public FilterEvent(string _Filter)
        {
            filter = _Filter;
            query = SearchQuery.Parse(_Filter);
        }

        /// <summary>
        /// Mark <paramref name="_Item"/> as matching the filter. It and all of its ancestors are kept.
        /// </summary>
        public void MarkMatched(HierarchyItem _Item)
        {
            if (_Item == null) return;

            MatchedCount++;
            for (var item = _Item; item != null && keptItems.Add(item); item = item.ParentItem) { }
        }

        /// <summary>
        /// Whether <paramref name="_Item"/> matched, or has a descendant that matched.
        /// </summary>
        public bool IsKept(HierarchyItem _Item) => _Item != null && keptItems.Contains(_Item);
    }
}
