# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package follows [Semantic Versioning](https://semver.org/).

## [1.1.0] - 2026-11-14

### Added
- `RegistryKindEditor<TRow, TAsset>.ValidatePackRows`: a kind's own pack rules on a package's registry, run after the core's, e.g. that gameplay attribute bounds never name the project's main registry.

### Fixed
- The first load of a kind's registries in Play Mode, e.g. after a domain reload on entering it, no longer warns that the registries changed during Play Mode.

## [1.0.0] - 2026-11-13

First release. Gameplay tags 1.1.0 is its first client.

### Added
- `RegistryGuid`, `RegistryResolution`, `RegistryStatus` and `IRegistryReference`.
- `RegistryDatabase`, `RegistryDatabase<TSelf, TRow>`, `RegistryDatabaseBuilder<TRow>`, `RegistryInfo` and `RegistryOrigin`.
- `RegistryFile`, `IRegistryFormat<TRow>`, `RegistryColumn`, `RegistryData<TRow>`, `RegistryRow<TRow>`, `RegistryParseResult<TRow>`, `RegistryAsset<TRow>`, `RegistryImporter<TRow, TAsset>` and `RegistryImport`.
- `RegistryKind`, `RegistryKindEditor<TRow, TAsset>`, `RegistryKinds`, `RegistrySnapshot<TRow>`, `RegistryEditorDatabase`, `RegistryPackages`, `RegistryAssets`, `RegistrySession`, `RegistryEdit`, `RegistryResult`, `RegistryLockProvider`, `NoneLockProvider`, `RegistryMarker` and `RegistryMarkerStore`.
- `RegistryReference`, `RegistryProperties`, `RegistryPropertyWalker`, `RegistryAssetScanner`, `RegistryPackage`, `RegistryPackageService`, `RegistryPackageResult` and `RegistrySettings`.
- `RegistryKey`, `RegistryReferenceIndex`, `RegistryReferenceTools`, `RegistryReferenceChange`, `RegistryReferenceApplyResult`, `RegistryImportMapping` and `RegistryImportResult`.
- `RegistryBuildProcessor`, `RegistrySceneProcessor`, `MissingReferenceIssue`, `MissingReferencePolicy` and `RegistryProjectChecks`.
- `RegistryCodeGen` and `RegistryCodeStyle`.
- `RegistryWindow`, `RegistryToolbar`, `RegistryMarkerField`, `RegistryImporterEditor<TRow, TAsset>`, `RegistryReferenceWindow`, `RegistryDrawerHelper`, `RegistryCommands`, and the *LMK → Registries* Project Settings page with a selectable lock provider.
