# Scripting reference

The runtime API, in the `LMK.Runtime.GameplayTags` namespace. Everything here works in the editor and in players.

## GameplayTag

A serializable reference to one tag. It stores the tag's registry ID, its tag ID and the path it had when it was assigned. The path is only shown when the tag doesn't resolve.

| Member | Description |
|---|---|
| `GameplayTag.None` | The unassigned tag. It matches nothing and isn't a missing reference. |
| `IsNone` | Whether the tag is unassigned. |
| `RegistryId`, `Id` | The tag's identity. |
| `Path` | The current path if the tag resolves, otherwise `LastKnownName`. |
| `LastKnownName` | The path when the tag was assigned. Display only. |
| `IsLive` | Whether the tag resolves to a tag of a loaded registry. |
| `Resolve()` | A `TagResolution`: `Status` (`None`, `Live`, `Deleted`, `MissingRegistry`) and the `Handle` of a live tag. |
| `Parent` | The direct parent of a live tag, or `None`. |
| `MatchesTag(parent)` | Whether this tag is `parent` or one of its descendants. `"A.B.C"` matches `"A.B.C"`, `"A.B"` and `"A"`. |
| `MatchesTagExact(other)` | Whether both resolve to the same tag. |
| `Equals(other)` | Same registry ID and tag ID. Safe as a dictionary key. |
| `FromId(registry, id, name)` | A reference from its IDs, e.g. from save data. |
| `FromName(path)`, `FromName(registry, path)` | The live tag at a path, or `None`. Debug and console use only: a path lookup doesn't survive renames. |

There is no `==` operator, so code has to choose a comparison:
- **`Equals`** compares stored IDs. Use it for data: dictionary keys, save data, deduplication.
- **`MatchesTag` and `MatchesTagExact`** compare resolved tags. Use them for gameplay. A tag that doesn't resolve matches nothing, not even itself.

Tags never match across registries: `Status` in the main registry and `Status.Stun` in a package registry are unrelated.

## GameplayTagContainer

A serializable set of tags.

| Member | Description |
|---|---|
| `AddTag(tag)`, `AddTags(container)` | Add tags that aren't stored yet. `None` is ignored. Returns whether the container changed. |
| `RemoveTag(tag, includeChildren)` | Remove a tag, and with `includeChildren` every stored descendant of it. |
| `RemoveTags(container)`, `Clear()` | Remove several tags, or all of them. |
| `HasTag(tag)` | Whether the container holds `tag` or a descendant of it: `{"A.B"}` has `"A.B"` and `"A"`, not `"A.B.C"`. |
| `HasTagExact(tag)` | Whether the container holds `tag` itself. |
| `HasAny(container)`, `HasAnyExact(container)` | Whether any tag of the other container passes `HasTag` / `HasTagExact`. False for an empty container. |
| `HasAll(container)`, `HasAllExact(container)` | Whether every tag of the other container passes. True for an empty container. |
| `GetMatchingTags(filter, result)` | Add every stored tag that matches a tag of `filter` to `result`. |
| `Count`, `IsEmpty`, enumeration | Stored tags, not counting `None` entries. |

The container caches resolved handles, so queries don't resolve tags one by one. The cache follows `GameplayTagDatabase.Generation`, so it updates when registries change.

`None` entries can appear in the serialized list: removing a missing tag from a prefab asset sets it to `None`, so prefab overrides keep their list indices. Queries, `Count` and enumeration skip them.

## Generated constants

`Assets/GameplayTags/Generated/GameplayTags.g.cs` holds one `GameplayTag` constant per tag, in a nested class per registry:

```csharp
ownedTags.AddTag(Tags.Main.Status_Debuff_Stun);
```

A constant stores the tag's IDs, so it keeps working after a rename. A renamed tag keeps its old constant as an `[Obsolete]` alias until *Prune Unused Code Aliases* removes it. A deleted tag loses its constant, so code that uses it stops compiling. The file is in `Assembly-CSharp`: code in an assembly with its own `.asmdef` can't reference it.

## GameplayTagDatabase

The baked lookup data of every registry. In players it's loaded at startup from the build; in the editor it follows the registries as you edit them.

| Member | Description |
|---|---|
| `GameplayTagDatabase.Active` | The database tags resolve against. |
| `GameplayTagDatabase.Generation` | Changes whenever `Active` changes. 0 means no database is ready yet. |
| `GameplayTagDatabase.ActiveChanged` | Raised after `Active` changed. |
| `Resolve(registry, id)`, `FindByPath(registry, path)` | Look a tag up by ID, or by path for debugging. |
| `GetPath`, `GetSegment`, `GetParent`, `GetDepth`, `IsImplicit`, `GetChildren` | Data of a live tag, by `TagHandle`. |
| `GetTag(handle)` | The `GameplayTag` of a handle. |
| `IsSameOrDescendantOf(handle, ancestor)` | Hierarchical match between two handles. |
| `MainRegistryId`, `HasRegistry`, `GetRegistryName` | Registry data. |

A `TagHandle` is valid for the database it came from only. Store `GameplayTag`s, not handles.

## Tags added after a build

A player only knows the tags that existed when it was built. A reference to a tag added later resolves as missing in that player, and behaves as `None`. Renaming tags is safe, because references hold IDs.
