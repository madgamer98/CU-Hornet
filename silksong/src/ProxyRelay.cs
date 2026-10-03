using HornetPassthrough;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// S4 phase 2: attached to each CU entity proxy. When Hornet's attack collider (layer 17,
    /// HERO_ATTACK) overlaps, push a HitEntity event so CU can damage the real actor. A short
    /// cooldown collapses the multi-step trigger contacts of one swing into a single hit.
    /// </summary>
    internal class ProxyRelay : MonoBehaviour
    {
        public int EntityId;

        private float _nextHit;

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryHit(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TryHit(other);
        }

        private void TryHit(Collider2D other)
        {
            if (other == null || other.gameObject.layer != EntityProxies.AttackLayer)
            {
                return;
            }
            if (Time.time < _nextHit)
            {
                return;
            }
            _nextHit = Time.time + 0.3f;

            PassthroughLink link = LiveLink.Instance != null ? LiveLink.Instance.Link : null;
            if (link == null)
            {
                return;
            }
            int damage = Mathf.RoundToInt(ReadAttackDamage(other, EntityProxies.HitDamage));
            link.PushEvent(Proto.EventHitEntity, EntityId, damage, 0f, 0f);
            Plugin.Log.LogInfo("Hit entity " + EntityId + " for " + damage);
        }

        /// <summary>
        /// S4 follow-up: report Hornet's real nail damage instead of a fixed value. The attack
        /// collider's DamageEnemies knows whether it uses the nail (and its multiplier) or a flat
        /// value; fall back to the constant if the component cannot be found.
        /// </summary>
        private static float ReadAttackDamage(Collider2D attack, float fallback)
        {
            DamageEnemies de = attack.GetComponentInParent<DamageEnemies>();
            if (de == null)
            {
                return fallback;
            }
            if (de.useNailDamage)
            {
                PlayerData pd = PlayerData.instance;
                if (pd != null)
                {
                    int dmg = Mathf.RoundToInt((float)pd.nailDamage * de.nailDamageMultiplier);
                    if (dmg > 0)
                    {
                        return dmg;
                    }
                }
            }
            else if (de.damageDealt > 0)
            {
                return de.damageDealt;
            }
            return fallback;
        }
    }
}
