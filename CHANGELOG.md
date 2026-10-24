# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package follows [Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-10-24

### Added
- `[InlineInstanced]`: searchable type picker for `[SerializeReference]` fields, lists and arrays, with multi-object editing, undo, prefab overrides, missing-type warnings and an IMGUI fallback.
- `[CanReferenceInline]` / `[CanNotReferenceInline]`: control which classes are listed; the closest attribute in the inheritance chain wins.
- `[CustomReferencedPath]`: folder path, display name, tooltip and icon of a class in the picker.
