# LMK Registry

Shared core for ID-based registries in Unity. A registry is a file of rows, each with a permanent ID; assets reference a row by (registry ID, row ID), so renaming a row never changes the assets that use it.

Domain packages register a *registry kind* (a row schema and its rules) and get the rest from this package. `com.lmk.gameplaytags` (tags) and `com.lmk.abilitysystem` (gameplay attributes) are its clients.

## Scope

**Included:**
- `RegistryGuid`: a 128-bit ID with a frozen serialized layout and a 32-digit hex text form.
- `RegistryResolution` and `RegistryStatus`: exact resolution of a reference to `Live`, `Deleted`, `MissingRegistry` or `None`.
- `IRegistryReference`: the contract of serialized references such as `GameplayTag`.
- `RegistryDatabase<TSelf, TRow>`: the baked, read-only database of one registry kind, with O(1) resolution, debug name lookup, a content hash, and the kind's active database with `Generation` and `ActiveChanged(previous, current)`. Built by `RegistryDatabaseBuilder<TRow>`.
- Registry files: `RegistryFile` reads and writes every kind's files (shared header, padded columns, rows sorted by ID, checksum, atomic writes), with the kind's columns and rules supplied through `IRegistryFormat<TRow>`. `RegistryImporter<TRow, TAsset>` and `RegistryAsset<TRow>` are the bases of a kind's importer.
- Registry kinds: a domain derives `RegistryKindEditor<TRow, TAsset>` (format, row names, database bake) and registers it with `RegistryKinds`. The core loads every registry file of the kind, publishes snapshots and the editor database, keeps the last valid version of a rejected file, and skips generated registries whose package isn't installed.
- Staged editing: `RegistryEdit.Apply` stages a change with Undo; `RegistrySession.Save` writes every kind's changes under one lock request, and `Discard` drops them. Saving is refused when a registry changed on disk since editing began. Per-user markers are staged with the edits. `RegistryLockProvider` is public and pluggable.
- References: `RegistryAssetScanner` reads the references of every kind an asset holds (text files as text, each reference placed by its registry; binary files and unplaceable references by walking the loaded asset), with `RegistryPropertyWalker` and `RegistryProperties`. `RegistryReferenceIndex` keeps one index in `Library/` of which saved assets reference which rows, of every kind. `RegistryReferenceTools` plans and applies Relink by name and Replace with a preview, skipping references changed since.
- Packages: *Assets → LMK Registries → Generate Registries For Package...* scans a package once and writes a generated registry per kind and unknown registry ID, so its references resolve without changing it. Package updates only prompt to regenerate. *Validate Package* and *Pack Package...* check a package you author for every kind it uses, and record its registry IDs. *Map References From Another Project...* (and every `.unitypackage` import) rewrites references into another project's registries to this project's main registries by name, for every kind.
- Builds: before a player build, every kind's database is baked from its saved registries into `<RootFolder>/Generated/Resources/<Database>.asset` and kept in Preloaded Assets. Unresolved references in Resources content and build scenes are reported per kind, following one Missing References In Builds policy (Warn or Fail) with optional per-kind overrides; the scene copies going into the build get None in their place. `RegistryProjectChecks` runs registry integrity, build content and pack validation for every kind, from tests or the command line.
- Generated code: a kind that returns a `RegistryCodeStyle` gets one constants file, e.g. `GameplayTags.g.cs`, with a nested class per registry and a constant per row that survives renames. Renamed rows keep their old names as `[Obsolete]` aliases until *Tools → LMK → Registries → Prune Unused Code Aliases*; deleted rows lose theirs. The file is written when edits are saved and at flush points (focus loss, Play Mode, builds, quit), only when its content changed. Project settings turn it off per registry, and set a namespace, and optionally an assembly, per kind.
- Editor UI shared by every kind: `RegistryWindow` (a registry window that asks to save or discard every kind's unsaved edits when it closes), `RegistryToolbar` (Save, Discard, lock provider), `RegistryMarkerField`, `RegistryImporterEditor<TRow, TAsset>` (the registry file Inspector), `RegistryReferenceWindow` (Find References, Relink and Replace with previews), `RegistryDrawerHelper` (missing labels and prefab-safe removal of missing references), *Tools → LMK → Registries* commands, and *Project Settings → LMK → Registries* (lock provider, build policy, code namespace and assembly).

Domain packages add their own windows, pickers and drawers on top of these.

## Adding a registry kind

A domain package adds a kind in four parts. `com.lmk.gameplaytags` is the reference implementation (`TagKind`).

1. **Runtime:** a reference struct with the three frozen fields `_registry`, `_id` and `_lastKnownName` that implements `IRegistryReference`, and a database `MyDatabase : RegistryDatabase<MyDatabase, MyRow>` holding the kind's own row data. Build it with `RegistryDatabaseBuilder<MyRow>`.
2. **Files:** a file row struct (every column after `guid`), an asset `MyRegistryAsset : RegistryAsset<MyFileRow>`, and an importer `MyRegistryImporter : RegistryImporter<MyFileRow, MyRegistryAsset>` with `[ScriptedImporter(1, "myregistry", RegistryImport.IMPORT_ORDER)]`.
3. **Kind:** `MyKind : RegistryKindEditor<MyFileRow, MyRegistryAsset>`, which supplies the format (format line, columns, row parse and write, integrity rules), row names, the database bake and `ReferenceType`. Register one instance with `RegistryKinds.Register` and hook its database with `RegistryEditorDatabase.Hook<MyDatabase, MyRow>` when the editor loads. Return a `RegistryCodeStyle` from `CodeStyle` for generated constants.
4. **Edits and UI:** write the kind's operations on `RegistryEdit.Apply`, and its window on `RegistryWindow`, `RegistryToolbar` and the other shared elements.

Everything else (loading, staging, saving, locks, markers, references, packages, builds, project checks and code generation) then works for the kind.

## Requirements

- Unity 6000.3 or newer.
- `com.lmk.editor` 0.3.0 (dependency).

## Installation

- **OpenUPM (recommended):** dependencies are installed automatically. `openupm add com.lmk.registry`, or add the OpenUPM scoped registry (scope `com.lmk`) and `"com.lmk.registry": "1.0.0"` to `Packages/manifest.json`. A domain package such as `com.lmk.gameplaytags` installs it for you.
- **Git URL:** add both packages in *Package Manager → + → Add package from git URL*, the editor library first, since Git URLs don't resolve dependencies:
  1. `https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.editor/0.3.0`
  2. `https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.registry/1.0.0`
- **Embedded:** copy the `com.lmk.registry` folder into your project's `Packages/` folder, and install `com.lmk.editor`.

The package is published on branch `upm/com.lmk.registry`, with one tag per version (`com.lmk.registry/<version>`).

## Assemblies and namespaces

| Assembly | Namespace |
|---|---|
| `LMK.Registry.Runtime` | `LMK.Runtime.Registry` |
| `LMK.Registry.Editor` | `LMK.Editor.Registry` |
| `LMK.Registry.Editor.Tests` | `LMK.Editor.Registry.Tests` |

## License

[MIT](LICENSE.md)
