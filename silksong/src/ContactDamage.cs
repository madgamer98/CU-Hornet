using GlobalEnums;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// S4 phase 3 contact damage, explicit form. The previous approach (a proxy <see cref="DamageHero"/>
    /// read by HeroBox) cascaded: HeroBox's buffered-hit machinery re-armed our DamageHero every physics
    /// step, draining Hornet in one contact and bypassing her i-frames.
    ///
    /// Instead we call <see cref="HeroController.TakeDamage"/> ourselves, once per contact, and only
    /// when she can actually take damage. That keeps native recoil/i-frames/clip but guarantees one hit
    /// per contact. A genuine pogo (down-slash / down-spike bounce) bounces and is explicitly exempt
    /// from contact damage, while a plain landing still hurts. Outgoing damage is handled by
    /// <see cref="ProxyRelay"/>.
    /// </summary>
    internal class ContactDamage : MonoBehaviour
    {
        /// <summary>True while CU flagged this actor as dealing contact damage.</summary>
        public bool Contact;
        public int Damage = EntityProxies.ContactDamageAmount;
        public float ReArmCooldown = 0.6f;

        private float _nextArm;

        public void SetContact(bool contact)
        {
            Contact = contact;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryHit(other);
        }

        // Also fire on Stay: if Hornet is already overlapping the proxy when it is (re)activated, or
        // the proxy is repositioned onto her every 0.25s, Enter never fires and she would take no
        // damage. The re-arm cooldown keeps one contact to one hit.
        private void OnTriggerStay2D(Collider2D other)
        {
            TryHit(other);
        }

        private void TryHit(Collider2D other)
        {
            if (!Contact || other == null || other.GetComponent<HeroBox>() == null)
            {
                return; // only Hornet's hurtbox, not her attack colliders
            }
            HeroController hero = HeroController.instance;
            if (hero == null || !hero.CanTakeDamage() || Time.time < _nextArm)
            {
                return; // she is already recoiling/invulnerable; don't waste the contact
            }
            // A real pogo (down-slash / down-spike bounce) bounces and must NOT hurt her. A plain
            // landing is not a pogo and should.
            HeroControllerStates st = hero.cState;
            if (st != null && (st.downAttacking || st.downSpikeBouncing || st.downSpikeAntic))
            {
                return;
            }
            _nextArm = Time.time + ReArmCooldown;
            CollisionSide side = transform.position.x > hero.transform.position.x
                ? CollisionSide.left
                : CollisionSide.right;
            hero.TakeDamage(gameObject, side, Damage, HazardType.ENEMY);
        }
    }
}
