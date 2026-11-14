# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package follows [Semantic Versioning](https://semver.org/).

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
