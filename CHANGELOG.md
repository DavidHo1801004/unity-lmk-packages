# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package follows [Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-10-31

### Added
- `ResizeHandle`: drag handle that resizes its parent or raises drag events.
- `DragReorderManipulator`: drag to reorder the children of a container, with a drop indicator.
- `ObjectLabel`: read-only object reference display with `None` and `Missing` states.
- `ColorLabel`: read-only colour swatch and hex value.
- `LMKTheme`: `--lmk-color-drop-indicator` variable.
- EditMode tests for the new elements, including real pointer events through a window.

### Fixed
- `ToolbarButton` documentation: the toggle example didn't compile. Use `RegisterCallback<ChangeEvent<bool>>`.

## [0.1.1] - 2026-10-27

### Added
- `ToolbarButton`: toolbar button with button and toggle modes (`IsToggle`, `value`, `ChangeEvent<bool>`). Buttons in a row share their borders.
- `LMKTheme`: `--lmk-color-button-background`, `-active`, `-hover` and `--lmk-color-button-border` variables.

## [0.1.0] - 2026-10-24

### Added
- `LMKTheme`: dark/light colour palette with public `--lmk-color-*` USS variables.
- `SearchableDropdown`: searchable popup with folder hierarchy and expand/collapse all.
- `HierarchyItem`: logical item hierarchy with registered, two-phase event callbacks (`SetExpandedEvent`, `FilterEvent`).
- `SearchQuery`: JEI-like search syntax (AND, OR `|`, NOT `-`, quoted phrases, `@` namespace and `#` tooltip prefixes).
- Controls: `SearchBar`, `PlaceholderTextField`, `NamedSeparator`, `Switcher`.
- `LMKAssetUtility`: package asset loading by path.
