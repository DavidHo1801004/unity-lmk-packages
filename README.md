# LMK Ability System

A gameplay ability system for Unity, modelled on Unreal Engine 5's Gameplay Ability System and built on `com.lmk.gameplaytags` and the registry core `com.lmk.registry`. Single-player; the core is plain C# that runs without a scene.

## Features

- **Gameplay attributes** defined as data in attribute registries (`*.attrregistry`), with defaults, clamp bounds (constants or other attributes), meta and integer flags. Edit them in *Tools → LMK → Ability System → Attribute Manager*, reference them with `GameplayAttribute` fields, and optionally use generated `Attributes` constants.
- **Units and the world:** `AbilitySystemWorld` steps every unit's `GameplayAbilitySystem` on its own clock and seeded random source. State changes only through a unit's methods. Values are evaluated as in UE 5.4: base, the six modifier ops, evaluation channels and stacks, clamped to the registry bounds.
- **Gameplay effects:** instant, duration, infinite and periodic effects; scalable, SetByCaller, attribute-based and custom magnitudes; execution calculations; stacking; tag requirements and inhibition; immunity, removal of other effects, chance to apply, additional effects and granted abilities.
- **Gameplay abilities:** tags, cost, cooldown, instancing policies and triggers, with custom logic authored as an inline-instanced `AbilityLogic` that is cloned per grant or activation. Logic acts through an `AbilityContext` with tasks and `await` helpers; `SequenceLogic` builds simple abilities without code.
- **Tags on units:** loose, granted and ability-owned tags, reference-counted with their ancestors, with tag events and gameplay events.
- **Gameplay cues:** cue events queued during a step and delivered at its end, routed by tag with parent fallback to `GameplayCueNotifyStatic` assets and pooled `GameplayCueNotifyActor` prefabs.
- **Scene layer:** `AbilitySystemComponent` hosts a unit on a GameObject, and `AbilitySystemWorldRunner` steps the scene's world in `Update` or `FixedUpdate`. `PlayAnimationAndWait` plays an animator state from an ability.
- **Tools:** a runtime debugger (*Tools → LMK → Ability System → Debugger*), asset validation from the menu and before every build, and project checks for CI.

## Documentation

- [Scripting guide](Documentation~/Scripting.md)
- [Attribute registry format](Documentation~/AttributeRegistryFormat.md)
- [Project checks](Documentation~/ProjectChecks.md)
- The **Walkthroughs** sample (*Package Manager → LMK Ability System → Samples*): six walkthroughs in a scene (setup, fireball, death, stun, equipment and cleanse), with a check of every number.

## Requirements

- Unity 6000.3 or newer.
- `com.lmk.gameplaytags` 1.1.0, `com.lmk.registry` 1.1.0, `com.lmk.attributes` 0.2.0 and `com.lmk.editor` 0.3.0 (dependencies).

## Installation

- **OpenUPM (recommended):** dependencies are installed automatically.
  - With [openupm-cli](https://openupm.com/docs/getting-started-cli.html): `openupm add com.lmk.abilitysystem`
  - Or add to `Packages/manifest.json`:
    ```json
    "scopedRegistries": [
      { "name": "OpenUPM", "url": "https://package.openupm.com", "scopes": [ "com.lmk" ] }
    ],
    "dependencies": {
      "com.lmk.abilitysystem": "0.1.0"
    }
    ```
- **Git URL:** add the five packages in *Package Manager → + → Add package from git URL*, in this order, since Git URLs don't resolve dependencies:
  1. `https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.editor/0.3.0`
  2. `https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.attributes/0.2.0`
  3. `https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.registry/1.1.0`
  4. `https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.gameplaytags/1.1.0`
  5. `https://github.com/DavidHo1801004/unity-lmk-packages.git#com.lmk.abilitysystem/0.1.0`
- **Embedded:** copy the `com.lmk.abilitysystem` folder into your project's `Packages/` folder, and install its dependencies.

The package is published on branch `upm/com.lmk.abilitysystem`, with one tag per version (`com.lmk.abilitysystem/<version>`).

## Assemblies and namespaces

| Assembly | Namespace |
|---|---|
| `LMK.AbilitySystem.Runtime` | `LMK.Runtime.AbilitySystem` |
| `LMK.AbilitySystem.Editor` | `LMK.Editor.AbilitySystem` |
| `LMK.AbilitySystem.Tests` | `LMK.Editor.AbilitySystem.Tests` |
| `LMK.AbilitySystem.Samples.Walkthroughs` (sample) | `LMK.Samples.AbilitySystem.Walkthroughs` |

## License

[MIT](LICENSE.md)
