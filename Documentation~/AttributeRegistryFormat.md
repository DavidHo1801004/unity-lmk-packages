# Attribute registry format

Gameplay attributes are stored in registry files (`*.attrregistry`) on the shared registry core (`com.lmk.registry`), like gameplay tags. Only the Attribute Manager and explicit commands write them. You read them in diffs and code reviews, and you may have to resolve a merge conflict in one.

## Example

```
# gameplayattribute-registry v1 — written by the Attribute Manager. Do not edit by hand.
registry  6c4d7a20000000000000000000000001
name      Ability System Walkthroughs
kind      package
checksum  sha256:…
# guid                           | name                | default | min | max                                                                 | onMaxChange | flags | description
6c4d7a20000000000000000000000201 | Vitals.Health       | 100     | 0   | @6c4d7a20000000000000000000000001:6c4d7a20000000000000000000000202 | clamp       | -     | Hit points.
6c4d7a20000000000000000000000202 | Vitals.MaxHealth    | 100     | 1   | -                                                                   | clamp       | -     |
6c4d7a20000000000000000000000207 | Meta.IncomingDamage | 0       | -   | -                                                                   | clamp       | meta  | Damage before it is transferred to Health.
```

## Header

| Line | Meaning |
|---|---|
| `registry` | The registry ID: 32 hex digits. Every `GameplayAttribute` stores it. **Never change it**: references to the registry would stop resolving. |
| `name` | Display name, shown in the attribute picker and used for the generated class name. |
| `kind` | `main` (the project's own attributes, in `Assets/GameplayAttributes/Main.attrregistry`), `package` (shipped inside a package or a sample, read-only) or `generated` (made for a package that ships no registry). |
| `package`, `version` | Generated registries only: the package and version they were made from. |
| `checksum` | SHA-256 of the row lines. It shows whether the file was edited outside the Attribute Manager. |

## Rows

One row per attribute, sorted by attribute ID: `guid | name | default | min | max | onMaxChange | flags | description`.

- **guid:** the attribute ID, 32 hex digits. It never changes, so references survive renames.
- **name:** a dotted name such as `Vitals.Health`: segments of letters, digits and `_`. The dots group attributes in the picker; they don't make a hierarchy.
- **default:** the base value a unit starts with, unless its `AttributeSetDefinition` overrides it.
- **min**, **max:** the clamp bounds of base and current values: `-` for none, a number, or another attribute as `@registryId:attributeId` (its current value). An attribute may be bounded by an attribute of its own registry or of a registry the project or package depends on.
- **onMaxChange:** what happens to the base value when an attribute bound changes: `clamp` (clamp it into the new range) or `keepRatio` (scale it, so 50/100 becomes 75/150).
- **flags:** `meta` for an attribute that only carries a value into an execution (no change events, ignored by duration modifiers, reset after each execution, no bounds); `integer` for values rounded half away from zero; `-` for none. Several flags are joined with `,`.
- **description:** free text, to the end of the line.

## Validation

A file is rejected when a name is invalid or repeated, a value isn't finite, a bound names an attribute that doesn't exist, bounds form a cycle, a constant range is empty, or a meta attribute has bounds. A rejected file keeps its last valid version loaded, and the Attribute Manager shows the errors. A checksum that doesn't match the rows only warns: the file still loads.

## Line endings and merges

The checksum is computed over the rows with LF line endings. Add this to your `.gitattributes`:

```
*.attrregistry text eol=lf
```

Rows are sorted by ID, so two people adding attributes usually change different rows. Both sides always change the `checksum` line, though, and a name longer than any before re-pads every row. When a conflict happens:
1. Keep both sides' rows, unless both sides changed the same row.
2. Leave the `checksum` line as either side wrote it.
3. Open the Attribute Manager. The file loads with a checksum warning. Make any edit and save, and the checksum is written again. If you'd rather not edit, the warning is harmless.

Never resolve a conflict by giving an attribute a new ID: references to the old one would break.
