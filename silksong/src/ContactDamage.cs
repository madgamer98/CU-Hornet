using GlobalEnums;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// S4 phase 3 contact damage, explicit form. The previous approach (a proxy <see cref="DamageHero"/>
    /// read by HeroBox) cascaded: HeroBox's buffered-hit machinery re-armed our DamageHero every physics
    /// step, draining Hornet in one contact and bypassing her i-frames.
    ///
    /// Instead we call <see cref="HeroController.TakeDamage"/> ourselves, exactly once per HeroBox entry,
    /// and only when she can actually take damage. That keeps native recoil/i-frames/clip but guarantees
    /// one hit per contact. Pogo is unaffected: <c>HeroDownAttack</c> bounces off the proxy with or
    /// without a DamageHero, and outgoing damage is handled by <see cref="ProxyRelay"/>.
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
            if (!Contact || other == null || other.GetComponent<HeroBox>() == null)
            {
                return; // only Hornet's hurtbox, not her attack colliders
            }
            if (Time.time < _nextArm)
            {
                return;
            }
            HeroController hero = HeroController.instance;
            if (hero == null || !hero.CanTakeDamage())
            {
                return; // she is already recoiling/invulnerable; don't waste the contact
            }
            _nextArm = Time.time + ReArmCooldown;
            CollisionSide side = transform.position.x > hero.transform.position.x
                ? CollisionSide.left
                : CollisionSide.right;
            hero.TakeDamage(gameObject, side, Damage, HazardType.ENEMY);
        }
    }
}
