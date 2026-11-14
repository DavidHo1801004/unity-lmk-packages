# Walkthroughs sample

Six walkthroughs (setup, fireball, death, stun, equipment and cleanse) in one scene: a mage and a target dummy, each with an `AbilitySystemComponent`, in a world run by an `AbilitySystemWorldRunner`.

Open `Walkthroughs.unity`, enter Play Mode and use the buttons in the Game view. Each walkthrough spawns a fresh mage and dummy, plays its steps in world time, and checks every number it expects (✓ or ✗ in the log). Watch the units in *Tools → LMK → Ability System → Debugger*.

| Walkthrough | Shows |
|---|---|
| 11.1 Setting up a unit | `AS_Character`: registry defaults and clamps, `TransferMetaAttribute`, `SendEventOnThreshold`; `GE_ManaRegen` as a startup effect. |
| 11.2 Fireball | `GA_Fireball`: targeting, the cast, commit (cost and cooldown), a projectile carrying the specs, `DamageExecution`, the stacking `GE_Burn` and its cues. |
| 11.3 Death | `GA_Death` triggered by `Event.Death`; `GE_Dead` removes every status and grants immunity to damage. |
| 11.4 Stun interrupts a cast | `GA_StunReaction`, triggered while `Status.Stun` is present, cancels the cast and blocks abilities. |
| 11.5 Equipment | `GE_Equip_BerserkerAxe`: a live attribute-based modifier, a granted passive, and inhibition by `Status.Disarmed`. |
| 11.6 Cleanse and immunity | `GE_Cleanse` removes and blocks every effect that grants `Status.Debuff`. |

## Contents

- `Registries/`: the sample's tags and attributes, as read-only registries of kind `package`, so the sample doesn't touch your main registries. `Scripts/WalkthroughIds.cs` names their rows, as generated constants would.
- `Attributes/`, `Effects/`, `Abilities/`: the walkthroughs' assets.
- `Cues/`: `GC_Hit_Fire` (a `GameplayCueNotifyBurst`), `GC_Status_Burning` (a pooled `GameplayCueNotifyActor` prefab) and the `WalkthroughCues` set the runner uses.
- `Scripts/`: `FireballLogic`, `NearestEnemyTargeting`, `DeathLogic`, `HoldLogic`, `DamageExecution`, `Projectile` and the `WalkthroughDirector` that runs the walkthroughs.

The prefabs use Unity's primitive meshes and default material, so they render in the built-in and the Universal render pipelines.
