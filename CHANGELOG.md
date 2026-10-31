# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package follows [Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-10-31

### Added
- Player builds fail when a class listed by `[InlineInstanced]` doesn't exist in the player, e.g. one wrapped in `#if UNITY_EDITOR`. Saved data of such a class made the whole component fail to load, with no build error.
- Warning and **Make Unique** button on list elements sharing an instance, which Unity's Duplicate Array Element creates.
- Warning on fields with no class to list.
- Dependency on `com.unity.nuget.mono-cecil`.

### Changed
- Missing types are reported by one warning per Inspector, since Unity doesn't report which field held a missing class. Those fields display None.
- `object` fields no longer scan every loaded type.

### Removed
- `InlineInstancedTypeCacheData.customPathAttributes` and `Count`.

### Fixed
- Missing-type warnings never appeared.
- Picking the current type replaced the instance and wiped its values.
- Nested drawers were built more than once (22 builds for 6 nested fields).
- A constructor throwing an exception left the type picker open, and could change only some of the selected objects.
- Multi-object editing could show an outdated type after a script wrote a field without `SetDirty`, and threw an exception if a selected object was destroyed.

## [0.1.0] - 2026-10-24

### Added
- `[InlineInstanced]`: searchable type picker for `[SerializeReference]` fields, lists and arrays, with multi-object editing, undo, prefab overrides, missing-type warnings and an IMGUI fallback.
- `[CanReferenceInline]` / `[CanNotReferenceInline]`: control which classes are listed; the closest attribute in the inheritance chain wins.
- `[CustomReferencedPath]`: folder path, display name, tooltip and icon of a class in the picker.
