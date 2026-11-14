using System;
using System.Collections.Generic;

using UnityEngine;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// One simulation: the clock, the random source, the net driver, the settings, and every unit's
    /// <see cref="GameplayAbilitySystem"/>, stepped in registration order. Holds no static state, so two worlds can run in one
    /// process.
    /// </summary>
    /// <remarks>
    /// A step advances the clock, ticks every unit, flushes any attribute change still queued, then adds
    /// and removes the units created or shut down during the step. <br/>
    /// A scene drives its world from <c>AbilitySystemWorldRunner</c>; tests and tools call <see cref="Advance"/> or
    /// <see cref="Step"/> themselves.
    /// </remarks>
    public sealed class AbilitySystemWorld : IDisposable
    {
        private readonly List<GameplayAbilitySystem> systems = new();
        private readonly List<GameplayAbilitySystem> pendingAdds = new();
        private readonly List<GameplayAbilitySystem> pendingRemoves = new();
        private readonly AbilitySystemSettings ownedSettings;
        private bool stepping;
        private int lastHandle;

        public AbilitySystemSettings Settings { get; }

        public IAbilitySystemClock Clock { get; }

        public IAbilitySystemRandom Random { get; }

        public IAbilitySystemNetDriver NetDriver { get; }

        /// <summary>
        /// The world's time, in seconds: <see cref="IAbilitySystemClock.Time"/>.
        /// </summary>
        public double Time => Clock.Time;

        /// <summary>
        /// The units of the world, in registration order. Units created during a step join after it.
        /// </summary>
        public IReadOnlyList<GameplayAbilitySystem> Systems => systems;

        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Called after each step, with the step's length in seconds.
        /// </summary>
        public event Action<double> Stepped;



        /// <param name="_Settings"> The project's settings, or <see langword="null"/> for the defaults. </param>
        /// <param name="_Clock"> <see langword="null"/> for a <see cref="ScaledClock"/>. </param>
        /// <param name="_Random"> <see langword="null"/> for a <see cref="SeededRandom"/> with the default seed. </param>
        /// <param name="_NetDriver"> <see langword="null"/> for a <see cref="SinglePlayerNetDriver"/>. </param>
        public AbilitySystemWorld(AbilitySystemSettings _Settings = null, IAbilitySystemClock _Clock = null,
                                  IAbilitySystemRandom _Random = null, IAbilitySystemNetDriver _NetDriver = null)
        {
            if (_Settings == null)
                _Settings = ownedSettings = AbilitySystemSettings.CreateDefault();

            Settings = _Settings;
            Clock = _Clock ?? new ScaledClock();
            Random = _Random ?? new SeededRandom();
            NetDriver = _NetDriver ?? new SinglePlayerNetDriver();
        }

        /// <summary>
        /// Shut every unit down, and release what the world owns.
        /// </summary>
        public void Dispose()
        {
            if (IsDisposed) return;

            foreach (var system in new List<GameplayAbilitySystem>(systems))
                system.Shutdown();
            foreach (var system in new List<GameplayAbilitySystem>(pendingAdds))
                system.Shutdown();

            foreach (var ability in FindWrittenAbilityAssets())
                Debug.LogError($"Ability asset \"{ability.name}\" was written while the world ran. Ability assets are templates: keep per-unit state in " +
                               "[NonSerialized] fields of the cloned logic.");
            abilityAssetStates.Clear();

            systems.Clear();
            pendingAdds.Clear();
            pendingRemoves.Clear();
            IsDisposed = true;

            if (ownedSettings != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(ownedSettings);
                else UnityEngine.Object.DestroyImmediate(ownedSettings);
            }
        }



        #region Units
        /// <summary>
        /// Create a unit's ability system in this world. Created during a step, it joins the world's tick after the step.
        /// </summary>
        /// <param name="_Name"> A name for logs and the debugger. </param>
        public GameplayAbilitySystem CreateSystem(string _Name = null)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(AbilitySystemWorld));

            var system = new GameplayAbilitySystem(this, _Name);
            if (stepping) pendingAdds.Add(system);
            else systems.Add(system);

            return system;
        }

        /// <summary>
        /// Called by a unit that shuts down. During a step its slot is freed after the step.
        /// </summary>
        internal void Unregister(GameplayAbilitySystem _System)
        {
            if (pendingAdds.Remove(_System)) return;

            if (stepping) pendingRemoves.Add(_System);
            else systems.Remove(_System);
        }

        /// <summary>
        /// How many effect applications are running inside each other, across every unit of the world. Bounded by
        /// <see cref="AbilitySystemSettings.MaxApplicationDepth"/>.
        /// </summary>
        internal int ApplicationDepth { get; set; }

        /// <summary>
        /// How many ability activations are running inside each other, across every unit of the world. Bounded by
        /// <see cref="AbilitySystemSettings.MaxActivationDepth"/>.
        /// </summary>
        internal int ActivationDepth { get; set; }

        /// <summary>
        /// How ability logic is cloned for each instance. <see cref="InstantiateAbilityLogicCloner"/> by
        /// default.
        /// </summary>
        public IAbilityLogicCloner AbilityLogicCloner { get; set; } = new InstantiateAbilityLogicCloner();

        private ICueDispatcher cues = new GameplayCueQueue();

        /// <summary>
        /// Where the world's cue events go: queued during a step, delivered at its end. A
        /// <see cref="GameplayCueQueue"/> without a handler by default, which drops them.
        /// </summary>
        public ICueDispatcher Cues
        {
            get => cues;
            set => cues = value ?? new GameplayCueQueue();
        }

        // The serialized state of every ability asset granted in this world, when first granted.
        private readonly Dictionary<GameplayAbility, string> abilityAssetStates = new();

        /// <summary>
        /// Remember an ability asset's serialized state, to check when the world ends that nothing wrote to it. Debug
        /// builds and the editor only.
        /// </summary>
        internal void TrackAbilityAsset(GameplayAbility _Ability)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_Ability != null && !abilityAssetStates.ContainsKey(_Ability))
                abilityAssetStates.Add(_Ability, JsonUtility.ToJson(_Ability));
#endif
        }

        /// <summary>
        /// The ability assets whose serialized state changed since they were first granted in this world: something
        /// wrote to a template, which would persist after Play Mode in the editor. Logged when the world is disposed.
        /// </summary>
        public List<GameplayAbility> FindWrittenAbilityAssets()
        {
            var written = new List<GameplayAbility>();
            foreach (var (ability, state) in abilityAssetStates)
                if (ability != null && JsonUtility.ToJson(ability) != state)
                    written.Add(ability);

            return written;
        }

        /// <summary>
        /// A new handle number, never reused within this world. 0 is never issued.
        /// </summary>
        internal int NextHandle()
        {
            if (lastHandle == int.MaxValue)
                throw new InvalidOperationException("The world ran out of handles.");

            return ++lastHandle;
        }
        #endregion



        #region Stepping
        /// <summary>
        /// Move time forward by <paramref name="_Seconds"/>, in equal steps of at most <see cref="AbilitySystemSettings.MaxStep"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="_Seconds"/> is negative or not finite. </exception>
        public void Advance(double _Seconds)
        {
            CheckSeconds(_Seconds);
            if (_Seconds == 0) return;

            double maxStep = Settings.MaxStep;

            // Equal steps instead of MaxStep plus a remainder: repeated subtraction leaves float dust that would add a step.
            int steps = (int)Math.Ceiling(_Seconds / maxStep - 1e-9);
            if (steps < 1) steps = 1;
            double step = _Seconds / steps;

            for (int i = 0; i < steps; i++)
                Step(step);
        }

        /// <summary>
        /// One step of <paramref name="_Seconds"/>, at most <see cref="AbilitySystemSettings.MaxStep"/>: advance the
        /// clock, tick every unit in registration order, flush queued attribute changes, then add and remove the units
        /// created or shut down meanwhile.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"> <paramref name="_Seconds"/> is negative, not finite, or longer than a step. </exception>
        /// <exception cref="InvalidOperationException"> Called from inside a step. </exception>
        public void Step(double _Seconds)
        {
            CheckSeconds(_Seconds);
            if (_Seconds > Settings.MaxStep * (1 + 1e-9))
                throw new ArgumentOutOfRangeException(nameof(_Seconds), _Seconds, $"A step is at most {Settings.MaxStep} s. Use Advance to split longer times.");
            if (stepping)
                throw new InvalidOperationException("The world is already stepping.");
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(AbilitySystemWorld));

            stepping = true;
            try
            {
                Clock.Advance(_Seconds);

                foreach (var system in systems)
                    if (!system.IsShuttingDown)
                        system.Tick(Clock.Time, _Seconds);

                foreach (var system in systems)
                    if (!system.IsShuttingDown)
                        system.TickTasks(Clock.Time, _Seconds);

                foreach (var system in systems)
                    if (!system.IsShuttingDown)
                        system.FlushPropagation();
            }
            finally
            {
                stepping = false;
                ApplyPendingAddsAndRemoves();
            }

            Cues.Flush();
            Stepped?.Invoke(_Seconds);
        }

        private void ApplyPendingAddsAndRemoves()
        {
            foreach (var system in pendingRemoves)
                systems.Remove(system);
            pendingRemoves.Clear();

            systems.AddRange(pendingAdds);
            pendingAdds.Clear();
        }

        private static void CheckSeconds(double _Seconds)
        {
            if (double.IsNaN(_Seconds) || double.IsInfinity(_Seconds) || _Seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(_Seconds), _Seconds, "Time only moves forward by a finite amount.");
        }
        #endregion
    }
}
