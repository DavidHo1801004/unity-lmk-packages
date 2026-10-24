# CustomReferencedPath

`[CustomReferencedPath(customName, toolTip, iconPath)]` sets how a class appears in the `[InlineInstanced]` type picker. Every parameter is optional.

```csharp
[Serializable, CustomReferencedPath("Movement/Patrol", "Walks between points", "d_ScriptableObject Icon")]
public class Patrol : EnemyBehaviour { }
```

## Name and path

- Without the attribute, a class is listed under its type name. If two listed classes share a name, both use their full names.
- `"Movement/Patrol"`: item **Patrol** in folder **Movement**, followed by the dimmed type name, e.g. *Patrol (Patrol)*.
- `"Movement/"`: a path ending in `/` uses the type name as the item name, e.g. *Movement / PatrolBehaviour*.
- Rich text is supported, e.g. `"<color=#ff3434>Danger</color>/Explode"`. A `/` inside a closing tag (`</color>`) is not a folder separator.
- The list is sorted by custom name (or type name), ignoring case and rich text.

## Tooltip

Shown when hovering the item, and searchable with `#` (see [Search syntax](SearchSyntax.md)).

## Icon

`iconPath` is resolved in this order:
1. A built-in editor icon name, e.g. `"d_ScriptableObject Icon"`.
2. A texture inside a `Resources` folder, without extension, e.g. `"Icons/Patrol"`.
3. A texture asset path, e.g. `"Assets/Art/Icons/Patrol.png"`.

If none matches, a warning names the class. Icons are shown in the UI Toolkit picker only.
