# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package follows [Semantic Versioning](https://semver.org/).

## [1.2.0] - 2026-11-14

### Added
- Move tags of the main registry, with their sub tags, under another tag or to the top level: drag a tag onto another tag or onto the main registry's header in the Tag Editor, where a blue-tinted preview shows where it will go. Only the tag's parent changes, so references follow, and the old constant name stays as an `[Obsolete]` alias. An implicit parent left without children becomes explicit. Tags can't be moved to another registry. Staged, with Undo.
- Named markers: define a marker once (a name and a colour) and put it on tags of any registry, several per tag. The Inspector panel's *Markers* row shows them as chips, with **×** to remove one and **+** to add one or make a new one.
- Tag rows show their markers after the tag's name, which they never cover: as named chips when there's room, otherwise as thin strips that expand on hover to show their name. When even the strips don't fit, Shift + scroll wheel scrolls them sideways. This replaces the single colour strip.
- *Manage Markers...* (options menu, the Inspector's **+** menu, *Tools → LMK → Gameplay Tags*): rename, recolour, delete and add markers, and drag them into the order rows show them in.
- Search tag trees by marker: `@wip` shows the tags with a marker whose name contains "wip", and `-@wip` the others. Search fields show the syntax in their tooltip.

### Changed
- Markers belong to gameplay tags, no longer to the registry core. They aren't staged any more: every change is saved at once, and *Save* and *Discard* don't affect them. Undo and Redo still do.
- `UserSettings/LMKGameplayTags/Markers.json` has a new layout (version 2). A file from 1.1.0 or earlier is converted when it first loads: each colour becomes a marker named "Marker 1", "Marker 2" and so on.
- Menus moved from *Tools → GASU* to *Tools → LMK*, next to the other LMK packages: *Tools → LMK → Gameplay Tags*, with the Gameplay Tag Editor at the top. The control panel is under *LMK* in the UI Builder library. The comment above generated code aliases names the new menu.
- The Inspector panel has a separator between the tag's registry line and its fields.

## [1.1.0] - 2026-11-13

Gameplay tags now run on the registry core, `com.lmk.registry` 1.0.0, which other registry kinds such as gameplay attributes share. Data and code written for 1.0.0 keep working: registry files, serialized tags (text and binary), the baked database asset and its GUID, generated constants and their aliases, markers, and the public API.

### Added
- `GameplayTagQuery`: a serializable tag query (any, all or none of tags, hierarchical or exact, with nested any, all or none of expressions), built from code with `GameplayTagQueryExpression`. It matches any `IGameplayTagSet`, which `GameplayTagContainer` now implements.
- `GameplayTagDatabase.ActiveDatabaseChanged`, with the previous and the new database.
- The Tag Editor's toolbar shows the lock provider when one is set.

### Changed
- Depends on `com.lmk.registry` 1.0.0. `GameplayTag` implements its `IRegistryReference`, and `GameplayTagDatabase` derives from its `RegistryDatabase<GameplayTagDatabase, TagRow>`.
- Tag IDs are `RegistryGuid`s (`LMK.Runtime.Registry`), with the same serialized layout and text form as `TagGuid`.
- Loading, staged editing, saving, Undo, markers, locking, the reference index and tools, package registries, mapping of imported assets, builds, project checks and code generation are the registry core's. *Save* in the Tag Editor saves the unsaved edits of every registry kind.
- Project settings live in `ProjectSettings/LMKRegistrySettings.asset`: the shared ones (lock provider, build policy) under *Project Settings → LMK → Registries*, the tag ones under *LMK → Gameplay Tags*. The 1.0.0 settings file is read once and carried over.
- *Rebuild Reference Index*, *Generate Registries For Package...*, *Validate Package*, *Pack Package...* and *Map References From Another Project...* are the registry core's commands, for every kind, under *Tools → LMK → Registries* and *Assets → LMK Registries*.
- The reference index moved to `Library/LMKRegistry/RefIndex` and rebuilds once. Import logs go to `Logs/LMKRegistry/`.
- `TagProjectChecks` forwards to `RegistryProjectChecks`, which checks every registry kind.

### Deprecated
- `TagGuid`: use `RegistryGuid`. It converts to and from `RegistryGuid` implicitly.

## [1.0.0] - 2026-11-11

First release.

### Added
- `GameplayTag` and `GameplayTagContainer`: a serializable tag reference and tag set, with Inspector drawers that support Undo, prefab overrides and multi-object editing. A reference stores its registry ID and tag ID, so it survives renames. `HasTag`, `HasAny` and `HasAll` match parent tags; their `Exact` versions don't. Unresolved tags match nothing.
- Registry files (`*.tagregistry`): a header (registry ID, name, kind, checksum) and one row per tag. Invalid files are rejected and the last valid version stays loaded; edits made outside the Gameplay Tag Editor are detected through the checksum.
- The Gameplay Tag Editor (*Tools → GASU → Gameplay Tag Editor*): add, rename, delete and describe tags of the main registry. Edits are staged until *Save*, *Discard* drops them, and Undo works on them. Saving is refused when the registry changed on disk since the edits began. A toolbar, a searchable tree grouped by registry, and an Inspector panel that can sit on either side of the tree, be resized and be collapsed.
- Renaming changes only a tag's last segment. Deleting a tag deletes its subtree, and removes implicit parents left empty unless you keep them.
- Tag markers: a personal colour per tag, shown as a strip on its row and kept in `UserSettings/`.
- A tag picker that lists live tags grouped by registry and selects each tag on its own.
- Inspector fields show `<name> (deleted)` for references that don't resolve, and offer to remove them. In prefab assets, missing entries become None instead of being removed, so instance overrides keep their indices. *Compact List* removes None entries.
- A reference index of which saved assets use which tags, kept in `Library/`. The Delete dialog lists the referencing assets. *Find References*, *Replace References* and *Relink Missing References* preview every change and rewrite only the selected ones.
- Registries shipped inside packages load read-only. *Generate Tag Registry For Package...* makes a registry for a package that ships none, so its references resolve without changing it; it loads only while the package is installed, and offers to regenerate when the package is updated.
- Pack validation for packages you author: *Validate Package Tags* and *Pack Package...*.
- Assets imported from another project through a `.unitypackage` have their tags mapped to the main registry by name. *Map Tags From Another Project...* maps selected assets.
- Tag files (`*.tags`, `<path> [; <description>]`): import into the main registry with a preview, export from any registry.
- Generated constants (`Tags.Main.Status_Debuff_Stun`) in `Assets/GameplayTags/Generated/GameplayTags.g.cs`, with `[Obsolete]` aliases for renamed tags until *Prune Unused Code Aliases* removes them.
- A runtime database baked into player builds and kept in Preloaded Assets. Built scenes get None in place of unresolved references; builds warn about them, or fail under *Project Settings → LMK → Gameplay Tags*.
- `TagProjectChecks`: registry integrity, build content and pack validation, for tests and CI.
- Hooks for a version control lock on tag edits (no locking by default).
- Documentation: scripting reference, registry file format, project checks.
- Edit mode tests.
