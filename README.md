# LMK Editor

Shared UI Toolkit editor library used by the LMK packages. It provides a colour theme, a searchable hierarchical dropdown and a few reusable editor controls.

## Scope

**Included** (editor only, no runtime code):
- `LMKTheme`: dark/light colour palette as public `--lmk-color-*` USS variables.
- `SearchableDropdown`: popup with a search bar, a folder hierarchy built from `"Folder/Sub/Item"` paths, expand/collapse all, and a JEI-like search syntax.
- `HierarchyItem`: base class for logical item hierarchies with registered, two-phase event callbacks.
- `SearchQuery`: search text parser (AND, OR, NOT, phrases, field prefixes).
- Controls: `SearchBar`, `PlaceholderTextField`, `NamedSeparator`, `ToolbarButton`, `Switcher` (also available in UI Builder under *LMK*).

**Not included:** runtime features, or inspector attributes (see `com.lmk.attributes`).

## Requirements

- Unity 6000.3 or newer.

## Installation

- **OpenUPM (recommended):**
  - With [openupm-cli](https://openupm.com/docs/getting-started-cli.html): `openupm add com.lmk.editor`
  - Or add to `Packages/manifest.json`:
    ```json
    "scopedRegistries": [
      { "name": "OpenUPM", "url": "https://package.openupm.com", "scopes": [ "com.lmk" ] }
    ],
    "dependencies": {
      "com.lmk.editor": "0.1.1"
    }
    ```
- **Git URL:** in *Package Manager → + → Add package from git URL*, enter
  `https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.editor/0.1.1`
- **Embedded:** copy the `com.lmk.editor` folder into your project's `Packages/` folder.

The package is published on branch `upm/com.lmk.editor`, with one tag per version (`com.lmk.editor/<version>`).

## Quick start

```csharp
var dropdown = new SearchableDropdown(
    new GUIContent("None"),
    new GUIContent("Shapes/Circle"),
    new GUIContent("Shapes/Square", "A square tooltip"));
dropdown.OnSelectedItemChanged += index => Debug.Log($"Selected {index}");
UnityEditor.PopupWindow.Show(buttonRect, dropdown);
```

## Documentation

- [Theme](Documentation~/Theme.md): palette variables and applying them to your own UI.
- [Searchable dropdown](Documentation~/SearchableDropdown.md): dropdown, `HierarchyItem` events and search syntax.
- [Controls](Documentation~/Controls.md): `SearchBar`, `PlaceholderTextField`, `NamedSeparator`, `ToolbarButton`, `Switcher`.

## License

[MIT](LICENSE.md)
