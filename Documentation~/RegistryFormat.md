# Registry file format

Tags are stored in registry files (`*.tagregistry`). Only the Gameplay Tag Editor and explicit commands write them. You read them in diffs and code reviews, and you may have to resolve a merge conflict in one.

## Example

```
# gameplaytag-registry v3 — written by the Gameplay Tag Manager. Do not edit by hand.
registry  7338dde9672e4165b8c7a755fd361488
name      Main
kind      main
checksum  sha256:6f6d91060196a29fca222dae076fc970db3d10fcc100021fe4e468eb1d9388c0
# guid                           | parent                           | segment | flags    | description
1570a3ddc5764dd7b87e798c9691519e | 324a4b686e6d4c2ca07341735ba38fc6 | Ability | implicit |
324a4b686e6d4c2ca07341735ba38fc6 | -                                | Combat  | implicit |
c5dc0f9691c74e3e952497d169f1854b | 1570a3ddc5764dd7b87e798c9691519e | Attack  | -        | Any attack ability
```

This registry holds `Combat`, `Combat.Ability` and `Combat.Ability.Attack`.

## Header

| Line | Meaning |
|---|---|
| `registry` | The registry ID: 32 hex digits. Every reference stores it. **Never change it**: references to the registry would stop resolving. |
| `name` | Display name, shown in the tag picker and used for the generated class name. |
| `kind` | `main` (the project's own tags), `package` (shipped inside a package, read-only) or `generated` (made for a package that ships no registry). |
| `package`, `version` | Generated registries only: the package and version they were made from. A generated registry loads only while its package is installed. |
| `checksum` | SHA-256 of the row lines. It shows whether the file was edited outside the Gameplay Tag Editor. |

## Rows

One row per tag, sorted by tag ID: `guid | parent | segment | flags | description`.

- **guid:** the tag ID, 32 hex digits. It never changes, so references survive renames.
- **parent:** the parent's tag ID, or `-` for a top-level tag. A tag's path is its parent's path, a `.`, and its segment.
- **segment:** the last part of the path: letters, digits and `_`.
- **flags:** `implicit` for a tag that exists only as the parent of added tags, `-` otherwise.
- **description:** free text, to the end of the line.

Renaming a tag changes only its segment, so a rename is a one-line diff. Moving a tag changes only its parent, also a one-line diff, plus the `implicit` flag of a parent it leaves without children. Deleting a tag removes its row and the rows of its descendants.

## Validation

A file is rejected when a row has an invalid ID, its parent doesn't exist, two siblings share a segment, parents form a cycle, or it contains version-control conflict markers. A rejected file keeps its last valid version loaded, and the Tag Editor shows the errors.

A checksum that doesn't match the rows only warns: the file still loads.

## Line endings

The checksum is computed over the rows with LF line endings, so it's the same on every machine. Add this to your `.gitattributes` so Git never converts them:

```
*.tagregistry text eol=lf
*.tags text eol=lf
```

## Merge conflicts

Rows are sorted by ID, so two people adding tags usually change different rows. Both sides always change the `checksum` line, though. And columns are padded to the longest segment, so a new segment longer than any before re-pads every row. When a conflict happens:
1. Keep both sides' rows, unless both sides changed the same row.
2. Leave the `checksum` line as either side wrote it.
3. Open the Gameplay Tag Editor. The file loads with a checksum warning. Make any edit and save, and the checksum is written again. If you'd rather not edit, the warning is harmless.

Never resolve a conflict by giving a tag a new ID: references to the old one would break.
