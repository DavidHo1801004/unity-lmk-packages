using System;
using System.Reflection;

using LMK.Runtime.AbilitySystem;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

using TagRow = LMK.Runtime.GameplayTags.GameplayTagDatabaseBuilder.Row;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// The test tag registry, as references.
    /// </summary>
    internal static class TestTags
    {
        public static readonly RegistryGuid Registry = RegistryGuid.Parse("7a6900000000000000000000000000a1");

        private static RegistryGuid Id(int _N) => new(0x4000_0000_0000_0000UL, (ulong)_N);

        private static GameplayTag Tag(int _N, string _Path) => GameplayTag.FromId(Registry, Id(_N), _Path);

        public static readonly GameplayTag Status = Tag(1, "Status");
        public static readonly GameplayTag Shielded = Tag(2, "Status.Shielded");
        public static readonly GameplayTag Silenced = Tag(3, "Status.Silenced");
        public static readonly GameplayTag Empowered = Tag(4, "Status.Empowered");
        public static readonly GameplayTag Event = Tag(5, "Event");
        public static readonly GameplayTag Damaged = Tag(6, "Event.Damaged");
        public static readonly GameplayTag Death = Tag(7, "Event.Death");
        public static readonly GameplayTag DataDuration = Tag(9, "Data.Duration");
        public static readonly GameplayTag DataDamage = Tag(10, "Data.Damage");
        public static readonly GameplayTag Buff = Tag(12, "Effect.Buff");
        public static readonly GameplayTag Ability = Tag(13, "Ability");
        public static readonly GameplayTag AbilityAttack = Tag(14, "Ability.Attack");
        public static readonly GameplayTag AbilityAttackSlash = Tag(15, "Ability.Attack.Slash");
        public static readonly GameplayTag AbilityMelee = Tag(16, "Ability.Melee");
        public static readonly GameplayTag CooldownTest = Tag(18, "Cooldown.Test");
        public static readonly GameplayTag CooldownShared = Tag(19, "Cooldown.Shared");
        public static readonly GameplayTag Casting = Tag(21, "State.Casting");
        public static readonly GameplayTag Stun = Tag(22, "Status.Stun");
        public static readonly GameplayTag StunFrozen = Tag(23, "Status.Stun.Frozen");
        public static readonly GameplayTag Ping = Tag(24, "Event.Ping");
        public static readonly GameplayTag Cue = Tag(25, "GameplayCue");
        public static readonly GameplayTag CueHit = Tag(26, "GameplayCue.Hit");
        public static readonly GameplayTag CueHitFire = Tag(27, "GameplayCue.Hit.Fire");
        public static readonly GameplayTag CueBurn = Tag(28, "GameplayCue.Burn");

        // The walkthroughs.
        public static readonly GameplayTag Debuff = Tag(29, "Status.Debuff");
        public static readonly GameplayTag Burning = Tag(30, "Status.Burning");
        public static readonly GameplayTag Disarmed = Tag(31, "Status.Disarmed");
        public static readonly GameplayTag Damage = Tag(32, "Damage");
        public static readonly GameplayTag DamageFire = Tag(33, "Damage.Fire");
        public static readonly GameplayTag CooldownFireball = Tag(34, "Cooldown.Fireball");
        public static readonly GameplayTag Dead = Tag(35, "State.Dead");
        public static readonly GameplayTag AbilityFireball = Tag(36, "Ability.Fireball");
        public static readonly GameplayTag AbilityInterruptible = Tag(37, "Ability.Interruptible");
        public static readonly GameplayTag DamageTaken = Tag(39, "Event.Damage.Taken");
        public static readonly GameplayTag CueStatusBurning = Tag(41, "GameplayCue.Status.Burning");

        public static GameplayTagDatabaseBuilder Builder() => new GameplayTagDatabaseBuilder().AddRegistry(Registry, "Test Tags", TagRegistryKind.Main, new[]
        {
            new TagRow(Id(1), RegistryGuid.Zero, "Status", true),
            new TagRow(Id(2), Id(1), "Shielded"),
            new TagRow(Id(3), Id(1), "Silenced"),
            new TagRow(Id(4), Id(1), "Empowered"),
            new TagRow(Id(5), RegistryGuid.Zero, "Event", true),
            new TagRow(Id(6), Id(5), "Damaged"),
            new TagRow(Id(7), Id(5), "Death"),
            new TagRow(Id(8), RegistryGuid.Zero, "Data", true),
            new TagRow(Id(9), Id(8), "Duration"),
            new TagRow(Id(10), Id(8), "Damage"),
            new TagRow(Id(11), RegistryGuid.Zero, "Effect", true),
            new TagRow(Id(12), Id(11), "Buff"),
            new TagRow(Id(13), RegistryGuid.Zero, "Ability"),
            new TagRow(Id(14), Id(13), "Attack"),
            new TagRow(Id(15), Id(14), "Slash"),
            new TagRow(Id(16), Id(13), "Melee"),
            new TagRow(Id(17), RegistryGuid.Zero, "Cooldown", true),
            new TagRow(Id(18), Id(17), "Test"),
            new TagRow(Id(19), Id(17), "Shared"),
            new TagRow(Id(20), RegistryGuid.Zero, "State", true),
            new TagRow(Id(21), Id(20), "Casting"),
            new TagRow(Id(22), Id(1), "Stun"),
            new TagRow(Id(23), Id(22), "Frozen"),
            new TagRow(Id(24), Id(5), "Ping"),
            new TagRow(Id(25), RegistryGuid.Zero, "GameplayCue", true),
            new TagRow(Id(26), Id(25), "Hit"),
            new TagRow(Id(27), Id(26), "Fire"),
            new TagRow(Id(28), Id(25), "Burn"),
            new TagRow(Id(29), Id(1), "Debuff"),
            new TagRow(Id(30), Id(1), "Burning"),
            new TagRow(Id(31), Id(1), "Disarmed"),
            new TagRow(Id(32), RegistryGuid.Zero, "Damage", true),
            new TagRow(Id(33), Id(32), "Fire"),
            new TagRow(Id(34), Id(17), "Fireball"),
            new TagRow(Id(35), Id(20), "Dead"),
            new TagRow(Id(36), Id(13), "Fireball"),
            new TagRow(Id(37), Id(13), "Interruptible"),
            new TagRow(Id(38), Id(5), "Damage"),
            new TagRow(Id(39), Id(38), "Taken"),
            new TagRow(Id(40), Id(25), "Status"),
            new TagRow(Id(41), Id(40), "Burning"),
        });
    }



    /// <summary>
    /// The active gameplay tag database, which the tag package only lets its own code replace: reached by reflection.
    /// </summary>
    internal static class TagDatabaseAccess
    {
        private const BindingFlags STATIC = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly MethodInfo SET_ACTIVE = typeof(GameplayTagDatabase).GetMethod("SetActive", STATIC, null,
            new[] { typeof(GameplayTagDatabase), typeof(bool) }, null);

        private static readonly PropertyInfo ENSURE_ACTIVE = typeof(GameplayTagDatabase).GetProperty("EditorEnsureActive", STATIC);

        public static void SetActive(GameplayTagDatabase _Database) => SET_ACTIVE.Invoke(null, new object[] { _Database, false });

        public static Action EnsureActive
        {
            get => (Action)ENSURE_ACTIVE.GetValue(null);
            set => ENSURE_ACTIVE.SetValue(null, value);
        }
    }



    /// <summary>
    /// Builds an in-memory <see cref="GameplayEffect"/> fluently: <c>e => e.Duration(5).Period(1).Mod(…)</c>.
    /// </summary>
    internal sealed class EffectBuilder
    {
        public GameplayEffect Effect { get; }

        public EffectBuilder(GameplayEffect _Effect)
        {
            Effect = _Effect;
            Effect.ExecutePeriodicEffectOnApplication = false;
        }

        public EffectBuilder Instant()
        {
            Effect.DurationPolicy = DurationPolicy.Instant;
            return this;
        }

        public EffectBuilder Duration(float _Seconds) => Duration(new ScalableFloatMagnitude(_Seconds));

        public EffectBuilder Duration(Magnitude _Seconds)
        {
            Effect.DurationPolicy = DurationPolicy.HasDuration;
            Effect.DurationMagnitude = _Seconds;
            return this;
        }

        public EffectBuilder Infinite()
        {
            Effect.DurationPolicy = DurationPolicy.Infinite;
            return this;
        }

        public EffectBuilder Period(float _Seconds, bool _ExecuteOnApplication = false)
        {
            Effect.Period = _Seconds;
            Effect.ExecutePeriodicEffectOnApplication = _ExecuteOnApplication;
            return this;
        }

        public EffectBuilder Mod(GameplayAttribute _Attribute, ModifierOp _Op, float _Magnitude, int _Channel = 0,
                                 TagRequirements _SourceTags = null, TagRequirements _TargetTags = null)
            => Mod(_Attribute, _Op, new ScalableFloatMagnitude(_Magnitude), _Channel, _SourceTags, _TargetTags);

        public EffectBuilder Mod(GameplayAttribute _Attribute, ModifierOp _Op, Magnitude _Magnitude, int _Channel = 0,
                                 TagRequirements _SourceTags = null, TagRequirements _TargetTags = null)
        {
            Effect.ModifierList.Add(new ModifierInfo(_Attribute, _Op, _Magnitude)
            {
                evaluationChannel = _Channel,
                sourceTags = _SourceTags ?? new TagRequirements(),
                targetTags = _TargetTags ?? new TagRequirements(),
            });
            return this;
        }

        public EffectBuilder GrantTags(params GameplayTag[] _Tags) => Add(new TargetTagsComponent { tags = new GameplayTagContainer(_Tags) });

        public EffectBuilder AssetTags(params GameplayTag[] _Tags) => Add(new AssetTagsComponent { tags = new GameplayTagContainer(_Tags) });

        public EffectBuilder Add(GameplayEffectComponent _Component)
        {
            Effect.ComponentList.Add(_Component);
            return this;
        }

        public EffectBuilder Stack(StackingType _Type, int _Limit = 0,
                                   StackExpirationPolicy _Expiration = StackExpirationPolicy.ClearEntireStack,
                                   StackDurationRefreshPolicy _Refresh = StackDurationRefreshPolicy.RefreshOnSuccessfulApplication,
                                   StackPeriodResetPolicy _PeriodReset = StackPeriodResetPolicy.NeverReset,
                                   bool _DenyOverflow = false, bool _ClearOnOverflow = false)
        {
            var stacking = Effect.Stacking;
            stacking.type = _Type;
            stacking.limitCount = _Limit;
            stacking.expirationPolicy = _Expiration;
            stacking.durationRefreshPolicy = _Refresh;
            stacking.periodResetPolicy = _PeriodReset;
            stacking.denyOverflowApplication = _DenyOverflow;
            stacking.clearStackOnOverflow = _ClearOnOverflow;
            return this;
        }

        public EffectBuilder Overflow(params GameplayEffect[] _Effects)
        {
            Effect.Stacking.overflowEffects.AddRange(_Effects);
            return this;
        }

        private TargetTagRequirementsComponent Requirements()
        {
            var requirements = Effect.FindComponent<TargetTagRequirementsComponent>();
            if (requirements == null) Add(requirements = new TargetTagRequirementsComponent());
            return requirements;
        }

        /// <summary>
        /// Ongoing requirements: the effect is inhibited while the target has any of <paramref name="_MustNotHave"/>.
        /// </summary>
        public EffectBuilder Ongoing(params GameplayTag[] _MustNotHave)
        {
            Requirements().ongoing = new TagRequirements(null, new GameplayTagContainer(_MustNotHave));
            return this;
        }

        /// <summary>
        /// Application requirements: the target must have every tag.
        /// </summary>
        public EffectBuilder Requires(params GameplayTag[] _MustHave)
        {
            Requirements().application = new TagRequirements(new GameplayTagContainer(_MustHave));
            return this;
        }

        /// <summary>
        /// Removal requirements: the effect is removed when the target has every tag.
        /// </summary>
        public EffectBuilder RemovedBy(params GameplayTag[] _Tags)
        {
            Requirements().removal = new TagRequirements(new GameplayTagContainer(_Tags));
            return this;
        }

        public EffectBuilder Immunity(GameplayEffectQuery _Query) => Add(new ImmunityComponent { queries = { _Query } });

        public EffectBuilder RemoveOther(GameplayEffectQuery _Query) => Add(new RemoveOtherComponent { queries = { _Query } });

        public EffectBuilder InhibitionPolicy(PeriodicInhibitionPolicy _Policy)
        {
            Effect.PeriodicInhibitionPolicy = _Policy;
            return this;
        }

        public EffectBuilder GrantAbility(GameplayAbility _Ability, GrantedAbilityRemovalPolicy _Policy = GrantedAbilityRemovalPolicy.CancelAbilityImmediately)
            => Add(new AbilitiesComponent { abilities = { new GrantedAbilityConfig { ability = _Ability, removalPolicy = _Policy } } });

        public EffectBuilder Cue(GameplayTag _Cue, GameplayAttribute _MagnitudeAttribute = default, float _MinLevel = 0f, float _MaxLevel = 0f)
        {
            Effect.CueList.Add(new GameplayEffectCue
            {
                cueTags = new GameplayTagContainer(new[] { _Cue }),
                magnitudeAttribute = _MagnitudeAttribute,
                minLevel = _MinLevel,
                maxLevel = _MaxLevel,
            });
            return this;
        }

        public EffectBuilder Execution(EffectExecutionCalculation _Calculation, params ConditionalEffect[] _Conditional)
        {
            var execution = new ExecutionDefinition { calculation = _Calculation };
            execution.conditionalEffects.AddRange(_Conditional);
            Effect.ExecutionList.Add(execution);
            return this;
        }
    }



    internal static class Magnitudes
    {
        public static SetByCallerMagnitude SetByCaller(GameplayTag _Tag) => new(_Tag);

        public static AttributeBasedMagnitude AttributeBased(CaptureSource _Source, GameplayAttribute _Attribute, bool _Live, float _Coefficient = 1f,
                                                             float _PreMultiplyAdd = 0f, float _PostMultiplyAdd = 0f)
            => new(new AttributeCaptureDefinition(_Attribute, _Source, !_Live), _Coefficient, _PreMultiplyAdd, _PostMultiplyAdd);
    }



    internal static class AdditionalEffects
    {
        public static AdditionalEffectsComponent OnApplication(params GameplayEffect[] _Effects) => new() { onApplication = new(_Effects) };

        public static AdditionalEffectsComponent OnComplete(GameplayEffect _Normal, GameplayEffect _Premature) => new()
        {
            onCompleteNormal = new() { _Normal },
            onCompletePrematurely = new() { _Premature },
        };
    }



    /// <summary>
    /// Ability logic driven by delegates set in the test. Counters live in the test, not here. It clones itself
    /// member-wise, so the delegates survive (the default cloner would drop them, as non-serialized state).
    /// </summary>
    [Serializable]
    internal sealed class TestLogic : AbilityLogic
    {
        [NonSerialized] public Action<AbilityContext> onActivate;
        [NonSerialized] public Action<AbilityContext, bool> onEnd;
        [NonSerialized] public Func<AbilityContext, GameplayTagContainer, bool> canActivate;
        [NonSerialized] public Action<AbilityContext> onInputPressed;
        [NonSerialized] public bool cannotBeCancelled;

        /// <summary>
        /// A logic that never ends on its own.
        /// </summary>
        public static TestLogic StayActive() => new();

        /// <summary>
        /// A logic that ends inside its activation.
        /// </summary>
        public static TestLogic EndAtOnce() => new() { onActivate = ctx => ctx.End() };

        public override bool CanActivate(AbilityContext _Context, GameplayTagContainer _FailureTags) => canActivate?.Invoke(_Context, _FailureTags) ?? true;

        public override void Activate(AbilityContext _Context, GameplayEventData? _Trigger) => onActivate?.Invoke(_Context);

        public override void OnEnd(AbilityContext _Context, bool _WasCancelled) => onEnd?.Invoke(_Context, _WasCancelled);

        public override void OnInputPressed(AbilityContext _Context) => onInputPressed?.Invoke(_Context);

        public override bool CanBeCancelled => !cannotBeCancelled;

        protected internal override AbilityLogic Clone() => (AbilityLogic)MemberwiseClone();
    }



    /// <summary>
    /// Builds an in-memory <see cref="GameplayAbility"/> fluently.
    /// </summary>
    internal sealed class AbilityBuilder
    {
        public GameplayAbility Ability { get; }

        public AbilityBuilder(GameplayAbility _Ability) => Ability = _Ability;

        public AbilityBuilder Logic(AbilityLogic _Logic)
        {
            Ability.logic = _Logic;
            return this;
        }

        public AbilityBuilder Logic(Action<AbilityContext> _OnActivate) => Logic(new TestLogic { onActivate = _OnActivate });

        public AbilityBuilder Tags(params GameplayTag[] _Tags) => Do(() => Ability.abilityTags.AddTags(new GameplayTagContainer(_Tags)));

        public AbilityBuilder OwnedTags(params GameplayTag[] _Tags) => Do(() => Ability.activationOwnedTags.AddTags(new GameplayTagContainer(_Tags)));

        public AbilityBuilder Blocks(params GameplayTag[] _Tags) => Do(() => Ability.blockAbilitiesWithTag.AddTags(new GameplayTagContainer(_Tags)));

        public AbilityBuilder Cancels(params GameplayTag[] _Tags) => Do(() => Ability.cancelAbilitiesWithTag.AddTags(new GameplayTagContainer(_Tags)));

        public AbilityBuilder Requires(params GameplayTag[] _Tags) => Do(() => Ability.activationRequiredTags.AddTags(new GameplayTagContainer(_Tags)));

        public AbilityBuilder BlockedBy(params GameplayTag[] _Tags) => Do(() => Ability.activationBlockedTags.AddTags(new GameplayTagContainer(_Tags)));

        public AbilityBuilder Cost(GameplayEffect _Effect) => Do(() => Ability.costEffect = _Effect);

        public AbilityBuilder Cooldown(GameplayEffect _Effect) => Do(() => Ability.cooldownEffect = _Effect);

        public AbilityBuilder Trigger(AbilityTriggerSource _Source, GameplayTag _Tag) => Do(() => Ability.triggers.Add(new AbilityTrigger(_Tag, _Source)));

        public AbilityBuilder InstancedPerActor() => Do(() => Ability.instancingPolicy = InstancingPolicy.InstancedPerActor);

        public AbilityBuilder InstancedPerExecution() => Do(() => Ability.instancingPolicy = InstancingPolicy.InstancedPerExecution);

        public AbilityBuilder Retrigger() => Do(() => Ability.retriggerInstancedAbility = true);

        private AbilityBuilder Do(Action _Change)
        {
            _Change();
            return this;
        }
    }
}