# Scripting guide

The runtime API is in the `LMK.Runtime.AbilitySystem` namespace. The core is plain C#: a world steps units, and a unit changes only through its methods. The scene layer (`AbilitySystemWorldRunner`, `AbilitySystemComponent`) is a thin host around it. The `Walkthroughs` sample builds everything below in a scene.

## A unit in a scene

1. Define attributes in *Tools → LMK → Ability System → Attribute Manager*, and tags in the Gameplay Tag Editor.
2. Create an `AttributeSetDefinition` (*Create → LMK → Ability System*) listing the attributes a unit has, with optional initial values and `AttributeBehaviour`s.
3. Add an `AbilitySystemComponent` to the object, with its default sets, startup effects and default abilities.

The component creates its unit (`System`, a `GameplayAbilitySystem`) in `Awake`, in the world of the scene's `AbilitySystemWorldRunner` (one is created if the scene has none), and sets it up in `Start`. The runner steps the world every frame: in `Update`, or in `FixedUpdate` with *Step In Fixed Update* on its `AbilitySystemSettings`.

```csharp
public sealed class PlayerHud : MonoBehaviour
{
    [SerializeField] private AbilitySystemComponent unit;
    [SerializeField] private GameplayAttribute health;      // picked in the inspector

    private void Start() => unit.System.OnAttributeChanged += change =>
    {
        if (change.Attribute == health) Debug.Log($"Health {change.OldValue} → {change.NewValue}");
    };

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) unit.System.AbilityInputPressed(1);
    }
}
```

With generated constants (*Tools → LMK → Ability System → Generate Attribute Code*), `Attributes.Main.Vitals_Health` names the same attribute in code.

## Without a scene

Tests and tools run a world directly. Time moves only when you advance it.

```csharp
using var world = new AbilitySystemWorld(settings, new ManualClock(), new SeededRandom(42));
var unit = world.CreateSystem("Dummy");
unit.AddAttributeSet(characterSet);
unit.ApplyEffectToSelf(burn);
world.Advance(3);           // three seconds, in steps of at most MaxStep
```

## Attributes

| Member | Description |
|---|---|
| `GetBase(a)`, `GetCurrent(a)`, `HasAttribute(a)`, `GetAttributes()` | Read values. A missing attribute reads 0. |
| `AddAttributeSet(set, level)`, `RemoveAttributeSet(set)` | Give or take a set's attributes. |
| `SetAttributeBase(a, value)` | Set a base value directly, clamped to the registry bounds. |
| `OnAttributeChanged`, `OnAttributesInitialized` | Change events, raised at once. Never for meta attributes. |

Values follow UE 5.4's aggregator: base, then the six `ModifierOp`s by evaluation channel, clamped to the registry bounds. `AttributeBehaviour`s react to changes: `TransferMetaAttribute` moves damage from a meta attribute to health, `SendEventOnThreshold` sends a gameplay event when a value crosses a threshold.

## Effects

```csharp
var spec = caster.MakeOutgoingSpec(fireballDamage, level: 2);    // captures the caster's tags and snapshot attributes
spec.SetByCaller(dataDamageTag, 20);
var handle = caster.ApplySpecToTarget(spec, target);              // Invalid for instant effects and refusals
target.RemoveActiveEffect(handle);
```

`GameplayEffect` assets hold the duration policy, period, modifiers (scalable, SetByCaller, attribute-based or custom magnitudes), executions, stacking, components and cues. Components cover tags (`AssetTagsComponent`, `TargetTagsComponent`), requirements and inhibition (`TargetTagRequirementsComponent`), `ImmunityComponent`, `RemoveOtherComponent`, `AdditionalEffectsComponent`, `ChanceToApplyComponent`, granted abilities (`AbilitiesComponent`) and `BlockAbilityTagsComponent`. Query active effects with `GetActiveEffects(GameplayEffectQuery)` and remove them with `RemoveActiveEffects(query)`. An `EffectExecutionCalculation` writes output modifiers from captured attributes:

```csharp
[Serializable]
public sealed class DamageExecution : EffectExecutionCalculation
{
    private static readonly AttributeCaptureDefinition Armor = new(Attributes.Main.Combat_Armor, CaptureSource.Target, false);

    public DamageExecution() => relevantAttributes.Add(Armor);

    public override void Execute(in ExecutionParams p, ExecutionOutput output)
    {
        p.TryGetCapturedMagnitude(Armor, out float armor);
        p.Spec.TryGetSetByCaller(dataDamageTag, out float damage);
        output.AddOutputModifier(Attributes.Main.Meta_IncomingDamage, ModifierOp.AddBase, damage * 100f / (100f + armor));
    }
}
```

## Abilities

A `GameplayAbility` asset holds the tags, cost and cooldown effects, instancing policy and triggers, and an inline-instanced `AbilityLogic`. The logic is cloned for each grant or activation, so its fields are per-instance state; the asset is never written.

```csharp
[Serializable]
public sealed class DashLogic : AbilityLogic
{
    public float duration = 0.3f;
    public GameplayEffect dashEffect;

    public override async void Activate(AbilityContext ctx, GameplayEventData? trigger)
    {
        if (!ctx.Commit()) { ctx.Cancel(); return; }

        ctx.ApplyToOwner(ctx.MakeOutgoingSpec(dashEffect));
        ctx.AddCue(dashCueTag);                 // removed when the activation ends
        await ctx.Delay(duration);              // world time; never resumes once the activation is over
        ctx.End();
    }
}
```

| Member | Description |
|---|---|
| `GiveAbility(ability, level, inputId)`, `ClearAbility(handle)` | Grant and remove. |
| `TryActivateAbility(handle)`, `TryActivateAbilitiesByTag(tags)`, `AbilityInputPressed(id)` | Activate. `OnAbilityFailed` says why an activation was refused. |
| `CancelAbility(handle)`, `CancelAbilities(withTags, withoutTags)` | Cancel running activations. |
| `FindAbility(asset)`, `GetActivatableAbilities()`, `GetCooldownRemaining(asset)` | Query. |

Context helpers start tasks owned by the activation: `Delay`, `WaitEvent`, `WaitTagAdded`, `WaitTagRemoved`, `WaitAttributeChange`, `WaitEffectRemoved`, `WaitInputPress`, `WaitInputRelease`, `WaitTargetData` and `PlayAnimation`. Each can be awaited, or observed through its `Completed` event. `SequenceLogic` builds simple abilities from steps without code.

## Tags and events

`unit.Tags` counts loose tags (`AddLooseTag`, `RemoveLooseTag`), tags granted by effects and tags owned by running abilities, with their ancestors. Use `HasTag`, `HasAll`, `HasAny`, `GetTagCount` and `RegisterTagEvent`. `HandleGameplayEvent(tag, data)` triggers abilities and reaches listeners added with `RegisterGameplayEventListener`.

## Cues

Cues are presentation only. Effects raise them from their cue entries: `Executed` for instant effects and periods, `OnActive` and `WhileActive` while an effect contributes, `Removed` when it stops. Code raises them with `ExecuteCue`, `AddCue` and `RemoveCue` on a unit or an `AbilityContext`. Events are queued and delivered at the end of the world step; an add and a remove of the same cue in one step cancel out.

The runner's `GameplayCueManager` routes each event to the notifies of its `GameplayCueSet`s: those of the cue's tag, then of each parent tag, stopping after a notify marked *Is Override*. `GameplayCueNotifyStatic` assets (such as the built-in `GameplayCueNotifyBurst`) handle one-shot feedback; `GameplayCueNotifyActor` prefabs are pooled, one instance per unit from `OnActive` to `Removed`. A world without a runner drops cues unless you set `world.Cues` to your own `ICueDispatcher`.

## Rules

- Change state only through a unit's methods; never write an asset at runtime.
- Use world time (`world.Time`, `ctx.Delay`) and the world's random source in gameplay code, not `Time` or `UnityEngine.Random`.
- Notifies never change gameplay state.
- Check assets with *Tools → LMK → Ability System → Validate Effects and Abilities*. Player builds run the same check and stop on errors.
