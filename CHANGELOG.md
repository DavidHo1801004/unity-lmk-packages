# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package follows [Semantic Versioning](https://semver.org/).

## [0.4.0] - 2026-11-18

### Added
- `PropertyList`: reorderable list bound to an array or list `SerializedProperty`, in a collapsible panel with the element count and an add button. Rows have a drag handle, the element (a `PropertyField`, or `makeItem` / `bindItem` content) and a remove button. `AddRequested` lets the owner open a picker instead of adding an element, and `SetAddEnabled` disables the add button with a tooltip saying why. Edits go through the `SerializedObject` for Undo and prefab overrides, and multi-object editing edits each object separately.
- `AlignedField`: field with any content as its input, whose label lines up with the inspector's built-in fields, with an option to keep the label at the top of tall content.
- `FoldoutArrowManipulator`: a `Foldout` expands and collapses from its arrow only, while clicks elsewhere on the header still reach it.
- `CollapsiblePanel.PersistenceKey`: saves the collapsed state per user and project.
- EditMode tests for the new elements.

## [0.3.0] - 2026-11-10

### Added
- `CollapsiblePanel`: titled panel with header actions, collapsing to its header or sideways to a strip with a rotated title.
- `PanelLayout`: row or column of panels around a fill child, with resize handles, reordering by panel header, collapsing, and per-user layout saved through `EditorUserSettings`.
- `AnchoredPopup`: popup window with any content, placed under an anchor element, closed by Esc, focus loss or `Close()`.
- `FieldRowGroup` and `FieldRow`: label and value rows whose labels line up.
- `EditableLabel`: label edited in place by double-click or its pencil button, with character filtering, validation and a title variant.
- `LinkLabel`: clickable text styled as a link, usable from the keyboard.
- `DragReorderManipulator.CanStartDrag`: decide which presses start a drag, e.g. only on a handle.
- `LMKTheme`: `--lmk-color-link`, `-link-hover`, `-item-selected`, `-row-alternate` and `-popup-border` variables.
- EditMode tests for the new elements, including real popup windows, focus and keyboard input.

### Fixed
- `DragReorderManipulator` didn't start a drag when its target was another element's content container.

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
