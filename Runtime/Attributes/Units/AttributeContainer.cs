using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// One attribute of one unit: its base value, its current value, and how many of the unit's sets list it.
    /// </summary>
    internal sealed class AttributeInstance
    {
        public readonly GameplayAttribute Attribute;
        public float BaseValue;
        public float CurrentValue;
        public int SetRefCount;

        // Handle in the active database, cached per database generation.
        public AttributeHandle Handle = AttributeHandle.None;

        public AttributeInstance(GameplayAttribute _Attribute)
        {
            Attribute = _Attribute;
        }

        public override string ToString() => $"{Attribute} base {BaseValue} current {CurrentValue}";
    }



    /// <summary>
    /// A unit's attributes: which it has, through its attribute sets; their base and current values; the
    /// modifiers on them; clamping to the registry bounds; and the propagation of changes to the attributes they bound.
    /// Only its <see cref="GameplayAbilitySystem"/> calls it, inside a mutation.
    /// </summary>
    internal sealed class AttributeContainer
    {
        private readonly GameplayAbilitySystem system;

        private readonly Dictionary<GameplayAttribute, AttributeInstance> instances = new();
        private readonly Dictionary<GameplayAttribute, Aggregator> aggregators = new();
        private readonly Dictionary<ModifierHandle, GameplayAttribute> modifierTargets = new();
        private readonly List<(AttributeSetDefinition Set, float Level)> sets = new();
        private readonly List<AttributeBehaviour> behaviours = new();

        private readonly Dictionary<int, AttributeInstance> byHandle = new();
        private GameplayAttributeDatabase handlesDatabase;
        private int handlesGeneration = -1;

        private readonly List<(AttributeInstance Instance, float Old, float New)> propagation = new();
        private int nestedChanges;

        /// <summary>
        /// Called for every propagated change of a current value, after the attributes it bounds were clamped:
        /// (attribute, old value, new value). Live attribute captures hook in here.
        /// </summary>
        internal event Action<GameplayAttribute, float, float> Propagated;

        /// <summary>
        /// The effect application changes are made for, reported as <see cref="AttributeChange.Cause"/>. Set by the
        /// effect system around its changes.
        /// </summary>
        internal GameplayEffectSpec Cause;

        public AttributeContainer(GameplayAbilitySystem _System)
        {
            system = _System;
        }

        private static GameplayAttributeDatabase Database => GameplayAttributeDatabase.Active;

        private AbilitySystemSettings Settings => system.World.Settings;

        public IReadOnlyList<AttributeSetDefinition> Sets => sets.ConvertAll(e => e.Set);

        public IEnumerable<GameplayAttribute> Attributes => instances.Keys;



        #region Queries
        public bool Has(GameplayAttribute _Attribute) => !_Attribute.IsNone && instances.ContainsKey(_Attribute);

        public float GetBase(GameplayAttribute _Attribute) => Find(_Attribute)?.BaseValue ?? 0f;

        public float GetCurrent(GameplayAttribute _Attribute) => Find(_Attribute)?.CurrentValue ?? 0f;

        /// <summary>
        /// The value an attribute capture reads: current, base, bonus (current minus base), or the
        /// aggregated value up to an evaluation channel. False if the unit lacks the attribute.
        /// </summary>
        public bool TryEvaluate(GameplayAttribute _Attribute, AttributeCalculationType _Type, int _Channel, out float _Value)
        {
            _Value = 0f;
            var instance = Find(_Attribute);
            if (instance == null) return false;

            switch (_Type)
            {
                case AttributeCalculationType.Base:
                    _Value = instance.BaseValue;
                    break;

                case AttributeCalculationType.Bonus:
                    _Value = instance.CurrentValue - instance.BaseValue;
                    break;

                case AttributeCalculationType.EvaluatedUpToChannel:
                    _Value = aggregators.TryGetValue(_Attribute, out var aggregator) && !IsMeta(instance)
                        ? aggregator.Evaluate(instance.BaseValue, out _, _Channel)
                        : instance.BaseValue;
                    break;

                default:
                    _Value = instance.CurrentValue;
                    break;
            }

            return true;
        }

        private AttributeInstance Find(GameplayAttribute _Attribute)
            => !_Attribute.IsNone && instances.TryGetValue(_Attribute, out var instance) ? instance : null;
        #endregion



        #region Sets
        /// <summary>
        /// Add a set's attributes in two passes, so a bound exists before the attribute it bounds: raw values first,
        /// then clamping in the clamp graph's order. No change events; one <c>OnAttributesInitialized</c>.
        /// </summary>
        public bool AddSet(AttributeSetDefinition _Set, float _Level)
        {
            if (_Set == null) return false;
            if (sets.Exists(e => e.Set == _Set))
            {
                Debug.LogWarning($"[{system.Name}] Attribute set \"{_Set.name}\" is already added. A set is added once.");
                return false;
            }

            var database = Database;
            sets.Add((_Set, _Level));
            RebuildBehaviours();

            // Pass 1: raw values, no clamping, no events.
            var created = new HashSet<AttributeInstance>();
            foreach (var entry in _Set.Entries)
            {
                var resolution = entry.attribute.Resolve();
                if (!resolution.IsLive)
                {
                    if (resolution.IsMissing)
                        GameplayAttributeDatabase.ReportUnresolved(entry.attribute, resolution.Status);
                    continue;
                }

                if (!instances.TryGetValue(entry.attribute, out var instance))
                {
                    instance = new AttributeInstance(entry.attribute)
                    {
                        BaseValue = entry.overrideDefault ? entry.initialValue.At(_Level) : database.GetDefault(resolution.Handle),
                    };
                    instances.Add(entry.attribute, instance);
                    created.Add(instance);
                }

                instance.SetRefCount++;
            }

            InvalidateHandles();
            EnsureHandles();

            // Pass 2: bounds before the attributes they bound.
            foreach (var handle in database != null ? database.InitializationOrder : Array.Empty<AttributeHandle>())
            {
                if (!byHandle.TryGetValue(handle.Index, out var instance) || !created.Contains(instance)) continue;

                if (!float.IsFinite(instance.BaseValue))
                {
                    Debug.LogError($"[{system.Name}] The initial value of \"{instance.Attribute}\" is not a finite number (non-finite). It starts at 0.");
                    instance.BaseValue = 0f;
                }

                instance.BaseValue = ClampToBounds(instance, instance.BaseValue);
                instance.CurrentValue = IsMeta(instance) ? instance.BaseValue : Compute(instance, instance.BaseValue, out _) ?? instance.BaseValue;
            }

            system.RaiseAttributesInitialized(_Set);
            return true;
        }

        /// <summary>
        /// Remove a set. Attributes no other set lists go; modifiers on them wait for the attribute to come back.
        /// </summary>
        public bool RemoveSet(AttributeSetDefinition _Set)
        {
            int index = sets.FindIndex(e => e.Set == _Set);
            if (index < 0) return false;

            sets.RemoveAt(index);
            RebuildBehaviours();

            foreach (var entry in _Set.Entries)
            {
                if (!instances.TryGetValue(entry.attribute, out var instance)) continue;
                if (--instance.SetRefCount > 0) continue;

                instances.Remove(entry.attribute);
                propagation.RemoveAll(e => e.Instance == instance);
            }

            InvalidateHandles();
            return true;
        }

        private void RebuildBehaviours()
        {
            behaviours.Clear();
            foreach (var (set, _) in sets)
                foreach (var behaviour in set.Behaviours)
                    if (behaviour != null)
                        behaviours.Add(behaviour);
        }
        #endregion



        #region Base Changes
        /// <summary>
        /// A direct base write: <c>PreBaseChange</c>, the finite check, clamping, then re-evaluation.
        /// </summary>
        public bool SetBase(GameplayAttribute _Attribute, float _Value)
        {
            var instance = Find(_Attribute);
            if (instance == null) return false;

            foreach (var behaviour in behaviours)
                Call(behaviour, b => b.PreBaseChange(system, _Attribute, ref _Value));

            if (!float.IsFinite(_Value))
            {
                Debug.LogError($"[{system.Name}] Rejected a non-finite base value for \"{_Attribute}\". It stays {instance.BaseValue}.");
                return false;
            }

            SetBaseInternal(instance, _Value);
            return true;
        }

        /// <summary>
        /// Execute one modifier on a base value: instant effects and each period of a periodic effect.
        /// Skipped when the unit lacks the attribute or a behaviour vetoes it. A meta attribute resets to its default
        /// once the <c>PostEffectExecute</c> behaviours have run.
        /// </summary>
        /// <returns> Whether the modifier was executed. </returns>
        public bool ExecuteModifier(GameplayAttribute _Attribute, ModifierOp _Op, float _Magnitude, GameplayEffectSpec _Spec = null)
        {
            var previousCause = Cause;
            Cause = _Spec ?? Cause;
            try
            {
                return ExecuteModifierInternal(_Attribute, _Op, _Magnitude, Cause);
            }
            finally
            {
                Cause = previousCause;
            }
        }

        private bool ExecuteModifierInternal(GameplayAttribute _Attribute, ModifierOp _Op, float _Magnitude, GameplayEffectSpec _Spec)
        {
            var instance = Find(_Attribute);
            if (instance == null) return false;

            var data = new ModCallbackData(_Attribute, _Op, _Magnitude, _Spec);
            foreach (var behaviour in behaviours)
            {
                bool proceed = true;
                Call(behaviour, b => proceed = b.PreEffectExecute(system, ref data));
                if (!proceed) return false;
            }

            float m = data.Magnitude;
            float newBase = data.Op switch
            {
                ModifierOp.AddBase or ModifierOp.AddFinal => instance.BaseValue + m,
                ModifierOp.MultiplyAdditive or ModifierOp.MultiplyCompound => instance.BaseValue * m,
                ModifierOp.DivideAdditive => m != 0f ? instance.BaseValue / m : instance.BaseValue,
                ModifierOp.Override => m,
                _ => instance.BaseValue,
            };

            foreach (var behaviour in behaviours)
                Call(behaviour, b => b.PreBaseChange(system, _Attribute, ref newBase));

            if (!float.IsFinite(newBase))
            {
                Debug.LogError($"[{system.Name}] Rejected a non-finite result for \"{_Attribute}\" ({data.Op} {m}). Its base stays {instance.BaseValue}.");
                return false;
            }

            SetBaseInternal(instance, newBase);

            foreach (var behaviour in behaviours)
                Call(behaviour, b => b.PostEffectExecute(system, in data));

            // The instance may be gone: a behaviour can remove its set.
            if (IsMeta(instance) && instances.ContainsValue(instance))
            {
                var database = Database;
                instance.BaseValue = instance.CurrentValue = database != null ? database.GetDefault(instance.Handle) : 0f;
            }

            return true;
        }

        private void SetBaseInternal(AttributeInstance _Instance, float _Value)
        {
            _Instance.BaseValue = ClampToBounds(_Instance, _Value);
            Reevaluate(_Instance);
        }
        #endregion



        #region Modifiers
        /// <summary>
        /// Add a modifier from a duration or infinite effect. Kept by attribute identity, so it waits while the unit
        /// lacks the attribute. Modifiers on meta attributes are kept but ignored.
        /// </summary>
        public ModifierHandle AddModifier(GameplayAttribute _Attribute, ModifierOp _Op, float _Magnitude, int _Channel, int _StackCount, Func<bool> _Qualifies)
        {
            if (_Attribute.IsNone) return ModifierHandle.Invalid;

            if (!aggregators.TryGetValue(_Attribute, out var aggregator))
                aggregators.Add(_Attribute, aggregator = new Aggregator());

            var handle = new ModifierHandle(system.World.NextHandle());
            aggregator.Add(new Aggregator.Modifier
            {
                Handle = handle,
                Op = _Op,
                Magnitude = _Magnitude,
                Channel = _Channel,
                StackCount = Math.Max(1, _StackCount),
                Qualifies = _Qualifies,
            });
            modifierTargets.Add(handle, _Attribute);

            ReevaluateIfPresent(_Attribute);
            return handle;
        }

        public bool RemoveModifier(ModifierHandle _Handle)
        {
            if (!modifierTargets.TryGetValue(_Handle, out var attribute)) return false;

            modifierTargets.Remove(_Handle);
            var aggregator = aggregators[attribute];
            aggregator.Remove(_Handle);
            if (aggregator.Count == 0)
                aggregators.Remove(attribute);

            ReevaluateIfPresent(attribute);
            return true;
        }

        public bool SetModifierMagnitude(ModifierHandle _Handle, float _Magnitude)
            => ChangeModifier(_Handle, e => e.Magnitude = _Magnitude);

        public bool SetModifierStackCount(ModifierHandle _Handle, int _StackCount)
            => ChangeModifier(_Handle, e => e.StackCount = Math.Max(1, _StackCount));

        private bool ChangeModifier(ModifierHandle _Handle, Action<Aggregator.Modifier> _Change)
        {
            if (!modifierTargets.TryGetValue(_Handle, out var attribute)) return false;

            _Change(aggregators[attribute].Find(_Handle));
            ReevaluateIfPresent(attribute);
            return true;
        }

        /// <summary>
        /// Evaluate every attribute again, e.g. after the unit's tags changed and qualifications may differ.
        /// </summary>
        public void ReevaluateAll()
        {
            foreach (var instance in instances.Values.ToList())
                if (instances.ContainsValue(instance))
                    Reevaluate(instance);
        }

        private void ReevaluateIfPresent(GameplayAttribute _Attribute)
        {
            var instance = Find(_Attribute);
            if (instance != null)
                Reevaluate(instance);
        }
        #endregion



        #region Evaluation
        /// <summary>
        /// Compute the current value again from the base and the modifiers. A change fires
        /// <c>PostAttributeChange</c> and <c>OnAttributeChanged</c> at once, and queues the re-clamping of the
        /// attributes it bounds.
        /// </summary>
        private void Reevaluate(AttributeInstance _Instance)
        {
            if (IsMeta(_Instance))
            {
                // A scratch value: no modifiers, no events.
                _Instance.CurrentValue = _Instance.BaseValue;
                return;
            }

            if (nestedChanges >= Settings.MaxApplicationDepth)
            {
                Debug.LogError($"[{system.Name}] Attribute changes nested {nestedChanges} deep while changing \"{_Instance.Attribute}\": " +
                               "an attribute cycle through behaviours or events. The change stops here.");
                return;
            }

            nestedChanges++;
            try
            {
                float old = _Instance.CurrentValue;
                float? computed = Compute(_Instance, _Instance.BaseValue, out bool zeroDivisor);

                if (zeroDivisor)
                    Debug.LogWarning($"[{system.Name}] The divide modifiers on \"{_Instance.Attribute}\" sum to a zero divisor. It counts as 1.");

                if (computed == null)
                {
                    Debug.LogError($"[{system.Name}] Rejected a non-finite value for \"{_Instance.Attribute}\". It stays {old}.");
                    return;
                }

                float value = computed.Value;
                foreach (var behaviour in behaviours)
                    Call(behaviour, b => b.PreAttributeChange(system, _Instance.Attribute, ref value));

                value = ClampToBounds(_Instance, value);
                if (!float.IsFinite(value) || value == old) return;

                _Instance.CurrentValue = value;

                foreach (var behaviour in behaviours)
                    Call(behaviour, b => b.PostAttributeChange(system, _Instance.Attribute, old, value));

                system.RaiseAttributeChanged(new AttributeChange(_Instance.Attribute, old, value, Cause));
                propagation.Add((_Instance, old, value));
            }
            finally
            {
                nestedChanges--;
            }
        }

        /// <summary>
        /// The aggregated, rounded value of a base, before behaviours and clamping. <see langword="null"/> if not finite.
        /// </summary>
        private float? Compute(AttributeInstance _Instance, float _Base, out bool _ZeroDivisor)
        {
            _ZeroDivisor = false;
            float value = aggregators.TryGetValue(_Instance.Attribute, out var aggregator)
                ? aggregator.Evaluate(_Base, out _ZeroDivisor)
                : _Base;

            if (!float.IsFinite(value)) return null;

            var database = Database;
            if (database != null && database.IsInteger(_Instance.Handle))
                value = (float)Math.Round(value, MidpointRounding.AwayFromZero);

            return value;
        }

        /// <summary>
        /// Keep a value inside the registry bounds. A bound naming another attribute uses that attribute's current
        /// value on this unit; a unit without it has no such bound.
        /// </summary>
        private float ClampToBounds(AttributeInstance _Instance, float _Value)
        {
            var database = Database;
            EnsureHandles();
            if (database == null || !_Instance.Handle.IsValid) return _Value;

            if (TryGetLimit(database.GetMax(_Instance.Handle), out float max) && _Value > max) _Value = max;
            if (TryGetLimit(database.GetMin(_Instance.Handle), out float min) && _Value < min) _Value = min;

            return _Value;
        }

        private bool TryGetLimit(AttributeLimit _Limit, out float _Value)
        {
            switch (_Limit.Kind)
            {
                case AttributeBoundKind.Constant:
                    _Value = _Limit.Constant;
                    return true;

                case AttributeBoundKind.Attribute when byHandle.TryGetValue(_Limit.Handle.Index, out var bound):
                    _Value = bound.CurrentValue;
                    return true;

                default:
                    _Value = 0f;
                    return false;
            }
        }

        private bool IsMeta(AttributeInstance _Instance)
        {
            var database = Database;
            EnsureHandles();
            return database != null && database.IsMeta(_Instance.Handle);
        }
        #endregion



        #region Propagation
        /// <summary>
        /// Whether changes wait to be propagated.
        /// </summary>
        public bool HasPendingPropagation => propagation.Count > 0;

        /// <summary>
        /// One pass of propagation: each queued change clamps the attributes it bounds again,
        /// keeping their ratio when asked, and notifies live captures. Their own changes queue the next pass. The unit
        /// runs the passes and their limit (<see cref="GameplayAbilitySystem.FlushPropagation"/>).
        /// </summary>
        public void PropagateOnce()
        {
            var batch = propagation.ToList();
            propagation.Clear();

            foreach (var (instance, old, value) in batch)
            {
                if (!instances.ContainsValue(instance)) continue;

                OnCurrentChanged(instance, old, value);
                Propagated?.Invoke(instance.Attribute, old, value);
            }
        }

        /// <summary>
        /// Drop the queued changes, and name their attributes for the error that stops them.
        /// </summary>
        public string ClearPropagation()
        {
            string involved = string.Join(", ", propagation.Select(e => $"\"{e.Instance.Attribute}\"").Distinct());
            propagation.Clear();
            return involved;
        }
        private void OnCurrentChanged(AttributeInstance _Bound, float _Old, float _New)
        {
            var database = Database;
            EnsureHandles();
            if (database == null || !_Bound.Handle.IsValid) return;

            foreach (var handle in database.GetDependents(_Bound.Handle))
            {
                if (!byHandle.TryGetValue(handle.Index, out var dependent)) continue;

                float newBase = dependent.BaseValue;
                if (database.GetOnMaxChange(handle) == AttributeMaxChange.KeepRatio && database.GetMax(handle).Handle == _Bound.Handle && _Old > 0f)
                    newBase = dependent.BaseValue * _New / _Old;

                SetBaseInternal(dependent, newBase);
            }
        }
        #endregion



        #region Handles
        private void InvalidateHandles() => handlesGeneration = -1;

        /// <summary>
        /// Resolve every instance's handle in the active database, once per database.
        /// </summary>
        private void EnsureHandles()
        {
            var database = Database;
            int generation = GameplayAttributeDatabase.Generation;
            if (handlesGeneration == generation && handlesDatabase == database) return;

            handlesGeneration = generation;
            handlesDatabase = database;
            byHandle.Clear();

            foreach (var instance in instances.Values)
            {
                instance.Handle = database != null ? database.Resolve(instance.Attribute.RegistryId, instance.Attribute.Id).Handle : AttributeHandle.None;
                if (instance.Handle.IsValid)
                    byHandle[instance.Handle.Index] = instance;
            }
        }
        #endregion



        private void Call(AttributeBehaviour _Behaviour, Action<AttributeBehaviour> _Hook)
        {
            try
            {
                _Hook(_Behaviour);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
