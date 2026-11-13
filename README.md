# LMK Gameplay Tags

Hierarchical gameplay tags for Unity, such as `Status.Debuff.Stun`. Tags live in a registry file and are picked in the Inspector through a searchable tag tree. A reference stores the tag's ID, so renaming a tag never changes the assets that use it.

## Scope

**Included:**
- `GameplayTag` and `GameplayTagContainer`: a single tag and a set of tags, with Inspector drawers.
- `GameplayTagQuery`: serializable tag queries (any, all or none of tags, nested).
- A main tag registry (`Assets/GameplayTags/Main.tagregistry`), edited in the Gameplay Tag Editor window (*Tools → GASU → Gameplay Tag Editor*): add, rename, delete and describe tags, with Undo. Nothing is written until you save.
- Personal colour markers on tags, which are never committed.
- Read-only registries shipped inside packages, generated registries for packages without one, and checks before you pack your own.
- Mapping of tags in assets imported from another project.
- Tag files (`*.tags`): plain text lists of tags to import into the main registry, or exported from any registry.
- Generated C# constants for every tag (`Tags.Main.Status_Debuff_Stun`).
- A runtime tag database, baked into player builds automatically.

**Not included:** gameplay abilities and effects.

Since 1.1.0, tags are a registry kind of the registry core, `com.lmk.registry`, which also serves other kinds such as gameplay attributes. Saving, discarding, locking, reference tools, package registries, builds and code generation are shared by every kind.

## Tag registries

Open *Tools → GASU → Gameplay Tag Editor* and create the main registry. Then add tags such as `Status.Debuff.Stun`. Parents you didn't add yourself (`Status`, `Status.Debuff`) exist *implicitly*, and are shown in italics. Each segment may contain letters, digits and `_`.

Every tag has a permanent ID:

- Renaming a tag changes only the registry file. Every reference follows.
- Deleting a tag leaves references in place, shown as `<name> (deleted)`. Clicking such a field offers one action: remove the missing tags. The Delete dialog lists the saved assets that reference the tag, and you can still delete it.
- Adding a deleted name again creates a new tag. *Relink Missing References* can point the old references to it (see [Finding and rewriting references](#finding-and-rewriting-references)).

Only the editor writes the registry file. A hand edit is detected through its checksum and reported in the Tag Editor.

### The Gameplay Tag Editor

- **Toolbar:** *Save*, *Discard*, the lock provider when one is set, a search field (`#` terms search descriptions), *Add Tag*, and the options menu (gear). *Add Tag* opens a field under the button. Type a path such as `Status.Debuff.Stun` and press Enter, or press Esc to close it.
- **Tag tree:** shows every loaded registry. Only the main registry's tags can be edited. Click a tag to select it.
  - Right-click a tag to add a sub tag, make it explicit, rename, delete or copy it, or to find or replace its references.
  - Right-click a registry header to add a tag, import tag files or export the registry.
- **Inspector panel:** shows the selected tag's name (double-click it or click the pencil to rename), its registry (click it to select the file in the Project window), and these fields:
  - *Is Explicit*: Yes or No, with a *Make Explicit* button for implicit tags.
  - *Parent*: click it to select the parent tag.
  - *Description*: applied when the field loses focus.
  - *Marker*: a colour shown as a strip at the end of the tag's row. It's empty by default, and *Clear* removes it.

  Tags of package and generated registries are read-only: you can only set their marker. You can rearrange the panel, and the layout is remembered per user:
  - drag its header to move it to the other side of the tree
  - drag its edge to resize it
  - collapse it

**Every edit is staged until you save.** The tree, Inspector fields and tag pickers show staged edits right away, and Undo and Redo work on them, but nothing is written to disk.
- *Save* writes the main registry and your markers, then the generated code. It saves the unsaved edits of every registry kind at once.
- *Discard* drops every unsaved edit.
- While there are unsaved edits, the window title shows `*`. Unity asks you to save them when you close the window or quit.

Some limits of staged edits:
- If the main registry changes on disk while you have unsaved edits (a pull, a revert), *Save* is refused, so it never overwrites the newer file. Discard your edits to load it. Without unsaved edits, the editor simply follows the file.
- Builds and generated code use only the saved registries. A build with unsaved edits logs a warning.
- Unsaved edits survive script recompiles, but not an editor restart.

Markers are personal. They're saved in `UserSettings/LMKGameplayTags/Markers.json`, which isn't committed.

### Finding and rewriting references

A reference index records which saved scenes, prefabs and assets use which tags. It is the registry core's index, in `Library/LMKRegistry/RefIndex`, and is never committed. Saving an asset updates it right away. Assets changed outside the editor (a pull, a revert) are scanned the next time you need the index, with a progress bar. After `Library/` is deleted, that first use scans the whole project once. *Rebuild Reference Index* (options menu, or *Tools → LMK → Registries*) forces a full scan.

The index covers saved data only: unsaved scene and asset changes aren't in it. A value that a prefab instance inherits is listed for the prefab, not for the instance.

- **Find References** (right-click a tag) lists the assets that use it. Select one to ping it.
- **Relink Missing References** (options menu, or *Tools → GASU → Gameplay Tags*) finds references shown as `(deleted)` whose last known name is a live tag: in their own registry first, then in the main registry. It previews each one. Only the ones you leave selected are rewritten.
- **Replace References** (right-click a tag) rewrites every reference to that tag to another tag you choose, after the same preview. The first tag isn't deleted. Delete it yourself if you no longer need it.

Relink and Replace change assets, not registries. They save the assets and closed scenes they change. Scenes that are open are changed with Undo and left for you to save.

## Tag files

A tag file (`*.tags`) lists tags as plain text, one per line, with an optional description after `;`:

```
# Combat.tags
Ability.Fire.Projectile ; Fired projectile abilities
Status.Burning          ; Damage over time from fire
Status.Stun
```

Tag files hold no IDs. Anyone can edit them, and nothing changes until they are imported:

- **Inspect:** select a `.tags` file in the Project window. The Inspector shows its tags as a tree, each marked *new*, *exists*, *made explicit* or *description differs* against the main registry, and lists any invalid lines. It reads the file directly, so no copy of its tags is stored.
- **Import:** *Tools → GASU → Gameplay Tags → Import Tag Files...*, or select `.tags` files in the Project window and choose *Gameplay Tags → Import Into Main Registry...*. The window previews the changes before you import. New paths are added with their parents. A path that exists only as an implicit parent becomes explicit and keeps its ID. The import is one Undo step, and is staged like any other edit, so save it in the Tag Editor.
  - *Overwrite descriptions* replaces the descriptions of existing tags. A line without `;` never changes a description.
  - *Remove tags not in the file(s)* deletes the main registry's other tags, after a dialog that lists them.
- **Export:** *Tools → GASU → Gameplay Tags → Export Main Registry To Tag File...* exports the main registry. To export any registry (main, package or generated), right-click its header in the Tag Editor, or select `.tagregistry` files in the Project window and choose *Gameplay Tags → Export To Tag File...*. Several selected registries are exported to one folder. Export never changes a registry.

Tags are matched by path. Renaming a line in a tag file and importing it creates a new tag, so rename tags in the Tag Editor, where references follow.

## Packages

A package can ship its own registry (kind `package`) anywhere inside it. It loads while the package is installed, and is read-only. Uninstalling the package makes its references show as `(deleted)`; reinstalling it brings them back. Neither writes anything to your project. Packages should put their tags under their own top-level name, such as `VendorCombat.*`.

**Packages without a registry.** A package whose assets reference tags from its author's project shows them as `(deleted)`. Select its folder in the Project window and choose *LMK Registries → Generate Registries For Package...*. This writes a registry of kind `generated` to `Assets/GameplayTags/Packages/<package>.tagregistry`, with the registry ID and tag IDs the package's assets use, so they resolve without changing the package. Tags listed in the package's tag files are added too. Commit the file. It loads only while the package is installed.

When the package is updated, the Tag Editor offers to regenerate the registry: right-click its header and choose *Regenerate From Package...*. Nothing regenerates on its own. Regenerating keeps existing tags, unless you choose *Regenerate And Remove Unused*.

**Packages you author.** Select your package's folder (embedded or local) and choose *LMK Registries → Validate Package* or *Pack Package...*. Packing is refused unless, for its tags:
- the package contains exactly one registry of kind `package`, with a valid checksum;
- its assets reference only that registry, or the registry of a package listed in its `package.json` dependencies, never your project's main registry;
- every referenced tag exists;
- its registry ID is the one it was last packed with. *Pack Package...* records it in the project settings.

## Assets from other projects

Assets from another project reference that project's main registry, which yours doesn't have. When you import a `.unitypackage`, its assets are mapped once the import finishes:
- A reference to a registry loaded here, such as a package's, is kept.
- A reference whose name exists in your main registry is rewritten to that tag.
- A new name is added to your main registry, with the same tag ID if it's free, and the reference is rewritten to it. The added tags are saved right away, unless you have other unsaved tag edits.
- A reference without a name is left as it is, and reported.

Only the imported assets are changed. A log in `Logs/LMKRegistry/` lists every added tag and rewritten reference. Cancelled or failed imports, pulls and copied files are never mapped.

Mapping needs the tag lock. Without it, the assets wait, and the Tag Editor offers *Map Imported Assets* in its options menu. To map assets that came in another way, select them or their folder and choose *LMK Registries → Map References From Another Project...*.

## Usage

Assign tags in the Inspector, or use the generated constants:

```csharp
using LMK.Runtime.GameplayTags;

public class Example : MonoBehaviour
{
    [SerializeField] private GameplayTag debuffTag;
    [SerializeField] private GameplayTagContainer ownedTags = new();

    private void Start()
    {
        ownedTags.AddTag(Tags.Main.Status_Debuff_Stun);
        bool hasDebuff = ownedTags.HasTag(debuffTag);       // true: "Status.Debuff.Stun" is a child of "Status.Debuff"
        bool exact = ownedTags.HasTagExact(debuffTag);      // false
    }
}
```

### Generated constants

`Assets/GameplayTags/Generated/GameplayTags.g.cs` holds one constant per tag, in a nested class per registry: `Tags.Main` for the main registry, and one class per package registry. A constant's name is its path with `.` replaced by `_`. It stores the tag's IDs, so it keeps working after the tag is renamed. Commit the file with the registry.

- The file is written from the saved registries when you save tag edits in the Tag Editor. It's only rewritten when its content changes. *Tools → GASU → Gameplay Tags → Generate Code* writes it immediately.
- **Per registry:** select a `.tagregistry` file to see its details in the Inspector.
  - *Generate C# class* turns that registry's class on or off. It's on by default, and stored in Project Settings, so it works for read-only package registries too.
  - *Open Gameplay Tag Manager* opens the Tag Editor with the registry selected.
- **Renamed tags** keep their old constant name as an `[Obsolete]` alias, so your code still compiles and the warning names the new constant. *Tools → GASU → Gameplay Tags → Prune Unused Code Aliases* removes the aliases that no script uses.
- **Deleted tags** lose their constant, so code that uses one stops compiling.
- When two paths produce the same name (`A.B_C` and `A_B.C`), both constants get the start of their tag ID appended, and a warning is logged.
- The generated code is in the predefined assembly (`Assembly-CSharp`) by default, so assemblies with their own `.asmdef` can't reference it. *Project Settings → LMK → Registries* can give it its own assembly, which other assembly definitions can reference. *Project Settings → LMK → Gameplay Tags → Code Namespace* puts the `Tags` class in a namespace.

`GameplayTag.FromName("Status.Debuff")` looks a tag up by its current path, so it doesn't survive renames: use it only for debugging and console commands.

`GameplayTag` has no `==` operator. `Equals` compares IDs and is safe as a dictionary key. `MatchesTag` and `MatchesTagExact` compare resolved tags, and a tag that doesn't resolve matches nothing. `HasTag`, `HasAny` and `HasAll` include parent tags; the `Exact` variants don't.

Builds bake every registry into `Assets/GameplayTags/Generated/Resources/GameplayTagDatabase.asset`, which stays in Preloaded Assets. Built scenes get None in place of references that don't resolve, and the build reports them. *Project Settings → LMK → Gameplay Tags* (or *LMK → Registries*, for every registry kind) can make such references fail the build instead.

### Tag queries

A `GameplayTagQuery` tests a set of tags: any, all or none of some tags, hierarchical or exact, combined with any, all or none of nested expressions. It's serializable, so assets can hold one. Build one from code with `GameplayTagQueryExpression`:

```csharp
var query = GameplayTagQuery.Build(GameplayTagQueryExpression.All(
    GameplayTagQueryExpression.AnyTags(Tags.Main.Status_Burning, Tags.Main.Status_Frozen),
    GameplayTagQueryExpression.NoTags(Tags.Main.Status_Immune)));

bool matches = query.Matches(ownedTags);    // ALL(ANY(Status.Burning, Status.Frozen), NONE(Status.Immune))
```

An empty query matches nothing: check `IsEmpty` where no query should mean no restriction. A query matches any `IGameplayTagSet`, such as a `GameplayTagContainer`.

## Upgrading from 1.0.0

1.1.0 keeps your data and your code working: registry files, serialized tags (text and binary), the baked database asset, generated constants and their aliases, markers, and the public API.

- `TagGuid` is now `RegistryGuid` (namespace `LMK.Runtime.Registry`), with the same layout and text form. `TagGuid` still compiles, as an obsolete type that converts to and from `RegistryGuid`.
- The 1.0.0 settings (`ProjectSettings/LMKGameplayTagSettings.asset`) are carried over once into `ProjectSettings/LMKRegistrySettings.asset`. Commit the new file. The old one is no longer used.
- The reference index rebuilds once, in its new place in `Library/`.
- Commands shared by every registry kind moved to *Tools → LMK → Registries* and *Assets → LMK Registries*: *Rebuild Reference Index*, *Generate Registries For Package...*, *Validate Package*, *Pack Package...* and *Map References From Another Project...*. The lock provider and the shared build policy are in *Project Settings → LMK → Registries*.
- `GameplayTagDatabase.ActiveDatabaseChanged` passes the previous and the new database. `ActiveChanged` still works.
- Saving in the Tag Editor saves the unsaved edits of every registry kind.

## Project checks

`RegistryProjectChecks` (`TagProjectChecks` in 1.0.0, which still works) checks your project's registries of every kind, tags included, the way a build would: registry integrity, build content and pack validation. Run it as edit mode tests, or from the command line on a CI service. See [Project checks](Documentation~/ProjectChecks.md) for the setup.

## Documentation

- [Scripting reference](Documentation~/Scripting.md): `GameplayTag`, `GameplayTagContainer`, generated constants and the runtime database.
- [Registry file format](Documentation~/RegistryFormat.md): reading registry diffs, and resolving merge conflicts.
- [Project checks](Documentation~/ProjectChecks.md): checking tags in tests and CI.

## Requirements

- Unity 6000.3 or newer.
- `com.lmk.editor` 0.3.0 and `com.lmk.registry` 1.0.0 (dependencies).

## Installation

- **OpenUPM (recommended):** dependencies are installed automatically.
  - With [openupm-cli](https://openupm.com/docs/getting-started-cli.html): `openupm add com.lmk.gameplaytags`
  - Or add to `Packages/manifest.json`:
    ```json
    "scopedRegistries": [
      { "name": "OpenUPM", "url": "https://package.openupm.com", "scopes": [ "com.lmk" ] }
    ],
    "dependencies": {
      "com.lmk.gameplaytags": "1.1.0"
    }
    ```
- **Git URL:** add the three packages in *Package Manager → + → Add package from git URL*, in this order, since Git URLs don't resolve dependencies:
  1. `https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.editor/0.3.0`
  2. `https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.registry/1.0.0`
  3. `https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.gameplaytags/1.1.0`
- **Embedded:** copy the `com.lmk.gameplaytags` folder into your project's `Packages/` folder, and install `com.lmk.editor` and `com.lmk.registry`.

The package is published on branch `upm/com.lmk.gameplaytags`, with one tag per version (`com.lmk.gameplaytags/<version>`).

## Namespaces

- Runtime: `LMK.Runtime.GameplayTags`
- Editor: `LMK.Editor.GameplayTags`

## License

[MIT](LICENSE.md)
