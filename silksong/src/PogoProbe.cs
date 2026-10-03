using HarmonyLib;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// S4 evidence hook: log whenever Hornet's genuine down-spike bounce fires. If this prints after
    /// a proxy is present under her, the CU→Silksong pogo path works.
    /// </summary>
    [HarmonyPatch(typeof(HeroController), "DownspikeBounce")]
    internal static class DownspikeBouncePatch
    {
        private static void Postfix()
        {
            Plugin.Log.LogInfo("POGO! DownspikeBounce fired.");
        }
    }

    /// <summary>
    /// HK caches the ground probe and only re-runs it when the hero moves; on mirrored terrain the
    /// collision arrives with a stale "not grounded" cache, so OnCollisionEnter2D never fires the
    /// HeroCtrl-Landed event and the FSM never refreshes cState.onGround (first jump becomes a double
    /// jump). Force the probe before the handler reads it.
    /// </summary>
    [HarmonyPatch(typeof(HeroController), "OnCollisionEnter2D")]
    internal static class ForceGroundOnCollisionPatch
    {
        private static void Prefix(HeroController __instance)
        {
            __instance.CheckTouchingGround(true);
        }
    }
}
