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
        internal static ConfigEntry<bool> DebugKeys;
        internal static ConfigEntry<float> AvatarScale;
        internal static ConfigEntry<float> AvatarOffsetX;
        internal static ConfigEntry<float> AvatarOffsetY;
        internal static ConfigEntry<float> AvatarPpu;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Enable = Config.Bind("General", "Enable", true, "Draw Hornet on the player.");
            HideVanillaBody = Config.Bind("General", "HideVanillaBody", true,
                "Hide the vanilla experiment's body sprites while Hornet is shown.");
            DebugKeys = Config.Bind("Dev", "DebugKeys", true,
                "F10 starts a run from the menu (dev oracle). F11 toggles the vanilla body.");
            AvatarScale = Config.Bind("Avatar", "Scale", 1.0f, "Hornet sprite scale.");
            AvatarOffsetX = Config.Bind("Avatar", "OffsetX", 0f, "Hornet horizontal offset (in sprite units).");
            AvatarOffsetY = Config.Bind("Avatar", "OffsetY", 0f, "Hornet vertical offset (in sprite units).");
            AvatarPpu = Config.Bind("Avatar", "Ppu", 64f, "Pixels per unit for the baked Hornet frames.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            if (DebugKeys.Value)
            {
                var go = new GameObject("HornetInCasualties.Debug");
                DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<DebugHotkeys>();
            }

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

    /// <summary>
    /// Dev-only oracle: reach a controlled test area without clicking through menus.
    /// F9 (from the menu) loads the tutorial world; F8 jumps straight to the flat SandboxCourse;
    /// F10 starts a normal run.
    /// </summary>
    internal class DebugHotkeys : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                Plugin.AvatarScale.Value = Mathf.Max(0.1f, Plugin.AvatarScale.Value - 0.1f);
                Plugin.Log.LogInfo("Scale = " + Plugin.AvatarScale.Value);
            }
            if (Input.GetKeyDown(KeyCode.F2))
            {
                Plugin.AvatarScale.Value += 0.1f;
                Plugin.Log.LogInfo("Scale = " + Plugin.AvatarScale.Value);
            }
            if (Input.GetKeyDown(KeyCode.F3))
            {
                Plugin.AvatarOffsetY.Value -= 0.1f;
                Plugin.Log.LogInfo("OffsetY = " + Plugin.AvatarOffsetY.Value);
            }
            if (Input.GetKeyDown(KeyCode.F4))
            {
                Plugin.AvatarOffsetY.Value += 0.1f;
                Plugin.Log.LogInfo("OffsetY = " + Plugin.AvatarOffsetY.Value);
            }
            if (Input.GetKeyDown(KeyCode.F5))
            {
                Plugin.AvatarOffsetX.Value -= 0.1f;
                Plugin.Log.LogInfo("OffsetX = " + Plugin.AvatarOffsetX.Value);
            }
            if (Input.GetKeyDown(KeyCode.F6))
            {
                Plugin.AvatarOffsetX.Value += 0.1f;
                Plugin.Log.LogInfo("OffsetX = " + Plugin.AvatarOffsetX.Value);
            }
            if (Input.GetKeyDown(KeyCode.F11))
            {
                Plugin.HideVanillaBody.Value = !Plugin.HideVanillaBody.Value;
                Plugin.Log.LogInfo("HideVanillaBody = " + Plugin.HideVanillaBody.Value);
            }

            if (Input.GetKeyDown(KeyCode.F9))
            {
                PreRunScript pre = PreRunScript.instance;
                if (pre != null)
                {
                    Plugin.Log.LogInfo("F9: PreRunScript.StartTutorial().");
                    pre.StartTutorial();
                }
            }

            if (Input.GetKeyDown(KeyCode.F8))
            {
                TutorialHandler handler = TutorialHandler.main;
                if (handler != null)
                {
                    Plugin.Log.LogInfo("F8: TutorialHandler.StartCourse(SandboxCourse).");
                    handler.StartCourse(typeof(SandboxCourse));
                    if (handler.courseSelectScreen != null)
                    {
                        handler.courseSelectScreen.SetActive(false);
                    }
                }
                else
                {
                    Plugin.Log.LogInfo("F8: no TutorialHandler in this scene.");
                }
            }

            if (Input.GetKeyDown(KeyCode.F10))
            {
                PreRunScript pre = PreRunScript.instance;
                if (pre != null)
                {
                    Plugin.Log.LogInfo("F10: PreRunScript.StartRun().");
                    pre.StartRun();
                }
            }
        }
    }
}
