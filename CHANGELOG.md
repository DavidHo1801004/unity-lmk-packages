# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package follows [Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-11-19

### Removed
- **Breaking:** `AttributeSetDefinition` no longer holds initial values: `Entry` keeps only `attribute`, and `Add(attribute, float?)` and the `Entry(attribute, bool, ScalableFloat)` constructor are removed. Starting values move to the new `AttributeInitialValues` asset. Existing sets still load, but any overridden initial values are dropped: move them into an Initial Values asset and assign it to the unit's `AbilitySystemComponent`. The Walkthroughs sample has `IV_Character` and `IV_TargetDummy` for this.

### Added
- `AttributeInitialValues` asset (*Create → LMK → Ability System → Attribute Initial Values*): the starting value of each listed attribute, level-scaled with an optional curve, so one attribute set can serve units that start differently. It's assigned on `AbilitySystemComponent.InitialValues`, or passed to `GameplayAbilitySystem.AddAttributeSet(set, level, initialValues)` and `AttributeContainer.AddSet`.
- Inspectors for Attribute Set, Attribute Initial Values, Gameplay Effect and Gameplay Ability, in the LMK style with collapsible sections that remember their state per user and project. Lists are LMK property lists: a header with the element count and an add button, rows with a drag handle and a remove button, and Undo. With several objects selected, lists of the same size can be edited; lists of different sizes are read-only.
  - Attribute Set: *Attributes* and *Behaviours*. Add opens a searchable picker of the registered attributes not in the list yet, so a list never holds an empty or duplicate attribute. Once every attribute is listed, Add is disabled and its tooltip says why.
  - Attribute Initial Values: the same picker, with each attribute's value and level curve on one row. A new value starts at the attribute's registry default.
  - Gameplay Effect: *Duration* (with the period), *Modifiers*, *Executions*, *Components*, *Stacking* and *Cues*. The duration shows only for *Has Duration*, and the period and stacking are hidden for *Instant* effects.
  - Gameplay Ability: *Tags*, *Cost & Cooldown*, *Policies* and *Logic*.
- Icons for every asset type: Attribute Set, Attribute Initial Values, attribute registries, Gameplay Effect, Gameplay Ability, Cue Set, cue notifies and Ability System Settings. They are flat glyphs on a transparent background in two colours, white and their category's tint (attributes green, effects violet, abilities orange, cues cyan, settings grey), with gaps between parts.
- Project validation warns about attributes listed twice in an attribute set or an Initial Values asset.

### Fixed
- The Inspector header of an attribute registry shows its icon, not the default ScriptableObject icon.
- Attribute Manager: a branch expands and collapses from its arrow only. Clicking the edge of its row, or the space around its name, toggled it.

### Changed
- Requires `com.lmk.editor` 0.5.0 and `com.lmk.registry` 1.1.2 (for `RegistryImporter.IconPath`, used by the attribute registry icon). Attribute fields use the editor library's `AlignedField`, and the Attribute Manager tree its `FoldoutArrowManipulator`.
- Attribute pickers (attribute fields, bounds in the Attribute Manager, registry pickers) use the LMK searchable dropdown, with search syntax and one folder per group.
- `GameplayAbility` no longer has `[Header]`s; its inspector sections replace them.
- The Attribute Manager now has the Gameplay Tag Manager's layout and style: a toolbar (Save, Discard, search, Add Attribute, options), a scrollable tree, and a resizable, collapsible Inspector panel. The tree shows every registry as a branch with a *main* or *package, read-only* badge, then one level of groups (the name before its last segment, e.g. `Stats.Core` for `Stats.Core.Speed`), then the attributes, each with a summary of its default, bounds and flags. Search covers every registry; `#text` searches descriptions.
- The Inspector shows the attribute's last name segment as an editable title, the registry it comes from, then the rows Group, Default, Min, Max, On Max Change, Meta, Integer, Description and Bound Of. Changing the Group moves the attribute to another group; a group set there is letters and digits only (a-z, A-Z, 0-9), and other characters can't be typed or pasted.
- Context menus are grouped by what they do: add, edit, references, copy, then delete.
- The Add Attribute popup is styled like the Add Tag popup and opens at the tree's top-right corner, starting with the group when opened from a group or a sibling. Enter adds the attribute. A name is `Group.Name` or `Name`: the group is letters and digits only, the name letters, digits and `_`. Keys that would break that are blocked, and other problems, such as a name already in use, show under the field as you type.

### Added
- *Add Sibling Attribute* and *Rename Attribute* (in place, on the row) in an attribute's context menu, *Add Attribute To Group* and *Copy Group Name* on a group, and *Select Registry Asset* on a registry.
- A Find References icon at the end of the Inspector's title row.
- *Bound Of* row: the attributes that use this one as their min or max, as links that select them.
- Options menu (gear): Collapse All, Expand All, Generate Code, Prune Unused Code Aliases and Relink Missing References.
- Registry problems (invalid or hand-edited files, inactive or outdated package registries, a main registry changed on disk under unsaved edits) show under the tree.

### Removed
- The registry dropdown in the toolbar: every registry is in the tree.
- The Delete and Find References buttons and the attribute ID at the bottom of the details. Delete and Copy ID are in the context menu; Find References is an icon in the title row.

## [0.1.1] - 2026-11-14

### Changed
- Depends on `com.lmk.registry` 1.1.1 and `com.lmk.gameplaytags` 1.2.0. Registry 1.1.1 removed the marker API that 0.1.0 used, so 0.1.0 only works with registry 1.1.0.

### Removed
- Markers in the Attribute Manager (the colour swatch on rows and the marker field in the details). Markers are now a gameplay tags feature only.

## [0.1.0] - 2026-11-14

First release. A single-player gameplay ability system modelled on Unreal Engine 5.4's, on `com.lmk.gameplaytags` 1.1.0 and `com.lmk.registry` 1.1.0.

### Added

- **Gameplay attributes as a registry kind:** `*.attrregistry` files with a name, default, min and max bounds (none, a constant, or another attribute), an `onMaxChange` policy (`clamp`, `keepRatio`), `meta` and `integer` flags, and a description per attribute. The main registry lives in `Assets/GameplayAttributes/Main.attrregistry`. Registry rules (valid and unique names, finite values, existing bounds, no clamp cycle, constant ranges, meta attributes without bounds) refuse an invalid edit, save or file.
- The Attribute Manager (*Tools → LMK → Ability System → Attribute Manager*): staged Add, Rename, Edit and Delete with Undo, locking, and Save or Discard. Deleting an attribute lists the attributes it bounds and the assets that reference it. The `GameplayAttribute` drawer and picker, and the attribute registry Inspector.
- `GameplayAttribute`: a serializable reference by (registry ID, attribute ID) that survives renames. `GameplayAttributeDatabase`: the baked attribute data, with bounds resolved across registries and a deterministic initialisation order.
- Generated constants (`Attributes.Main.Vitals_Health`) in `GameplayAttributes.g.cs`, with `[Obsolete]` aliases for renamed attributes.
- Pack validation: a package attribute may not be bounded by the project's main registry, or by a registry the package doesn't depend on.
- **The world and units:** `AbilitySystemWorld` steps every unit's `GameplayAbilitySystem` in registration order, in steps of at most `MaxStep`, on its own clock (`ScaledClock`, `ManualClock`) and seeded random source (`SeededRandom`). State changes only through a unit's methods, which flush attribute propagation before they return. Limits live in an `AbilitySystemSettings` asset.
- **Attribute runtime:** `AttributeSetDefinition` assets give units their attributes, with initial values scaled by level. Values are evaluated as in UE 5.4 (the six modifier ops, evaluation channels, first-applied `Override`, stacks), kept inside the registry bounds, and propagated to the attributes they bound. Meta attributes reset after each execution. `AttributeBehaviour` hooks (`PreAttributeChange`, `PreBaseChange`, `PreEffectExecute`, `PostEffectExecute`, `PostAttributeChange`), with the built-in `TransferMetaAttribute` and `SendEventOnThreshold`.
- **Gameplay effects:** `GameplayEffect` assets applied through `GameplayEffectSpec`s: instant, duration, infinite and periodic effects; modifiers with tag requirements; scalable, SetByCaller, attribute-based (snapshot or live) and custom (`ModMagnitudeCalculation`) magnitudes; executions (`EffectExecutionCalculation`) with scoped modifiers and conditional effects. Periods are counted from the start, so hitches catch up exactly.
- Stacking by source or by target, with limits, refresh, reset and expiration policies, and overflow effects. Inhibition through ongoing tag requirements.
- Effect components: asset and granted tags, target tag requirements, immunity, removal of other effects, chance to apply, custom application requirements, additional effects, granted abilities (`AbilitiesComponent`) and blocked ability tags.
- `ActiveGameplayEffect` and `ActiveEffectHandle`, `GameplayEffectQuery`, and events for application, removal, stack changes, expiry, inhibition and immunity.
- **Tags on units:** loose tags, tags granted by effects and tags owned by abilities, counted with their ancestors, with tag events. Gameplay events with listeners.
- **Gameplay abilities:** `GameplayAbility` assets with UE 5.4's tags, cost and cooldown effects, `InstancedPerActor` and `InstancedPerExecution` policies, retrigger, and triggers (`GameplayEvent`, `OwnedTagAdded`, `OwnedTagPresent`). Custom logic is an inline-instanced `AbilityLogic`, cloned per grant or activation so assets are never written. Logic acts through an `AbilityContext`: commit (all or nothing), end, cancel, effects, cues, tasks and `await` helpers.
- Ability tasks: `WaitDelay`, `WaitGameplayEvent`, `WaitTagAdded`, `WaitTagRemoved`, `WaitAttributeChange`, `WaitEffectRemoved`, `WaitEffectStackChange`, `WaitInput`, `WaitTargetData` with `TargetingStrategy`, and `PlayAnimationAndWait`. A continuation from an activation that is over never runs.
- `SequenceLogic`: abilities assembled from steps (commit, wait, apply effects, send events) without code.
- **Gameplay cues:** `Executed`, `OnActive`, `WhileActive` and `Removed` events raised by effects, abilities and units, queued during a step and delivered at its end. `GameplayCueManager` routes them by tag with parent fallback to `GameplayCueNotifyStatic` assets (`GameplayCueNotifyBurst` built in) and pooled `GameplayCueNotifyActor` prefabs, listed in `GameplayCueSet`s.
- **Scene layer:** `AbilitySystemComponent` hosts a unit on a GameObject, with an avatar, attribute sets, startup effects and default abilities. `AbilitySystemWorldRunner` creates the scene's world and cue manager and steps it in `Update` or `FixedUpdate`.
- **Tools:** a runtime debugger (*Tools → LMK → Ability System → Debugger*) showing each unit's attributes, tags, effects, abilities and cues in Play Mode. `EffectValidator` and `AbilityValidator`, run from *Tools → LMK → Ability System → Validate Effects and Abilities* and before every build, which stops on errors. `AbilitySystemProjectChecks` for tests and CI.
- The **Walkthroughs** sample: six walkthroughs (setup, fireball, death, stun, equipment, cleanse) in a scene, with its own tag and attribute registries.
- Documentation: scripting guide, attribute registry format and project checks.
- Edit mode tests.
