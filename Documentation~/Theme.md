# Theme

`LMK.Editor.Styles.LMKTheme` applies the LMK colour palette for the current Editor skin (dark or light).

## Applying the palette

```csharp
var root = new VisualElement();
LMKTheme.Apply(root); // adds the palette style sheet and the "lmk-theme" class
```

Every child of `root` can then use the variables in USS:

```css
.my-header {
    background-color: var(--lmk-color-header-background);
    border-color: var(--lmk-color-border);
}
```

- `LMKTheme.Apply` is safe to call more than once.
- `LMKTheme.ApplyIfStandalone(element)` applies the palette when the element is attached to a panel, and only if no ancestor already has it. Reusable controls use it, so they also work alone, e.g. in UI Builder.
- `LMKTheme.AddStyleSheet(element, assetPath)` adds any style sheet by asset path, e.g. `"Packages/com.lmk.editor/Editor/Assets/LMK/LMKThemeDark.uss"`.

Elements keep the palette they were built with when the Editor skin changes; rebuild them to update.

## Variables

| Variable | Used for |
|---|---|
| `--lmk-color-text-muted` | Secondary text, e.g. footers |
| `--lmk-color-text-placeholder` | Placeholder text |
| `--lmk-color-text-error` | Error text |
| `--lmk-color-icon` | Icon tint |
| `--lmk-color-separator` | Separator lines and labels |
| `--lmk-color-popup-background` | Popup window background |
| `--lmk-color-popup-content-background` | Popup content area |
| `--lmk-color-item-hover` | Hovered list items and buttons |
| `--lmk-color-header-background` / `-hover` | Foldout headers |
| `--lmk-color-border` | Header borders |
| `--lmk-color-content-border` | Foldout content borders |
| `--lmk-color-field-background` / `-hover` | Input fields |
| `--lmk-color-field-border` / `-hover` | Input field borders |
| `--lmk-color-button-background` / `-active` / `-hover` | Toolbar buttons (normal, active, hovered) |
| `--lmk-color-button-border` | Toolbar button borders |

The values are defined in `Editor/Assets/LMK/LMKThemeDark.uss` and `LMKThemeLight.uss`. Keep both files in sync, and use only these variables (no colour literals) in LMK USS files.
