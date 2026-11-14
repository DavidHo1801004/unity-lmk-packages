using System;

using UnityEngine;

using LMK.Runtime.Attributes;
using LMK.Runtime.GameplayTags;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// Reacts to a unit's attribute changes, in place of UE's <c>UAttributeSet</c> virtual functions.
    /// Inline-instanced on an <see cref="AttributeSetDefinition"/>, so one instance serves every unit that uses the
    /// set: keep no per-unit state in fields.
    /// </summary>
    /// <remarks>
    /// Every hook is called for every attribute of the unit; check the attribute first. Behaviours run in set order,
    /// then list order. Changes made from a hook go through the unit's mutators as usual.
    /// </remarks>
    [Serializable, CanReferenceInline]
    public abstract class AttributeBehaviour
    {
        /// <summary>
        /// Before a current value changes: may adjust it. Clamping to the registry bounds follows.
        /// </summary>
        public virtual void PreAttributeChange(GameplayAbilitySystem _System, GameplayAttribute _Attribute, ref float _NewCurrent) { }

        /// <summary>
        /// Before a base value changes: may adjust it. Clamping to the registry bounds follows.
        /// </summary>
        public virtual void PreBaseChange(GameplayAbilitySystem _System, GameplayAttribute _Attribute, ref float _NewBase) { }

        /// <summary>
        /// Before an executed modifier changes a base value. Return false to veto it; may change its magnitude.
        /// </summary>
        public virtual bool PreEffectExecute(GameplayAbilitySystem _System, ref ModCallbackData _Data) => true;

        /// <summary>
        /// After an executed modifier changed a base value. A meta attribute still holds its value here, and resets
        /// to its default right after.
        /// </summary>
        public virtual void PostEffectExecute(GameplayAbilitySystem _System, in ModCallbackData _Data) { }

        /// <summary>
        /// After a current value changed. Never called for meta attributes.
        /// </summary>
        public virtual void PostAttributeChange(GameplayAbilitySystem _System, GameplayAttribute _Attribute, float _OldValue, float _NewValue) { }
    }



    /// <summary>
    /// Whether <see cref="TransferMetaAttribute"/> adds the meta value to its target or subtracts it.
    /// </summary>
    public enum TransferMode
    {
        Subtract,
        Add,
    }



    /// <summary>
    /// The usual damage-to-health step: when an execution writes to the meta attribute <see cref="from"/>, its value
    /// is applied to the base of <see cref="to"/>, and <see cref="eventTag"/> is sent with the amount.
    /// </summary>
    [Serializable]
    public sealed class TransferMetaAttribute : AttributeBehaviour
    {
        [Tooltip("The meta attribute executions write to, e.g. Meta.IncomingDamage.")]
        public GameplayAttribute from;

        [Tooltip("The attribute whose base receives the value, e.g. Vitals.Health.")]
        public GameplayAttribute to;

        public TransferMode mode = TransferMode.Subtract;

        [Tooltip("Optional gameplay event sent to the unit with the amount, e.g. Event.Damaged.")]
        public GameplayTag eventTag;

        public override void PostEffectExecute(GameplayAbilitySystem _System, in ModCallbackData _Data)
        {
            if (_Data.Attribute != from || !_System.HasAttribute(to)) return;

            float amount = _System.GetCurrent(from);
            if (amount == 0f) return;

            float current = _System.GetBase(to);
            _System.SetAttributeBase(to, mode == TransferMode.Subtract ? current - amount : current + amount);

            if (!eventTag.IsNone)
                _System.HandleGameplayEvent(eventTag, new GameplayEventData(eventTag, _System, _System, amount));
        }
    }



    /// <summary>
    /// How <see cref="SendEventOnThreshold"/> compares the value to its threshold.
    /// </summary>
    public enum ThresholdComparison
    {
        LessOrEqual,
        Less,
        GreaterOrEqual,
        Greater,
    }



    /// <summary>
    /// Sends a gameplay event when an attribute's current value crosses a threshold, such as <c>Event.Death</c> at
    /// Health ≤ 0. Fires on the change that crosses it, not while it stays across.
    /// </summary>
    [Serializable]
    public sealed class SendEventOnThreshold : AttributeBehaviour
    {
        public GameplayAttribute attribute;

        public ThresholdComparison comparison = ThresholdComparison.LessOrEqual;

        public float threshold;

        public GameplayTag eventTag;

        [Tooltip("Send the event only the first time, per unit.")]
        public bool once;

        public override void PostAttributeChange(GameplayAbilitySystem _System, GameplayAttribute _Attribute, float _OldValue, float _NewValue)
        {
            if (_Attribute != attribute || eventTag.IsNone) return;
            if (Passes(_OldValue) || !Passes(_NewValue)) return;
            if (once && !_System.TryMarkOnce(this)) return;

            _System.HandleGameplayEvent(eventTag, new GameplayEventData(eventTag, _System, _System, _NewValue));
        }

        private bool Passes(float _Value) => comparison switch
        {
            ThresholdComparison.LessOrEqual => _Value <= threshold,
            ThresholdComparison.Less => _Value < threshold,
            ThresholdComparison.GreaterOrEqual => _Value >= threshold,
            ThresholdComparison.Greater => _Value > threshold,
            _ => false,
        };
    }
}
