using GlobalEnums;
using HarmonyLib;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// S4 phase 3 evidence hook: log whenever Hornet's real TakeDamage path runs.
    /// </summary>
    [HarmonyPatch(typeof(HeroController), "TakeDamage")]
    internal static class HeroTakeDamagePatch
    {
        private static void Prefix(out int __state)
        {
            PlayerData pd = PlayerData.instance;
            __state = pd != null ? pd.health : -1;
        }

        private static void Postfix(int __state, int __2, HazardType __3)
        {
            if (__2 <= 0)
            {
                return;
            }
            PlayerData pd = PlayerData.instance;
            int now = pd != null ? pd.health : -1;
            int max = pd != null ? pd.maxHealth : -1;
            Plugin.Log.LogInfo("HURT! HeroController.TakeDamage dmg=" + __2 +
                               " hazard=" + __3 + " hp " + __state + "->" + now + "/" + max);
        }
    }

    /// <summary>
    /// Catch-all evidence hook: <see cref="PlayerData.TakeHealth"/> is the single place health is
    /// actually reduced, whatever the source.
    /// </summary>
    [HarmonyPatch(typeof(PlayerData), "TakeHealth")]
    internal static class PlayerDataTakeHealthPatch
    {
        private static void Prefix(PlayerData __instance, out int __state)
        {
            __state = __instance.health;
        }

        private static void Postfix(PlayerData __instance, int __state, int __0)
        {
            if (__state == __instance.health)
            {
                return;
            }
            Plugin.Log.LogInfo("HEALTH! TakeHealth amount=" + __0 + " hp " + __state + "->" +
                               __instance.health + "/" + __instance.maxHealth);
        }
    }

    /// <summary>Quiet probes that only print if a non-TakeDamage source fires.</summary>
    [HarmonyPatch(typeof(HeroController), "TakeChompDamage")]
    internal static class ChompProbe
    {
        private static void Postfix()
        {
            Plugin.Log.LogInfo("SRC! HeroController.TakeChompDamage");
        }
    }

    [HarmonyPatch(typeof(HeroController), "DoSpecialDamage")]
    internal static class SpecialDamageProbe
    {
        private static void Postfix(int __0, string __2)
        {
            Plugin.Log.LogInfo("SRC! HeroController.DoSpecialDamage amount=" + __0 + " event=" + __2);
        }
    }

    [HarmonyPatch(typeof(HeroController), "TakeHealth")]
    internal static class HeroTakeHealthProbe
    {
        private static void Postfix(int __0)
        {
            Plugin.Log.LogInfo("SRC! HeroController.TakeHealth amount=" + __0);
        }
    }
}
