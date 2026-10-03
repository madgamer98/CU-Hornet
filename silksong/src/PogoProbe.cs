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
}
