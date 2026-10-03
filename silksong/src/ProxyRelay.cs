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
            link.PushEvent(Proto.EventHitEntity, EntityId, EntityProxies.HitDamage, 0f, 0f);
            Plugin.Log.LogInfo("Hit entity " + EntityId + " for " + EntityProxies.HitDamage);
        }
    }
}
