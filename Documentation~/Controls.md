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
