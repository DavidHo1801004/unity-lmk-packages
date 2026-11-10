# Controls

Reusable UI Toolkit controls in `LMK.Editor.UIElements`. All are `[UxmlElement]`s, listed in UI Builder under *LMK*, and apply the LMK theme when used without a themed parent. `AnchoredPopup` is the exception: it is a popup window, in `LMK.Editor.Windows`.

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
- `CanStartDrag` gets the pressed element (the child or one of its descendants) and decides whether that press can start a drag. Use it to drag only by a handle, or to keep a child in place while it still counts as a drop slot. `null` accepts every press.
- The target can be a content container: children are found through `hierarchy.parent`, not `parent`.
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

## CollapsiblePanel

A titled panel with a header and a content area, which can be collapsed.

```csharp
var inspector = new CollapsiblePanel("Inspector") { MinSize = 180, MaxSize = 600 };
inspector.HeaderActions.Add(new ToolbarButton(true) { text = "Pin" }); // optional, right of the title
inspector.Add(content);                                                 // goes into the content area
inspector.CollapsedChanged += collapsed => { };
inspector.Collapsed = true;                    // raises CollapsedChanged
inspector.SetCollapsedWithoutNotify(false);    // doesn't
```

- The header has the title (with an ellipsis), the `HeaderActions` slot and a collapse button. Double-clicking the header toggles too.
- `CollapseAxis` `Vertical` (the default) collapses to the header, like a foldout. `Horizontal` collapses sideways to a 24px strip with the expand button at the top and the title rotated below it. `PanelLayout` sets it to its own orientation.
- The content is hidden, not removed, so its state survives.
- `Collapsible = false` hides the button and ignores double-clicks. Code can still set `Collapsed`.
- `MinSize` and `MaxSize` (0 = no limit) are used by `PanelLayout`. The panel doesn't keep its expanded size itself.
- UXML attributes: `title`, `collapsed`, `collapsible`, `min-size`, `max-size`, `orientation`.
- USS classes: `lmk-collapsible-panel`, `__header`, `__title`, `__header-actions`, `__collapse-button`, `__chevron`, `__content`, modifiers `--collapsed`, `--horizontal`.
- Colours: header `--lmk-color-header-background` / `-hover`, header border `--lmk-color-border`, outer border `--lmk-color-content-border`, chevron `--lmk-color-icon`.

## PanelLayout

A row (or column) of panels: one fill child takes the remaining space, the others are resized with a handle on their side facing the fill child, and `CollapsiblePanel`s are dragged by their header to change order.

```csharp
var layout = new PanelLayout { PersistenceKey = "MyPackage.MyWindowLayout" };
var tree = new VisualElement { name = "tree" };
layout.Add(tree);
PanelLayout.SetFill(tree, true);          // or the USS class lmk-panel-layout__fill

var inspector = new CollapsiblePanel("Inspector") { name = "inspector", MinSize = 180, MaxSize = 600 };
inspector.style.width = 260;              // starting width
layout.Add(inspector);

layout.LayoutChanged += () => { };        // order, size or collapse changed; already saved
layout.ResetLayout();                     // UXML/code order, default sizes and collapsed states
```

- **Resizing**: one handle per gap between visible children, on the edge of the neighbour that isn't the fill child. Sizes are clamped to the panel's `MinSize` / `MaxSize`, and a panel never takes more than the fill child can give up. The fill child never gets a fixed size. Give the other panels a starting width (or height).
- **Reordering**: drag a `CollapsiblePanel` by its header, or any child by a descendant with the USS class `lmk-panel-layout__drag-handle` (`PanelLayout.dragHandleUssClassName`). Presses on buttons, header actions and panel content never move panels. The fill child can't be dragged, but other panels can move to either side of it. `Reorderable = false` turns reordering off.
- **Collapsing** a `CollapsiblePanel` gives its space to the fill child. Expanding takes its size back, as far as the fill child can give it up. A collapsed panel has no handle. The chevron points away from the fill child while the panel is expanded, and towards it while collapsed.
- **Persistence**: with a `PersistenceKey`, order, sizes and collapsed states are saved with `EditorUserSettings` on every change, per user and per project, and restored when children appear. Children are identified by `name`, so each child needs a unique name: a warning is logged otherwise. Unknown names are ignored, and children missing from the saved layout keep their position. An empty key saves nothing.
- `Orientation` `Vertical` stacks panels in a column. Changing it resets sizes.
- UXML attributes: `orientation`, `persistence-key`, `reorderable`.
- USS classes: `lmk-panel-layout`, `__container`, `__panel` (resizable children), `__fill`, `__handle`, `__drag-handle`.

## AnchoredPopup

A small popup window with any content, placed under an anchor element. In `LMK.Editor.Windows`.

```csharp
var field = new PlaceholderTextField { label = "Name", PlaceholderText = "X.Y.Z" };
var popup = AnchoredPopup.Show(addButton, field, new Vector2(0, 2)); // under addButton, 2px gap
if (popup == null) return;

field.RegisterCallback<KeyDownEvent>(e =>
{
    if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter) { Add(field.value); popup.Close(); }
});
popup.Closed += () => { };
```

- Its top-left corner is at the anchor's bottom-left corner plus the offset. Unity moves it to stay on screen. Call `Show` from UI Toolkit code, e.g. a click handler.
- It sizes to its content, at least as wide as the anchor, and follows the content when it changes size. Pass a width to fix it.
- Esc, losing focus and `Close()` close it. `Closed` is raised once, whatever closed it. Keyboard focus goes back to the anchor, unless another window took it.
- Its first focusable element gets keyboard focus once it opens.
- `Show` returns `null` when Unity doesn't open the popup: Unity closes an open `AnchoredPopup` instead of opening a second one, and doesn't reopen one from the click that just closed it. So clicking the anchor again toggles the popup.
- It is a popup window, not an overlay, so it isn't clipped by the host window and closes when the Editor loses focus.
- USS class `lmk-anchored-popup`. Colours: background `--lmk-color-popup-background`, border `--lmk-color-popup-border`.

## FieldRowGroup and FieldRow

Label and value rows whose labels line up.

```csharp
var rows = new FieldRowGroup { RowSpacing = 4 };
rows.Add(new FieldRow("Explicit", explicitValue));
rows.Add(new FieldRow("Parent", parentLink));
rows.Add(new FieldRow("Description", descriptionField) { AlignTop = true });
```

- In a group every label gets the width of the widest one, measured again when a label changes or a row is added. `LabelWidth` fixes the width instead (longer labels get an ellipsis).
- The value area is a row, so a value and a button sit on one line. Fields in it (`unity-base-field`) stretch to fill it.
- `AlignTop` puts the label next to the first line of a tall value, e.g. a multi-line text field.
- Rows can be nested in other elements inside the group. Rows in a nested group belong to that group. A row outside any group keeps its own label width.
- UXML attributes: `label-width`, `row-spacing` (group, default 2); `label`, `align-top` (row).
- USS classes: `lmk-field-row-group`, `lmk-field-row`, `__label`, `__value`, `--align-top`. Label colour `--lmk-color-text-muted`.

## EditableLabel

A label whose text can be edited in place.

```csharp
var title = new EditableLabel { value = "Stun", IsValidChar = c => char.IsLetterOrDigit(c) || c == '_' };
title.AddToClassList(EditableLabel.titleVariantUssClassName);  // larger, bold
title.Validate = text => text.Length > 0 ? null : "Name is empty.";
title.RegisterValueChangedCallback(e => Rename(e.newValue));
```

- A double-click, a click on the pencil, or `BeginEdit()` show a text field with the text selected. Enter or keypad Enter commits, Esc cancels, `EndEdit(commit)` does either. There is no keyboard shortcut: the label itself takes no focus.
- The pencil shows while the label is hovered, and only the pencil highlights, when the pointer is over it. The label itself never highlights.
- The field's text starts exactly where the label's text was, so the text doesn't move when editing starts.
- A commit that changes the text raises one `ChangeEvent<string>`. An unchanged commit raises nothing, and typing in the field never raises events on the label.
- A `Validate` error keeps the field open, with the message as its tooltip and an error border. Editing clears it.
- `CommitOnBlur` (default `false`) decides whether losing focus commits or cancels. A blur that fails validation cancels, since the field has no focus left to fix it.
- `IsValidChar` filters typed characters, and pasted text on change.
- The field is focused a frame after the edit starts, so the click or menu that started it can't take focus back. Enter and Esc are handled while the key event trickles down, before the field can lose focus.
- `Editable = false` hides the pencil and ignores edit requests.
- UXML attributes: `value`, `commit-on-blur`, `editable`.
- USS classes: `lmk-editable-label`, `__label`, `__icon`, `__field`, `--editing`, `--invalid`, `--title`. Colours: pencil `--lmk-color-icon`, hovered pencil `--lmk-color-item-hover`, pressed pencil `--lmk-color-button-background-active`, error border `--lmk-color-text-error`.
- The pencil is the package sprite `Editor/Visual Elements/Assets/Sprites/pencil-symbol-white.png`, white and tinted by USS.

## LinkLabel

Text that acts as a link.

```csharp
var registry = new LinkLabel("Main") { tooltip = "Select the registry file" };
registry.clicked += () => EditorGUIUtility.PingObject(asset);
```

- `clicked` is raised on a left click, or on Enter, keypad Enter or Space while focused. It is focusable, so it works from the keyboard.
- On hover: `--lmk-color-link-hover`, an underline and the link cursor. Otherwise `--lmk-color-link`.
- `SetEnabled(false)` shows plain muted text without the hover effect, and raises nothing.
- USS class `lmk-link-label`.
