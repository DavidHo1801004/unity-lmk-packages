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
pin.RegisterValueChangedCallback(e => SetPinned(e.newValue));
pin.SetValueWithoutNotify(true); // on, without a ChangeEvent
```

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
