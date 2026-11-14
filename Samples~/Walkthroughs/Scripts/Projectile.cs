using UnityEngine;

using LMK.Runtime.AbilitySystem;

namespace LMK.Samples.AbilitySystem.Walkthroughs
{
    /// <summary>
    /// The fireball's projectile: flies to its target and applies the specs it carries on impact, even if its caster has
    /// died since.
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        public float speed = 15f;

        private GameplayAbilitySystem target;
        private GameplayEffectSpec damage, burn;

        /// <summary>
        /// Launch a projectile from <paramref name="_From"/>, or hit at once without a prefab or avatars.
        /// </summary>
        public static void Launch(Projectile _Prefab, GameObject _From, GameplayAbilitySystem _Target, GameplayEffectSpec _Damage, GameplayEffectSpec _Burn)
        {
            if (_Prefab == null || _From == null || _Target.ActorInfo.AvatarGameObject == null)
            {
                Hit(_Target, _Damage, _Burn);
                return;
            }

            var projectile = Instantiate(_Prefab, _From.transform.position + Vector3.up, Quaternion.identity);
            projectile.target = _Target;
            projectile.damage = _Damage;
            projectile.burn = _Burn;
        }

        private void Update()
        {
            var avatar = target != null && !target.IsShuttingDown ? target.ActorInfo.AvatarGameObject : null;
            if (avatar == null)
            {
                Destroy(gameObject);
                return;
            }

            var destination = avatar.transform.position + Vector3.up;
            transform.position = Vector3.MoveTowards(transform.position, destination, speed * Time.deltaTime);
            if ((transform.position - destination).sqrMagnitude > 0.01f) return;

            Hit(target, damage, burn);
            Destroy(gameObject);
        }

        private static void Hit(GameplayAbilitySystem _Target, GameplayEffectSpec _Damage, GameplayEffectSpec _Burn)
        {
            if (_Target == null || _Target.IsShuttingDown) return;

            _Target.ApplySpecToSelf(_Damage);
            _Target.ApplySpecToSelf(_Burn);
        }
    }
}
