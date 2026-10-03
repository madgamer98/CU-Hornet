using GlobalEnums;
using HarmonyLib;

namespace HornetExporter
{
    /// <summary>
    /// S4 phase 3 evidence hook: log whenever Hornet's real TakeDamage path runs. A proxy
    /// DamageHero (CU biter) reaching this proves actor -> Hornet damage works; the hp delta shows
    /// whether the hit actually landed or was absorbed by i-frames.
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
            Plugin.Log.LogInfo("HURT! HeroController.TakeDamage dmg=" + __2 +
                               " hazard=" + __3 + " hp " + __state + "->" + now);
        }
    }
}
