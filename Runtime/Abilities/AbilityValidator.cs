using System.Collections.Generic;
using System.Linq;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// What is wrong with an ability asset.
    /// </summary>
    public enum AbilityIssueCode
    {
        /// <summary> The ability has no logic. </summary>
        NoLogic,

        /// <summary> The cost effect isn't instant. </summary>
        CostNotInstant,

        /// <summary> The cooldown effect isn't a duration effect, or grants no tags. </summary>
        InvalidCooldown,

        /// <summary> An OwnedTagAdded trigger's tag is one of the ability's own activation owned tags. A warning. </summary>
        TriggerOnOwnTag,
    }



    public readonly struct AbilityIssue
    {
        public readonly AbilityIssueCode Code;
        public readonly EffectIssueSeverity Severity;
        public readonly string Message;

        public AbilityIssue(AbilityIssueCode _Code, EffectIssueSeverity _Severity, string _Message)
        {
            Code = _Code;
            Severity = _Severity;
            Message = _Message;
        }

        public override string ToString() => $"{Severity}: {Message}";
    }



    public sealed class AbilityValidationResult
    {
        private readonly List<AbilityIssue> issues = new();

        public IReadOnlyList<AbilityIssue> Issues => issues;

        public bool HasErrors => issues.Any(e => e.Severity == EffectIssueSeverity.Error);

        public bool HasError(AbilityIssueCode _Code) => issues.Any(e => e.Code == _Code && e.Severity == EffectIssueSeverity.Error);

        public bool HasWarning(AbilityIssueCode _Code) => issues.Any(e => e.Code == _Code && e.Severity == EffectIssueSeverity.Warning);

        internal void Add(AbilityIssueCode _Code, EffectIssueSeverity _Severity, string _Message) => issues.Add(new AbilityIssue(_Code, _Severity, _Message));

        public override string ToString() => string.Join("\n", issues);
    }



    /// <summary>
    /// Checks an ability asset for mistakes. The <c>NonInstanced</c> rule is gone with the policy.
    /// </summary>
    public static class AbilityValidator
    {
        public static AbilityValidationResult Validate(GameplayAbility _Ability)
        {
            var result = new AbilityValidationResult();
            if (_Ability == null) return result;

            string name = _Ability.name;

            if (_Ability.logic == null)
                result.Add(AbilityIssueCode.NoLogic, EffectIssueSeverity.Error, $"\"{name}\" has no logic, so it can't activate.");

            if (_Ability.costEffect != null && _Ability.costEffect.DurationPolicy != DurationPolicy.Instant)
                result.Add(AbilityIssueCode.CostNotInstant, EffectIssueSeverity.Error,
                    $"\"{name}\": the cost effect \"{_Ability.costEffect.name}\" isn't instant. A cost is paid once, on commit.");

            var cooldown = _Ability.cooldownEffect;
            if (cooldown != null && (cooldown.DurationPolicy != DurationPolicy.HasDuration || cooldown.GetGrantedTags().IsEmpty))
                result.Add(AbilityIssueCode.InvalidCooldown, EffectIssueSeverity.Error,
                    $"\"{name}\": the cooldown effect \"{cooldown.name}\" must be a duration effect that grants tags: the tags are what the cooldown checks.");

            foreach (var trigger in _Ability.triggers)
                if (trigger.source == AbilityTriggerSource.OwnedTagAdded && !trigger.tag.IsNone && _Ability.activationOwnedTags.HasTagExact(trigger.tag))
                    result.Add(AbilityIssueCode.TriggerOnOwnTag, EffectIssueSeverity.Warning,
                        $"\"{name}\" is triggered by \"{trigger.tag}\", which its own activation adds: it can only trigger when activated another way.");

            return result;
        }
    }
}
