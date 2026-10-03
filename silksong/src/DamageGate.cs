using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// S5 2E: while the mirror is active, Silksong's own world must not be able to hurt Hornet - only
    /// our proxied CU actors, which carry <see cref="ContactDamage"/>. Silksong's damage intake is
    /// centralized, so gating these is thorough regardless of what the streamed scene contains (far
    /// more robust than trying to disable hazards/enemies scene by scene).
    /// </summary>
    internal static class DamageGate
    {
        private static bool Mirrored => TerrainMirror.Active;

        [HarmonyPatch(typeof(HeroController), "TakeDamage")]
        internal static class TakeDamagePatch
        {
            private static bool Prefix(GameObject go)
            {
                if (!Mirrored)
                {
                    return true;
                }
                // Only our CU proxies may deal contact damage; block enemies/hazards routed here.
                return go != null && go.GetComponent<ContactDamage>() != null;
            }
        }

        [HarmonyPatch(typeof(HeroController), "DoSpecialDamage")]
        internal static class DoSpecialDamagePatch
        {
            private static bool Prefix()
            {
                return !Mirrored;
            }
        }

        [HarmonyPatch(typeof(HeroController), "TakeFrostDamage")]
        internal static class TakeFrostDamagePatch
        {
            private static bool Prefix()
            {
                return !Mirrored;
            }
        }

        [HarmonyPatch(typeof(HeroController), "TakeChompDamage")]
        internal static class TakeChompDamagePatch
        {
            private static bool Prefix()
            {
                return !Mirrored;
            }
        }

        [HarmonyPatch(typeof(HeroController), "DieFromHazard")]
        internal static class DieFromHazardPatch
        {
            // These are coroutines: returning false alone would hand the caller a null IEnumerator and
            // StartCoroutine would throw, so return an empty one instead.
            private static bool Prefix(ref IEnumerator __result)
            {
                if (!Mirrored)
                {
                    return true;
                }
                __result = Empty();
                return false;
            }
        }

        [HarmonyPatch(typeof(HeroController), "HazardRespawn")]
        internal static class HazardRespawnPatch
        {
            private static bool Prefix(ref IEnumerator __result)
            {
                if (!Mirrored)
                {
                    return true;
                }
                __result = Empty();
                return false;
            }
        }

        private static IEnumerator Empty()
        {
            yield break;
        }
    }
}
