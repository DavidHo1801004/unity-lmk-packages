# LMK Attributes

Inspector attributes for Unity. `[InlineInstanced]` adds a searchable type picker to `[SerializeReference]` fields, so designers can choose, create and edit polymorphic objects directly in the Inspector.

## Scope

**Included:**
- `[InlineInstanced]`: type picker for `[SerializeReference]` fields, lists and arrays. Supports multi-object editing, undo, prefab overrides, missing-type and shared-instance warnings, a build check for classes missing from the player, and both UI Toolkit and IMGUI inspectors.
- `[CanReferenceInline]` / `[CanNotReferenceInline]`: choose which classes appear in the picker.
- `[CustomReferencedPath]`: folder path, display name, tooltip and icon of a class in the picker.

**Not included:** runtime behaviour. All attributes are editor metadata, removed from player builds by `[Conditional("UNITY_EDITOR")]`.

## Requirements

- Unity 6000.3 or newer.
- `com.lmk.editor` 0.1.0 (dependency).
- `com.unity.nuget.mono-cecil` 1.11.6 (dependency, installed automatically from the Unity registry).

## Installation

- **OpenUPM (recommended):** `com.lmk.editor` is installed automatically as a dependency.
  - With [openupm-cli](https://openupm.com/docs/getting-started-cli.html): `openupm add com.lmk.attributes`
  - Or add to `Packages/manifest.json`:
    ```json
    "scopedRegistries": [
      { "name": "OpenUPM", "url": "https://package.openupm.com", "scopes": [ "com.lmk" ] }
    ],
    "dependencies": {
      "com.lmk.attributes": "0.2.0"
    }
    ```
- **Git URL:** add **both** packages in *Package Manager → + → Add package from git URL*, editor first:
  1. `https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.editor/0.1.0`
  2. `https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.attributes/0.2.0`

  The Package Manager doesn't resolve dependencies from git URLs, so `com.lmk.editor` must be added by hand.
  Update both tags together when upgrading.
- **Embedded:** copy both `com.lmk.editor` and `com.lmk.attributes` into your project's `Packages/` folder.

Each package is published on its own branch (`upm/com.lmk.attributes`, `upm/com.lmk.editor`) with one tag per version (`com.lmk.attributes/<version>`).

## Quick start

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using LMK.Runtime.Attributes;

public class Enemy : MonoBehaviour
{
    [SerializeReference, InlineInstanced] private EnemyBehaviour behaviour;
    [SerializeReference, InlineInstanced] private List<EnemyBehaviour> extraBehaviours = new();
}

[Serializable, CanReferenceInline]
public abstract class EnemyBehaviour { }

[Serializable, CustomReferencedPath("Movement/Patrol", "Walks between points")]
public class Patrol : EnemyBehaviour { public float speed = 2f; }
```

## Documentation

- [InlineInstanced](Documentation~/InlineInstanced.md): setup, which classes are listed, collections, missing types.
- [CustomReferencedPath](Documentation~/CustomReferencedPath.md): paths, rich text and icons.
- [Search syntax](Documentation~/SearchSyntax.md): filtering the type picker.

## License

[MIT](LICENSE.md)
