using UnityEngine;
using UnityEngine.UIElements;

namespace LMK.Editor.UIElements
{
    /// <summary>
    /// Makes a <see cref="Foldout"/> expand and collapse from its arrow only, for tree rows whose header is clicked to
    /// select. Add it to the foldout: <c>foldout.AddManipulator(new FoldoutArrowManipulator())</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Foldout.toggleOnLabelClick"/> only spares the label: a click on the header's padding, border or free
    /// space still toggles it. With this manipulator, those clicks still reach the header, for selection, context menus
    /// and dragging; only the toggle they cause is undone, before the foldout changes. <br/>
    /// Keyboard and code changes still toggle the foldout. <br/>
    /// The arrow isn't a pick target, so it is found by position.
    /// </remarks>
    public class FoldoutArrowManipulator : Manipulator
    {
        private Foldout foldout;

        private Toggle header;

        private VisualElement arrow;

        // A left press on the header, away from the arrow, whose release hasn't come yet.
        private bool pressedOffArrow;

        // Set by the release of such a press inside the header, which toggles the header. Cleared by that toggle.
        private bool expectingClickToggle;



        protected override void RegisterCallbacksOnTarget()
        {
            foldout = target as Foldout;
            header = foldout?.Q<Toggle>(className: Foldout.toggleUssClassName);
            arrow = header?.Q(className: Toggle.checkmarkUssClassName);

            if (header == null || arrow == null)
            {
                Debug.LogWarning($"{nameof(FoldoutArrowManipulator)} needs a {nameof(Foldout)} as its target, not {target?.GetType().Name ?? "null"}.");
                foldout = null;
                return;
            }

            // Trickle-down: before the header's own handlers, which may stop these events.
            header.RegisterCallback<PointerDownEvent>(OnHeaderPointerDown, TrickleDown.TrickleDown);
            header.RegisterCallback<PointerUpEvent>(OnHeaderPointerUp, TrickleDown.TrickleDown);
            header.RegisterCallback<ChangeEvent<bool>>(OnHeaderValueChanged, TrickleDown.TrickleDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            if (foldout == null) return;

            header.UnregisterCallback<PointerDownEvent>(OnHeaderPointerDown, TrickleDown.TrickleDown);
            header.UnregisterCallback<PointerUpEvent>(OnHeaderPointerUp, TrickleDown.TrickleDown);
            header.UnregisterCallback<ChangeEvent<bool>>(OnHeaderValueChanged, TrickleDown.TrickleDown);

            pressedOffArrow = false;
            expectingClickToggle = false;
            foldout = null;
            header = null;
            arrow = null;
        }



        private void OnHeaderPointerDown(PointerDownEvent _Event)
        {
            expectingClickToggle = false;
            pressedOffArrow = _Event.button == 0 && !arrow.worldBound.Contains(_Event.position);
        }

        // The header toggles when a press is released inside it. The toggle can be queued until after this event,
        // so it is expected here and recognised when it comes.
        private void OnHeaderPointerUp(PointerUpEvent _Event)
        {
            expectingClickToggle = pressedOffArrow && _Event.button == 0 && header.worldBound.Contains(_Event.position);
            pressedOffArrow = false;

            // In case the header ignores the click, e.g. while disabled, so a later keyboard toggle isn't undone.
            if (expectingClickToggle)
                header.schedule.Execute(() => expectingClickToggle = false);
        }

        // Only input changes the header itself: code that sets the foldout updates the header without an event.
        private void OnHeaderValueChanged(ChangeEvent<bool> _Event)
        {
            if (_Event.target != header || !expectingClickToggle) return;

            expectingClickToggle = false;
            _Event.StopImmediatePropagation();

            // The foldout's value, not the event's previous value: a click handler may have set the foldout meanwhile.
            header.SetValueWithoutNotify(foldout.value);
        }
    }
}
