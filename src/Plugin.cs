using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HornetInCasualties
{
    /// <summary>
    /// Entry point. Loads Hornet's sprites (Stage 1: placeholder) and attaches a
    /// <see cref="HornetAvatar"/> to the player's <see cref="Body"/>.
    /// </summary>
    [BepInPlugin(Guid, "Hornet in Casualties", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.cuhornet.hornetincasualties";
        public const string Version = "0.0.1";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        internal static ConfigEntry<bool> Enable;
        internal static ConfigEntry<bool> HideVanillaBody;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Enable = Config.Bind("General", "Enable", true, "Draw Hornet on the player.");
            HideVanillaBody = Config.Bind("General", "HideVanillaBody", false,
                "Hide the vanilla experiment's body sprites while Hornet is shown.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            Log.LogInfo("Hornet in Casualties v" + Version + " loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }

    /// <summary>Late in the frame, make sure the player has a Hornet avatar.</summary>
    [HarmonyPatch(typeof(PlayerCamera), "Update")]
    internal static class PlayerCameraUpdatePatch
    {
        private static void Postfix(PlayerCamera __instance)
        {
            if (!Plugin.Enable.Value)
            {
                return;
            }

            Body body = __instance != null ? __instance.body : null;
            if (body == null)
            {
                return;
            }

            HornetAvatar.Ensure(body);
        }
    }
}
