using UnityEngine;

namespace Taiyun.SuckTheWater.Game
{
    /// <summary>
    /// Damage receiver that applies multipliers and forwards to Health component.
    /// </summary>
    /// <remarks>
    /// Searches for Health on self or parent. Supports self-damage reduction.
    /// </remarks>
    public class Damageable : MonoBehaviour
    {
        [Tooltip("Multiplier to apply to the received damage")]
        public float DamageMultiplier = 1f;

        [Range(0, 1)] [Tooltip("Multiplier to apply to self damage")]
        public float SensibilityToSelfdamage = 0.5f;

        public Health Health { get; private set; }

        void Awake()
        {
            // find the health component either at the same level, or higher in the hierarchy
            Health = GetComponent<Health>();
            if (!Health)
            {
                Health = GetComponentInParent<Health>();
            }
        }

        public void InflictDamage(float damage, bool isExplosionDamage, GameObject damageSource)
        {
            TryApplyAuthoritativeDamage(damage, isExplosionDamage, damageSource, out _, out _);
        }

        /// <summary>
        /// Unified authoritative damage application entrypoint.
        /// Returns the final applied damage and whether the target died from this application.
        /// </summary>
        public bool TryApplyAuthoritativeDamage(float damage, bool isExplosionDamage, GameObject damageSource,
            out float appliedDamage, out bool killed)
        {
            appliedDamage = 0f;
            killed = false;

            if (Health == null || damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage))
            {
                return false;
            }

            float healthBefore = Health.CurrentHealth;
            if (healthBefore <= 0f)
            {
                killed = true;
                return false;
            }

            float totalDamage = damage;

            // skip the crit multiplier if it's from an explosion
            if (!isExplosionDamage)
            {
                totalDamage *= DamageMultiplier;
            }

            // potentially reduce damages if inflicted by self
            if (damageSource != null && Health.gameObject == damageSource)
            {
                totalDamage *= SensibilityToSelfdamage;
            }

            totalDamage = Mathf.Max(0f, totalDamage);
            if (totalDamage <= 0f)
            {
                return false;
            }

            Health.TakeDamage(totalDamage, damageSource);

            appliedDamage = Mathf.Max(0f, healthBefore - Health.CurrentHealth);
            killed = Health.CurrentHealth <= 0f;
            return appliedDamage > 0f;
        }
    }
}
