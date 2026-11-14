using LMK.Runtime.AbilitySystem;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Samples.AbilitySystem.Walkthroughs
{
    /// <summary>
    /// The tags of the sample's tag registry (<c>Registries/WalkthroughTags.tagregistry</c>), by ID. Generated constants
    /// would do the same; the sample writes them out so it works without code generation.
    /// </summary>
    public static class WalkthroughTags
    {
        public static readonly RegistryGuid Registry = RegistryGuid.Parse("6c4d7a10000000000000000000000001");

        private static GameplayTag Tag(int _Id, string _Path) => GameplayTag.FromId(Registry, Row(_Id), _Path);

        /// <summary> The ID of a row of the registry. </summary>
        public static RegistryGuid Row(int _Id) => RegistryGuid.Parse($"6c4d7a1000000000000000000000{_Id:x4}");

        public static readonly GameplayTag Ability = Tag(0x101, "Ability");
        public static readonly GameplayTag AbilityFireball = Tag(0x102, "Ability.Fireball");
        public static readonly GameplayTag AbilityInterruptible = Tag(0x103, "Ability.Interruptible");
        public static readonly GameplayTag AbilityThorns = Tag(0x104, "Ability.Thorns");
        public static readonly GameplayTag CooldownFireball = Tag(0x111, "Cooldown.Fireball");
        public static readonly GameplayTag Damage = Tag(0x120, "Damage");
        public static readonly GameplayTag DamageFire = Tag(0x121, "Damage.Fire");
        public static readonly GameplayTag DataDamage = Tag(0x131, "Data.Damage");
        public static readonly GameplayTag EventDeath = Tag(0x141, "Event.Death");
        public static readonly GameplayTag EventDamageTaken = Tag(0x143, "Event.Damage.Taken");
        public static readonly GameplayTag CueHitFire = Tag(0x152, "GameplayCue.Hit.Fire");
        public static readonly GameplayTag CueStatusBurning = Tag(0x154, "GameplayCue.Status.Burning");
        public static readonly GameplayTag StateCasting = Tag(0x161, "State.Casting");
        public static readonly GameplayTag StateDead = Tag(0x162, "State.Dead");
        public static readonly GameplayTag Status = Tag(0x170, "Status");
        public static readonly GameplayTag StatusBurning = Tag(0x171, "Status.Burning");
        public static readonly GameplayTag StatusDebuff = Tag(0x172, "Status.Debuff");
        public static readonly GameplayTag StatusDisarmed = Tag(0x173, "Status.Disarmed");
        public static readonly GameplayTag StatusStun = Tag(0x174, "Status.Stun");
    }



    /// <summary>
    /// The attributes of the sample's attribute registry (<c>Registries/WalkthroughAttributes.attrregistry</c>), by ID.
    /// </summary>
    public static class WalkthroughAttributes
    {
        public static readonly RegistryGuid Registry = RegistryGuid.Parse("6c4d7a20000000000000000000000001");

        private static GameplayAttribute Attribute(int _Id, string _Name) => GameplayAttribute.FromId(Registry, Row(_Id), _Name);

        /// <summary> The ID of a row of the registry. </summary>
        public static RegistryGuid Row(int _Id) => RegistryGuid.Parse($"6c4d7a2000000000000000000000{_Id:x4}");

        public static readonly GameplayAttribute Health = Attribute(0x201, "Vitals.Health");
        public static readonly GameplayAttribute MaxHealth = Attribute(0x202, "Vitals.MaxHealth");
        public static readonly GameplayAttribute Mana = Attribute(0x203, "Vitals.Mana");
        public static readonly GameplayAttribute MaxMana = Attribute(0x204, "Vitals.MaxMana");
        public static readonly GameplayAttribute AttackPower = Attribute(0x205, "Combat.AttackPower");
        public static readonly GameplayAttribute Armor = Attribute(0x206, "Combat.Armor");
        public static readonly GameplayAttribute IncomingDamage = Attribute(0x207, "Meta.IncomingDamage");
    }
}
