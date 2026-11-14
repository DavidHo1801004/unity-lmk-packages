using System;
using System.Collections.Generic;

using LMK.Runtime.AbilitySystem;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

using Object = UnityEngine.Object;
using Row = LMK.Runtime.AbilitySystem.GameplayAttributeDatabaseBuilder.Row;
using Unit = LMK.Runtime.AbilitySystem.GameplayAbilitySystem;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// The test registry's attributes, as references.
    /// </summary>
    internal static class TestAttributes
    {
        public static readonly RegistryGuid ExtrasRegistry = RegistryGuid.Parse("7c1e9a00000000000000000000000003");

        public static readonly GameplayAttribute Health = AttributeIds.Main(AttributeIds.HEALTH, "Vitals.Health");
        public static readonly GameplayAttribute MaxHealth = AttributeIds.Main(AttributeIds.MAX_HEALTH, "Vitals.MaxHealth");
        public static readonly GameplayAttribute Mana = AttributeIds.Main(AttributeIds.MANA, "Vitals.Mana");
        public static readonly GameplayAttribute MaxMana = AttributeIds.Main(AttributeIds.MAX_MANA, "Vitals.MaxMana");
        public static readonly GameplayAttribute AttackPower = AttributeIds.Main(AttributeIds.ATTACK_POWER, "Combat.AttackPower");
        public static readonly GameplayAttribute Armor = AttributeIds.Main(AttributeIds.ARMOR, "Combat.Armor");
        public static readonly GameplayAttribute IncomingDamage = AttributeIds.Main(AttributeIds.INCOMING_DAMAGE, "Meta.IncomingDamage");
        public static readonly GameplayAttribute A = AttributeIds.Main(AttributeIds.A, "A");
        public static readonly GameplayAttribute B = AttributeIds.Main(AttributeIds.B, "B");

        /// <summary>
        /// An integer attribute in [0, -], default 0, in a second registry.
        /// </summary>
        public static readonly GameplayAttribute Charges = GameplayAttribute.FromId(ExtrasRegistry, AttributeIds.Id(1), "Combat.Charges");

        /// <summary>
        /// An attribute named like <see cref="Health"/>, in the second registry: another attribute.
        /// </summary>
        public static readonly GameplayAttribute OtherHealth = GameplayAttribute.FromId(ExtrasRegistry, AttributeIds.Id(2), "Vitals.Health");

        public static GameplayAttributeDatabaseBuilder Builder() => AttributeIds.MainBuilder()
            .AddRegistry(ExtrasRegistry, "Extras", RegistryOrigin.Package, new[]
            {
                new Row(AttributeIds.Id(1), "Combat.Charges", 0f, AttributeBound.FromConstant(0f), _Flags: AttributeFlags.Integer),
                new Row(AttributeIds.Id(2), "Vitals.Health", 100f, AttributeBound.FromConstant(0f)),
            });
    }



    /// <summary>
    /// Counts and keeps every event of the units it watches.
    /// </summary>
    internal sealed class EventRecorder
    {
        public readonly List<AttributeChange> Changes = new();
        public readonly List<AttributeSetDefinition> Initialized = new();
        public readonly List<GameplayEventData> GameplayEvents = new();
        public readonly List<(GameplayEffectSpec Spec, ActiveEffectHandle Handle)> Applied = new();
        public readonly List<(ActiveGameplayEffect Effect, RemovalInfo Info)> Removed = new();
        public readonly List<(AbilitySpec Spec, bool WasCancelled)> Ended = new();
        public readonly List<(GameplayAbility Ability, ActivationFailure Failure)> Failed = new();
        public readonly List<AbilitySpec> Activated = new();

        public int AttributeChanges => Changes.Count;

        public int Initializations => Initialized.Count;

        public void Watch(Unit _Unit)
        {
            _Unit.OnAttributeChanged += Changes.Add;
            _Unit.OnAttributesInitialized += Initialized.Add;
            _Unit.OnGameplayEvent += (_, data) => GameplayEvents.Add(data);
            _Unit.OnEffectAppliedToSelf += (spec, handle) => Applied.Add((spec, handle));
            _Unit.OnEffectRemoved += (effect, info) => Removed.Add((effect, info));
            _Unit.OnAbilityEnded += (spec, cancelled) => Ended.Add((spec, cancelled));
            _Unit.OnAbilityFailed += (ability, failure) => Failed.Add((ability, failure));
            _Unit.OnAbilityActivated += Activated.Add;
        }

        public int ChangesOf(GameplayAttribute _Attribute) => Changes.FindAll(e => e.Attribute == _Attribute).Count;

        public void Clear()
        {
            Changes.Clear();
            Initialized.Clear();
            GameplayEvents.Clear();
            Applied.Clear();
            Removed.Clear();
            Ended.Clear();
            Failed.Clear();
            Activated.Clear();
        }
    }



    /// <summary>
    /// Test fixture around one ability system world: a manual clock, a seeded random source, the test
    /// registry as the active attribute database, units, a log recorder and an event recorder. <br/>
    /// Plain C# objects only: no scene, no frames, no asset files.
    /// </summary>
    /// <remarks>
    /// <b>NOTE:</b> It gains effect and ability builders as those systems are built.
    /// </remarks>
    internal sealed class TestWorld : IDisposable
    {
        private readonly List<Object> owned = new();
        private readonly GameplayAttributeDatabase previousDatabase;
        private readonly Action previousEnsureActive;
        private readonly GameplayTagDatabase previousTags;
        private readonly Action previousEnsureTags;
        private int effectCount;
        private int abilityCount;

        public ManualClock Clock { get; } = new();

        public SeededRandom Random { get; } = new(12345);

        public AbilitySystemSettings Settings { get; }

        public AbilitySystemWorld World { get; }

        public GameplayAttributeDatabase Database { get; }

        public GameplayTagDatabase TagDatabase { get; }

        public TestLog Log { get; } = new();

        public EventRecorder Events { get; } = new();

        /// <summary>
        /// Longest step <see cref="Advance(double)"/> takes at once, in seconds.
        /// </summary>
        public double MaxStep => Settings.MaxStep;

        /// <summary>
        /// Raised after each step, with the step's length in seconds.
        /// </summary>
        public event Action<double> Stepped
        {
            add => World.Stepped += value;
            remove => World.Stepped -= value;
        }



        public TestWorld()
        {
            previousEnsureActive = GameplayAttributeDatabase.EditorEnsureActive;
            previousDatabase = GameplayAttributeDatabase.Active;
            GameplayAttributeDatabase.EditorEnsureActive = null;

            Database = Own(TestAttributes.Builder().Build());
            GameplayAttributeDatabase.SetActive(Database);

            previousEnsureTags = TagDatabaseAccess.EnsureActive;
            previousTags = GameplayTagDatabase.Active;
            TagDatabaseAccess.EnsureActive = null;
            TagDatabase = Own(TestTags.Builder().Build());
            TagDatabaseAccess.SetActive(TagDatabase);

            Settings = Own(AbilitySystemSettings.CreateDefault());
            World = new AbilitySystemWorld(Settings, Clock, Random);
        }

        public void Dispose()
        {
            World.Dispose();

            GameplayAttributeDatabase.EditorEnsureActive = previousEnsureActive;
            GameplayAttributeDatabase.SetActive(previousDatabase);
            TagDatabaseAccess.EnsureActive = previousEnsureTags;
            TagDatabaseAccess.SetActive(previousTags);

            foreach (var item in owned)
                if (item != null)
                    Object.DestroyImmediate(item);

            Log.Dispose();
        }

        private T Own<T>(T _Object) where T : Object
        {
            owned.Add(_Object);
            return _Object;
        }



        #region Effects
        /// <summary>
        /// An in-memory effect, built by <paramref name="_Build"/>, destroyed with the world.
        /// </summary>
        public GameplayEffect Effect(Action<EffectBuilder> _Build)
        {
            var effect = Own(UnityEngine.ScriptableObject.CreateInstance<GameplayEffect>());
            effect.name = $"Effect {++effectCount}";
            _Build(new EffectBuilder(effect));
            return effect;
        }
        /// <summary>
        /// An in-memory ability, built by <paramref name="_Build"/>, destroyed with the world. Instanced per actor and
        /// ending at once unless built otherwise.
        /// </summary>
        public GameplayAbility Ability(Action<AbilityBuilder> _Build)
        {
            var ability = Own(UnityEngine.ScriptableObject.CreateInstance<GameplayAbility>());
            ability.name = $"Ability {++abilityCount}";
            ability.logic = TestLogic.EndAtOnce();
            _Build(new AbilityBuilder(ability));
            return ability;
        }
        #endregion



        #region Units
        /// <summary>
        /// An attribute set: each attribute with its initial value, or its registry default for <see langword="null"/>.
        /// </summary>
        public AttributeSetDefinition Set(params (GameplayAttribute Attribute, float? Initial)[] _Entries)
        {
            var set = Own(UnityEngine.ScriptableObject.CreateInstance<AttributeSetDefinition>());
            foreach (var (attribute, initial) in _Entries)
                set.Add(attribute, initial);

            return set;
        }

        /// <summary>
        /// A unit with one set holding these attributes and initial values, watched by <see cref="Events"/>.
        /// </summary>
        public Unit MakeUnit(params (GameplayAttribute Attribute, float Initial)[] _InitialValues)
        {
            var entries = Array.ConvertAll(_InitialValues, e => (e.Attribute, (float?)e.Initial));
            return MakeUnit(Set(entries));
        }

        /// <summary>
        /// A unit with no attributes, watched by <see cref="Events"/>: a source of effects.
        /// </summary>
        public Unit MakeUnit() => MakeUnit(Array.Empty<AttributeSetDefinition>());

        /// <summary>
        /// A unit with these sets, watched by <see cref="Events"/>.
        /// </summary>
        public Unit MakeUnit(params AttributeSetDefinition[] _Sets)
        {
            var unit = World.CreateSystem($"Unit {World.Systems.Count + 1}");
            Events.Watch(unit);
            unit.InitActorInfo(unit.Name, new object());

            foreach (var set in _Sets)
                unit.AddAttributeSet(set);

            return unit;
        }
        #endregion



        /// <summary>
        /// Moves time forward by <paramref name="_Seconds"/>, in equal steps of at most <see cref="MaxStep"/>.
        /// </summary>
        public void Advance(double _Seconds) => World.Advance(_Seconds);
    }
}
