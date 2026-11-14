using System.Collections.Generic;

using UnityEngine;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Runs a scene's <see cref="AbilitySystemWorld"/>: creates it with the settings asset, steps it every
    /// frame (in <c>Update</c>, or <c>FixedUpdate</c> with <see cref="AbilitySystemSettings.StepInFixedUpdate"/>), and
    /// shows its cues through a <see cref="GameplayCueManager"/> built from the cue sets.
    /// </summary>
    /// <remarks>
    /// One runner per world. <see cref="AbilitySystemComponent"/>s without a runner use the first one in the scene, and
    /// create one if there is none.
    /// </remarks>
    [DefaultExecutionOrder(-100), DisallowMultipleComponent]
    [AddComponentMenu("LMK/Ability System/Ability System World Runner")]
    public sealed class AbilitySystemWorldRunner : MonoBehaviour
    {
        [SerializeField, Tooltip("The world's limits. Empty for the defaults.")]
        private AbilitySystemSettings settings;

        [SerializeField, Tooltip("The cue notifies this scene shows.")]
        private List<GameplayCueSet> cueSets = new();

        [SerializeField, Tooltip("Seed of the world's random source. 0 for the default seed.")]
        private ulong randomSeed;

        private AbilitySystemWorld world;
        private Transform poolRoot;

        /// <summary>
        /// The world this runner steps, created on first use.
        /// </summary>
        public AbilitySystemWorld World => EnsureWorld();

        /// <summary>
        /// The cue manager of the world, created with it.
        /// </summary>
        public GameplayCueManager CueManager { get; private set; }

        public AbilitySystemSettings Settings
        {
            get => settings;
            set
            {
                if (world != null) Debug.LogWarning($"[{name}] The world already runs with its settings: the new ones apply to the next world.", this);
                settings = value;
            }
        }

        public IList<GameplayCueSet> CueSets => cueSets;

        private void Awake() => EnsureWorld();

        private AbilitySystemWorld EnsureWorld()
        {
            if (world != null && !world.IsDisposed) return world;

            poolRoot = new GameObject("Cue Pool").transform;
            poolRoot.SetParent(transform, false);

            CueManager = new GameplayCueManager(cueSets, poolRoot);
            world = new AbilitySystemWorld(settings, null, randomSeed == 0 ? null : new SeededRandom(randomSeed))
            {
                Cues = new GameplayCueQueue { Handler = CueManager },
            };

            return world;
        }

        private void Update()
        {
            if (world != null && !StepsInFixedUpdate) Run(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (world != null && StepsInFixedUpdate) Run(Time.fixedDeltaTime);
        }

        private bool StepsInFixedUpdate => world.Settings.StepInFixedUpdate;

        /// <summary>
        /// Move the world forward. Called each frame; call it yourself only on a disabled runner.
        /// </summary>
        public void Run(double _Seconds)
        {
            if (world == null || world.IsDisposed || _Seconds <= 0) return;

            world.Advance(_Seconds);
            CueManager.Tick(world.Time);
        }

        private void OnDestroy()
        {
            world?.Dispose();
            CueManager?.Dispose();
            world = null;
        }

        /// <summary>
        /// The runner of the scene: <paramref name="_Preferred"/>, else the first in the scene, else a new one.
        /// </summary>
        internal static AbilitySystemWorldRunner Find(AbilitySystemWorldRunner _Preferred)
        {
            if (_Preferred != null) return _Preferred;

            var runner = FindAnyObjectByType<AbilitySystemWorldRunner>();
            if (runner != null) return runner;

            return new GameObject("Ability System World").AddComponent<AbilitySystemWorldRunner>();
        }
    }
}
