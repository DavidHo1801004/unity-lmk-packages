using System;

using UnityEngine;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Project limits of the ability system: the step size and period floor, and the guards that stop runaway chains
    ///. A world reads them from the settings asset it is given.
    /// </summary>
    [CreateAssetMenu(menuName = "LMK/Ability System/Settings", fileName = "AbilitySystemSettings")]
    public sealed class AbilitySystemSettings : ScriptableObject
    {
        public const double DEFAULT_MAX_STEP = 0.1;
        public const double DEFAULT_MIN_PERIOD = 0.02;
        public const int DEFAULT_MAX_PROPAGATION_PASSES = 8;
        public const int DEFAULT_MAX_APPLICATION_DEPTH = 32;
        public const int DEFAULT_MAX_ACTIVATION_DEPTH = 8;

        [SerializeField, Min(0.001f), Tooltip("Longest world step, in seconds. A longer frame is split into equal steps of at most this length.")]
        private double maxStep = DEFAULT_MAX_STEP;

        [SerializeField, Min(0.001f), Tooltip("Shortest effect period, in seconds. Shorter periods are clamped to it.")]
        private double minPeriod = DEFAULT_MIN_PERIOD;

        [SerializeField, Min(1), Tooltip("Passes of attribute change propagation before a dependency cycle is cut off with an error.")]
        private int maxPropagationPasses = DEFAULT_MAX_PROPAGATION_PASSES;

        [SerializeField, Min(1), Tooltip("Effects applied from inside other applications before the chain is cut off with an error.")]
        private int maxApplicationDepth = DEFAULT_MAX_APPLICATION_DEPTH;

        [SerializeField, Min(1), Tooltip("Abilities activated from inside other activations before the chain is cut off with an error.")]
        private int maxActivationDepth = DEFAULT_MAX_ACTIVATION_DEPTH;

        [SerializeField, Tooltip("The scene runner steps the world in FixedUpdate instead of Update.")]
        private bool stepInFixedUpdate;

        /// <summary>
        /// Longest world step, in seconds.
        /// </summary>
        public double MaxStep
        {
            get => maxStep;
            set => maxStep = Positive(value, nameof(MaxStep));
        }

        /// <summary>
        /// Shortest effect period, in seconds.
        /// </summary>
        public double MinPeriod
        {
            get => minPeriod;
            set => minPeriod = Positive(value, nameof(MinPeriod));
        }

        public int MaxPropagationPasses
        {
            get => maxPropagationPasses;
            set => maxPropagationPasses = AtLeastOne(value, nameof(MaxPropagationPasses));
        }

        public int MaxApplicationDepth
        {
            get => maxApplicationDepth;
            set => maxApplicationDepth = AtLeastOne(value, nameof(MaxApplicationDepth));
        }

        public int MaxActivationDepth
        {
            get => maxActivationDepth;
            set => maxActivationDepth = AtLeastOne(value, nameof(MaxActivationDepth));
        }

        /// <summary>
        /// Whether <c>AbilitySystemWorldRunner</c> steps the world in <c>FixedUpdate</c> instead of <c>Update</c>.
        /// </summary>
        public bool StepInFixedUpdate
        {
            get => stepInFixedUpdate;
            set => stepInFixedUpdate = value;
        }

        /// <summary>
        /// A settings object with the default values, not saved anywhere. The caller destroys it.
        /// </summary>
        public static AbilitySystemSettings CreateDefault()
        {
            var settings = CreateInstance<AbilitySystemSettings>();
            settings.name = "Default Ability System Settings";
            settings.hideFlags = HideFlags.DontSave;
            return settings;
        }

        private void OnValidate()
        {
            if (!(maxStep > 0) || double.IsInfinity(maxStep)) maxStep = DEFAULT_MAX_STEP;
            if (!(minPeriod > 0) || double.IsInfinity(minPeriod)) minPeriod = DEFAULT_MIN_PERIOD;
            maxPropagationPasses = Math.Max(1, maxPropagationPasses);
            maxApplicationDepth = Math.Max(1, maxApplicationDepth);
            maxActivationDepth = Math.Max(1, maxActivationDepth);
        }

        private static double Positive(double _Value, string _Name)
        {
            if (!(_Value > 0) || double.IsInfinity(_Value))
                throw new ArgumentOutOfRangeException(_Name, _Value, "Must be a positive, finite number of seconds.");

            return _Value;
        }

        private static int AtLeastOne(int _Value, string _Name)
        {
            if (_Value < 1) throw new ArgumentOutOfRangeException(_Name, _Value, "Must be at least 1.");
            return _Value;
        }
    }
}
