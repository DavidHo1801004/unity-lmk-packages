# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package follows [Semantic Versioning](https://semver.org/).

## [0.5.0] - 2026-11-19

### Added
- `LMKTheme`: `--lmk-color-row-header` variable, a grey between the window background and foldout headers; `--lmk-color-button-destructive` / `-hover` and `--lmk-color-icon-destructive` for destructive buttons such as remove.
- `AlignedField` layout tests in PropertyList rows (drawer and custom content), nested structs and lists of nested structs, in a scroll view with and without a scroll bar, and in a container like the Inspector window's.
- Vector icons `Editor/Assets/Icons/Commons/plus.svg` and `minus.svg`. The package now depends on the built-in `com.unity.modules.vectorgraphics` module, which imports them as `VectorImage`.

### Changed
- `CollapsiblePanel`: the collapse button is on the left of the header, as Unity's foldout arrows, with a line between it and the title.
- `PropertyList` header: the count reads "1 item" or "n items", with a line between it and the add button. Add and remove show 7px vector icons with 1px strokes instead of `+` and `-` text, and the remove button is red, as com.lmk.gameplaytags' tag remove button. The rows' drag handles are as wide as the header's collapse button, and their remove buttons as the add button, so their separators line up in columns.
- `PropertyList` look: rounded corners, an 18px header and a 2px side margin. Rows no longer alternate colours: a line in the panel's outline colour divides them, and only each row's first line is tinted (`--lmk-color-row-header`), so an expanded element's content isn't. Separators on that line, in the same outline colour, divide the drag handle, the element and the remove button, which fills the end of the line with rounded outer corners. Content shorter than the line is centred on it. `__row--alternate` is still applied to odd rows but no longer styled.

### Fixed
- `PropertyList` header buttons stuck 3px out of the header: `ToolbarButton`'s minimum height overrode theirs. They and `CollapsiblePanel`'s collapse button now fill the header's height, expanded or collapsed, where the header loses its bottom line and they used to leave a 1px gap.
- `PropertyList` rows in an inspector: Unity's `margin-right: -2px` on inspector fields slid row content 2px under the remove button.
- `ToolbarButton` text is centred, as in Unity's buttons, so text used as an icon (e.g. `PropertyList`'s `+` and `-`) is no longer top-left.
- `PropertyList` editing several objects missed changes made outside it to any object but the first, even after `Refresh()`: it stayed read-only after their lists became the same size, or stayed editable after they stopped matching. Unity only re-reads whether the objects' values differ when the first one changes; the list now clears that cache and watches every object's dirty count.
- `PropertyList.Refresh()` logged "SerializedObject target has been destroyed." once an edited object was destroyed. The list now checks every object first, and clears its rows by itself when one is destroyed.

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
