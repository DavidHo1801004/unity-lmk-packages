# Search syntax

The type picker's search bar uses a JEI-like syntax. Hover the search bar for a summary.

| Query | Matches |
|---|---|
| `move patrol` | both terms, in any order |
| `patrol\|chase` | either group; each group can have several terms |
| `move -patrol` | `move` items, except those containing `patrol` |
| `"move/pat"` | the exact phrase, spaces included |
| `@game.ai` | namespace or assembly contains `game.ai` |
| `#walks` | tooltip contains `walks` |
| `-@editor` | prefixes combine with `-` |

- Terms without a prefix search the item's full path, e.g. `Movement/Patrol (Patrol)`, so a folder name matches every class inside it.
- Matching ignores case and rich text tags.
- Inside quotes, `-`, `@` and `#` are plain text.
- A folder is hidden when none of its items match. The footer shows how many items match.
