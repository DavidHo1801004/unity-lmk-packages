using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// A granted ability, identified by a handle issued by its world. Handles are never reused.
    /// </summary>
    public readonly struct AbilitySpecHandle : IEquatable<AbilitySpecHandle>
    {
        public static readonly AbilitySpecHandle Invalid = default;

        public readonly int Id;

        public bool IsValid => Id > 0;

        internal AbilitySpecHandle(int _Id) => Id = _Id;

        public bool Equals(AbilitySpecHandle _Other) => Id == _Other.Id;

        public override bool Equals(object _Obj) => _Obj is AbilitySpecHandle other && Equals(other);

        public override int GetHashCode() => Id;

        public static bool operator ==(AbilitySpecHandle _A, AbilitySpecHandle _B) => _A.Id == _B.Id;

        public static bool operator !=(AbilitySpecHandle _A, AbilitySpecHandle _B) => _A.Id != _B.Id;

        public override string ToString() => IsValid ? $"Ability {Id}" : "Invalid";
    }



    /// <summary>
    /// Who a unit is in the world: the owner that holds its ability system, and the avatar that is its body. For a
    /// simple unit they are the same object. Plain objects, so the core runs without a scene; the
    /// <c>AbilitySystemComponent</c> passes GameObjects.
    /// </summary>
    public sealed class AbilityActorInfo
    {
        public object Owner { get; internal set; }

        public object Avatar { get; internal set; }

        public GameObject OwnerGameObject => Owner as GameObject ?? (Owner as Component)?.gameObject;

        public GameObject AvatarGameObject => Avatar as GameObject ?? (Avatar as Component)?.gameObject;
    }



    /// <summary>
    /// A granted ability on a unit (UE's <c>FGameplayAbilitySpec</c>): the asset, level, input, and its instances.
    /// </summary>
    public sealed class AbilitySpec
    {
        internal readonly List<AbilityInstance> executionInstances = new();
        internal readonly List<IDisposable> triggerRegistrations = new();

        public AbilitySpecHandle Handle { get; }

        public GameplayAbility Ability { get; }

        public float Level { get; internal set; }

        /// <summary>
        /// The input ID the game maps an input action to, or -1 for none.
        /// </summary>
        public int InputId { get; }

        public UnityEngine.Object SourceObject { get; }

        /// <summary>
        /// Tags this grant has besides the ability's own.
        /// </summary>
        public GameplayTagContainer DynamicTags { get; } = new();

        /// <summary>
        /// The active effect that granted the ability, or <see cref="ActiveEffectHandle.Invalid"/>.
        /// </summary>
        public ActiveEffectHandle GrantedByEffect { get; internal set; }

        /// <summary>
        /// How many activations are running now.
        /// </summary>
        public int ActiveCount { get; internal set; }

        public bool InputPressed { get; internal set; }

        /// <summary>
        /// Being cleared: it can't activate any more, and goes when its activations end.
        /// </summary>
        public bool PendingRemove { get; internal set; }

        /// <summary>
        /// Removed when its current activation ends.
        /// </summary>
        public bool RemoveAfterActivation { get; internal set; }

        /// <summary>
        /// Gone from the unit. Its handle no longer finds it.
        /// </summary>
        public bool IsRemoved { get; internal set; }

        /// <summary>
        /// The one instance of an <see cref="InstancingPolicy.InstancedPerActor"/> ability, created at grant.
        /// </summary>
        public AbilityInstance PerActorInstance { get; internal set; }

        /// <summary>
        /// The running instances of an <see cref="InstancingPolicy.InstancedPerExecution"/> ability.
        /// </summary>
        public IReadOnlyList<AbilityInstance> ExecutionInstances => executionInstances;

        public bool IsActive => ActiveCount > 0;

        internal AbilitySpec(AbilitySpecHandle _Handle, GameplayAbility _Ability, float _Level, int _InputId, UnityEngine.Object _SourceObject)
        {
            Handle = _Handle;
            Ability = _Ability;
            Level = _Level;
            InputId = _InputId;
            SourceObject = _SourceObject;
        }

        /// <summary>
        /// The instances running an activation now.
        /// </summary>
        public IEnumerable<AbilityInstance> ActiveInstances
        {
            get
            {
                if (PerActorInstance != null && PerActorInstance.IsActive)
                    yield return PerActorInstance;

                foreach (var instance in executionInstances.ToArray())
                    if (instance.IsActive)
                        yield return instance;
            }
        }

        /// <summary>
        /// The ability's tags and this grant's.
        /// </summary>
        public GameplayTagContainer GetAllTags()
        {
            var tags = new GameplayTagContainer(Ability.abilityTags);
            tags.AddTags(DynamicTags);
            return tags;
        }

        public override string ToString() => $"{Handle}: {(Ability != null ? Ability.name : "?")}";
    }



    /// <summary>
    /// One clone of an ability's logic, and the state of its activations. A per-actor instance lives as
    /// long as the grant; a per-execution instance for one activation.
    /// </summary>
    public sealed class AbilityInstance
    {
        internal readonly List<AbilityTask> tasks = new();

        // The cues the running activation added, removed when it ends.
        internal readonly List<GameplayTag> cues = new();

        public AbilitySpec Spec { get; }

        /// <summary>
        /// The clone. Never the asset's template.
        /// </summary>
        public AbilityLogic Logic { get; }

        public AbilityContext Context { get; }

        /// <summary>
        /// Increases with every activation of the unit, so a callback from an earlier activation can be told apart.
        /// </summary>
        public int ActivationId { get; internal set; }

        public bool IsActive { get; internal set; }

        public bool IsEnding { get; internal set; }

        /// <summary>
        /// Reserved for multiplayer prediction. Always 0.
        /// </summary>
        public int PredictionKey => 0;

        internal AbilityInstance(GameplayAbilitySystem _Owner, AbilitySpec _Spec, AbilityLogic _Logic)
        {
            Spec = _Spec;
            Logic = _Logic;
            Context = new AbilityContext(_Owner, _Spec, this);
        }

        /// <summary>
        /// Run a task callback or an await continuation, unless it belongs to an activation that is over: stale
        /// callbacks are dropped.
        /// </summary>
        internal void Deliver(int _ActivationId, Action _Callback)
        {
            if (!IsActive || IsEnding || _ActivationId != ActivationId) return;

            try
            {
                _Callback();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
