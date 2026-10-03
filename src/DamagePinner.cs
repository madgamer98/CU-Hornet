using UnityEngine;

namespace HornetInCasualties
{
    /// <summary>
    /// S5 2C: while the mirror drives the body, restore CU's damage fields every frame so no CU
    /// damage source (spikes, biters, bleeding, infection, ...) can accumulate - Silksong's health is
    /// the authority while mirrored. The survival meters (hunger/thirst/temperature) keep running;
    /// only the damage they cause is reverted.
    /// </summary>
    internal static class DamagePinner
    {
        public static void Pin(Body body)
        {
            if (body == null)
            {
                return;
            }
            body.brainHealth = 100f;
            if (body.bloodVolume < 100f)
            {
                body.bloodVolume = 100f;
            }
            body.venomTotal = 0f;
            body.internalBleeding = 0f;
            body.hemothorax = 0f;
            body.traumaAmount = 0f;
            body.radiationSickness = 0f;
            body.sicknessAmount = 0f;
            PinLimbs(body.limbs);
            PinLimbs(body.legLimbs);
        }

        private static void PinLimbs(Limb[] limbs)
        {
            if (limbs == null)
            {
                return;
            }
            for (int i = 0; i < limbs.Length; i++)
            {
                Limb l = limbs[i];
                if (l == null)
                {
                    continue;
                }
                l.skinHealth = 100f;
                l.muscleHealth = 100f;
                l.bleedAmount = 0f;
                l.infectionAmount = 0f;
                l.pain = 0f;
            }
        }
    }
}
