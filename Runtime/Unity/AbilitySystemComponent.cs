using System;
using System.Collections.Generic;

using UnityEngine;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// A granted ability a component starts with.
    /// </summary>
    [Serializable]
    public struct DefaultAbility
    {
        public GameplayAbility ability;

        [Min(0)]
        public float level;

        [Tooltip("The input that activates the ability, or -1 for none.")]
        public int inputId;

        public DefaultAbility(GameplayAbility _Ability, float _Level = 1f, int _InputId = -1)
        {
            ability = _Ability;
            level = _Level;
            inputId = _InputId;
        }
    }



    /// <summary>
    /// A scene object's unit: hosts a <see cref="GameplayAbilitySystem"/> in the scene's world, with the
    /// attribute sets, startup effects and abilities it starts with.
    /// </summary>
    /// <remarks>
    /// <c>Awake</c> creates the unit, so other components can subscribe to it in their <c>Awake</c> or <c>OnEnable</c>.
    /// <c>Start</c> sets the owner and avatar, then adds the sets, applies the startup effects and grants the abilities.
    /// <c>OnDestroy</c> shuts the unit down. Gameplay goes through <see cref="System"/>.
    /// </remarks>
    [DefaultExecutionOrder(-50), DisallowMultipleComponent]
    [AddComponentMenu("LMK/Ability System/Ability System Component")]
    public class AbilitySystemComponent : MonoBehaviour
    {
        [SerializeField, Tooltip("The world runner. Empty for the scene's first, created if there is none.")]
        private AbilitySystemWorldRunner runner;

        [SerializeField, Tooltip("The object abilities act through: animation, sockets, targeting. Empty for this object.")]
        private GameObject avatar;

        [SerializeField, Tooltip("Level of the default attribute sets and startup effects.")]
        private float level = 1f;

        [SerializeField]
        private List<AttributeSetDefinition> defaultSets = new();

        [SerializeField, Tooltip("Applied to the unit at start, in order, after its sets.")]
        private List<GameplayEffect> startupEffects = new();

        [SerializeField]
        private List<DefaultAbility> defaultAbilities = new();

        /// <summary>
        /// The unit. Created in <c>Awake</c>; shut down in <c>OnDestroy</c>.
        /// </summary>
        public GameplayAbilitySystem System { get; private set; }

        public AbilitySystemWorldRunner Runner => runner;

        public GameObject Avatar
        {
            get => avatar != null ? avatar : gameObject;
            set
            {
                avatar = value;
                if (System != null && started) System.InitActorInfo(gameObject, Avatar);
            }
        }

        public float Level => level;

        public IList<AttributeSetDefinition> DefaultSets => defaultSets;

        public IList<GameplayEffect> StartupEffects => startupEffects;

        public IList<DefaultAbility> DefaultAbilities => defaultAbilities;

        private bool started;

        protected virtual void Awake()
        {
            runner = AbilitySystemWorldRunner.Find(runner);
            System = runner.World.CreateSystem(name);
        }

        protected virtual void Start()
        {
            if (System == null || System.IsShuttingDown) return;

            started = true;
            System.InitActorInfo(gameObject, Avatar);

            foreach (var set in defaultSets)
                if (set != null)
                    System.AddAttributeSet(set, level);

            foreach (var effect in startupEffects)
                if (effect != null)
                    System.ApplyEffectToSelf(effect, level);

            foreach (var grant in defaultAbilities)
                if (grant.ability != null)
                    System.GiveAbility(grant.ability, grant.level > 0 ? grant.level : 1f, grant.inputId);
        }

        protected virtual void OnDestroy()
        {
            System?.Shutdown();
        }
    }
}
