# Controls

Reusable UI Toolkit controls in `LMK.Editor.UIElements`. All are `[UxmlElement]`s, listed in UI Builder under *LMK*, and apply the LMK theme when used without a themed parent.

## PlaceholderTextField

A `TextField` that shows a placeholder while it is empty and not focused.

```csharp
var field = new PlaceholderTextField { PlaceholderText = "Name" };
```

UXML attribute: `placeholder-text` (default: `"Search"`).

## SearchBar

A rounded `PlaceholderTextField` with a search icon. `SearchableDropdown` uses it as its search field.

```csharp
var searchBar = new SearchBar();
searchBar.RegisterValueChangedCallback(e => Filter(e.newValue));
```

## NamedSeparator

A horizontal separator line with an optional label in front of it.

```csharp
var separator = new NamedSeparator("Advanced");
separator.Color = Color.clear; // Color.clear uses the theme colour --lmk-color-separator
```

UXML attributes: `text`, `color`.

## ToolbarButton

A `Button` for toolbars, working as a plain button or a toggle. It keeps every `Button` feature (`text`, `iconImage`, `clicked`, keyboard submit, `SetEnabled`).

```csharp
var refresh = new ToolbarButton(false, Refresh) { text = "Refresh" };

var pin = new ToolbarButton(true) { text = "Pin" };
pin.RegisterCallback<ChangeEvent<bool>>(e => SetPinned(e.newValue));
pin.SetValueWithoutNotify(true); // on, without a ChangeEvent
```

- `RegisterValueChangedCallback` doesn't compile on a `ToolbarButton`: `Button` also implements `INotifyValueChanged<string>`, so the value type can't be inferred.
- `UnityEditor.UIElements` has its own `ToolbarButton`. In a file using both namespaces, add `using ToolbarButton = LMK.Editor.UIElements.ToolbarButton;`.
- Display states, from lowest to highest highlight: normal, active (pressed in button mode, on in toggle mode), hovered. The background gets darker with each level.
- Square corners with a 1px border on the left and right. Each button has a -1px left margin, so buttons placed in a row share one border between them.
- In toggle mode each click flips `value` before other `clicked` handlers run. In button mode `value` is always `false`; switching `IsToggle` off resets it.
- Replacing `clickable` also removes the toggle behaviour.
- UXML attributes: `is-toggle`, `value`.
- Colours: `--lmk-color-button-background` / `-active` / `-hover`, border `--lmk-color-button-border`, icon tint `--lmk-color-icon`.

A row of toolbar buttons:

```csharp
var toolbar = new VisualElement();
toolbar.style.flexDirection = FlexDirection.Row;
toolbar.Add(new ToolbarButton(false, Undo) { text = "Undo" });
toolbar.Add(new ToolbarButton(false, Redo) { text = "Redo" });
```

## Switcher

A container that shows one child at a time.

```csharp
var switcher = new Switcher(leafView, branchView);
switcher.SwitchActive(1);           // by index
switcher.SwitchActive(branchView);  // by element
switcher.SwitchActive("leaf-view"); // by element name
```

- UXML attribute: `active-index`.
- Use the `Switcher`'s own `Add`, `Remove`, `RemoveAt` and `Clear`, so the active child stays valid.

## ResizeHandle

A drag handle for resizing any element. It resizes its parent directly, or only raises events for owners that keep the size in their own model.

```csharp
// Resizes the parent, no other code needed
var handle = new ResizeHandle(TwoPaneSplitViewOrientation.Vertical)
{
    ResizeParent = true,
    MinSize = 80,
};
panel.Add(handle);

// Events only, for a size kept in a model
var width = 0f;
var handle = new ResizeHandle(TwoPaneSplitViewOrientation.Horizontal);
handle.DragStarted += () => width = model.Width;
handle.Dragged += offset => model.Width = Mathf.Max(width + offset, 40);
handle.DragFinished += model.Save;
```

- `Horizontal` drags along X and resizes width. `Vertical` drags along Y and resizes height.
- `Dragged` reports the distance from the drag start, not a step, so a clamped size follows the pointer again as soon as the pointer returns. Positive is right or down.
- `MinSize` and `MaxSize` apply to `ResizeParent` only. A `MaxSize` of 0 means no limit.
- The handle doesn't position itself. Place it from the owner, e.g. absolutely on the parent's edge.
- Pressing the handle stops the event, so a `DragReorderManipulator` on an ancestor doesn't start as well.
- UXML attributes: `orientation`, `resize-parent`, `min-size`, `max-size`.
- Colours: hovered and dragged background `--lmk-color-item-hover`.

## DragReorderManipulator

A manipulator that drags the children of a container to reorder them, with a drop indicator. It only raises `Reordered`, so owners can keep the order in their own model.

```csharp
var manipulator = new DragReorderManipulator(FlexDirection.Row)
{
    CanReorder = child => child != pinnedChild, // optional
};
manipulator.Reordered += (from, to) =>
{
    var child = container[from];
    container.RemoveAt(from);
    container.Insert(to, child);
};
container.AddManipulator(manipulator);
```

- `to` is the child's index after the move: remove at `from`, then insert at `to`.
- Pass the container's `flexDirection` to the constructor. Reversed directions are supported.
- A drag starts after 5px, so plain clicks reach the children. Children that capture the pointer themselves, e.g. buttons, don't start a drag.
- Hidden children and children rejected by `CanReorder` can't be dragged and aren't drop slots. Rejected children stay in place.
- Dropping a child back on its own position raises nothing. There is no key to cancel a drag.
- Colours: drop indicator `--lmk-color-drop-indicator`.

## ObjectLabel

A read-only object reference, displayed like Unity's object field.

```csharp
var label = new ObjectLabel();
label.SetValue(texture);                     // thumbnail and name
label.SetValue(null, typeof(Texture2D));     // None (Texture2D)
label.SetValue(serializedProperty);          // also detects Missing (Texture2D)
```

- Only the `SerializedProperty` overload can show `Missing`, because telling a missing reference from an unassigned one needs the serialized instance ID.
- Sprites are drawn directly, including their region of an atlas. Other objects use their mini thumbnail.
- `label.SetValue(null)` is ambiguous between the overloads; write `label.SetValue(null, type)`.
- It isn't a field, so it can't be edited or focused, and it doesn't update by itself. Call `SetValue` again when the value changes.
- Long names get an ellipsis, and the full name shows as a tooltip.
- Colours: `--lmk-color-text-muted` for `None`, `--lmk-color-text-error` for `Missing`.

## ColorLabel

A read-only colour: a swatch and its hex value.

```csharp
var label = new ColorLabel(Color.red);    // #FF0000
label.Value = new Color(1, 0, 0, 0.5f);   // #FF000080
```

- Alpha is shown only when the colour isn't opaque.
- HDR values above 1 are clamped.
- UXML attribute: `value`.
