using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HornetInCasualties
{
    /// <summary>
    /// Casualties: Unknown side of the Hornet passthrough. It attaches <see cref="LiveLink"/> to the
    /// player, which talks to Silksong over shared memory. There is no baked asset import any more.
    /// </summary>
    [BepInPlugin(Guid, "Hornet in Casualties", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.cuhornet.hornetincasualties";
        public const string Version = "0.3.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        internal static ConfigEntry<bool> Enable;
        internal static ConfigEntry<bool> HideVanillaBody;
        internal static ConfigEntry<bool> DebugKeys;
        internal static ConfigEntry<float> AvatarScale;
        internal static ConfigEntry<float> AvatarOffsetX;
        internal static ConfigEntry<float> AvatarOffsetY;
        internal static ConfigEntry<bool> LiveMode;
        internal static ConfigEntry<KeyboardShortcut> KeySlash;
        internal static ConfigEntry<KeyboardShortcut> KeyDash;
        internal static ConfigEntry<KeyboardShortcut> KeyNeedle;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            // The passthrough requires both games to keep simulating while one is unfocused.
            Application.runInBackground = true;

            Enable = Config.Bind("General", "Enable", true, "Show Hornet on the player.");
            HideVanillaBody = Config.Bind("General", "HideVanillaBody", true,
                "Hide the vanilla experiment while Hornet is shown.");
            LiveMode = Config.Bind("Passthrough", "LiveMode", true,
                "Run the live passthrough link to Silksong (required).");
            AvatarScale = Config.Bind("Avatar", "Scale", 1.6f, "Hornet sprite scale.");
            AvatarOffsetX = Config.Bind("Avatar", "OffsetX", 0.2f, "Hornet horizontal offset (sprite units).");
            AvatarOffsetY = Config.Bind("Avatar", "OffsetY", -0.1f, "Hornet vertical offset (sprite units).");
            KeySlash = Config.Bind("Moves", "KeySlash", new KeyboardShortcut(KeyCode.J),
                "Slash (the action is forwarded to Silksong).");
            KeyDash = Config.Bind("Moves", "KeyDash", new KeyboardShortcut(KeyCode.K), "Dash.");
            KeyNeedle = Config.Bind("Moves", "KeyNeedle", new KeyboardShortcut(KeyCode.L), "Throw the needle.");
            DebugKeys = Config.Bind("Dev", "DebugKeys", true,
                "F1/F2 scale, F3/F4 offsetY, F5/F6 offsetX. F9 tutorial, F10 run.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            if (DebugKeys.Value)
            {
                var go = new GameObject("HornetInCasualties.Debug");
                DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<DebugHotkeys>();
            }

            Log.LogInfo("Hornet in Casualties v" + Version + " loaded (passthrough).");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }

    /// <summary>Late in the frame, make sure the player has a Hornet avatar (which attaches LiveLink).</summary>
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
            if (body != null)
            {
                HornetAvatar.Ensure(body);
            }
        }
    }

    /// <summary>Live tuning for placement.</summary>
    internal class DebugHotkeys : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                Plugin.AvatarScale.Value = Mathf.Max(0.05f, Plugin.AvatarScale.Value - 0.05f);
                Plugin.Log.LogInfo("Scale = " + Plugin.AvatarScale.Value);
            }
            if (Input.GetKeyDown(KeyCode.F2))
            {
                Plugin.AvatarScale.Value += 0.05f;
                Plugin.Log.LogInfo("Scale = " + Plugin.AvatarScale.Value);
            }
            if (Input.GetKeyDown(KeyCode.F3))
            {
                Plugin.AvatarOffsetY.Value -= 0.05f;
                Plugin.Log.LogInfo("OffsetY = " + Plugin.AvatarOffsetY.Value);
            }
            if (Input.GetKeyDown(KeyCode.F4))
            {
                Plugin.AvatarOffsetY.Value += 0.05f;
                Plugin.Log.LogInfo("OffsetY = " + Plugin.AvatarOffsetY.Value);
            }
            if (Input.GetKeyDown(KeyCode.F5))
            {
                Plugin.AvatarOffsetX.Value -= 0.05f;
                Plugin.Log.LogInfo("OffsetX = " + Plugin.AvatarOffsetX.Value);
            }
            if (Input.GetKeyDown(KeyCode.F6))
            {
                Plugin.AvatarOffsetX.Value += 0.05f;
                Plugin.Log.LogInfo("OffsetX = " + Plugin.AvatarOffsetX.Value);
            }
            if (Input.GetKeyDown(KeyCode.F9))
            {
                PreRunScript pre = PreRunScript.instance;
                if (pre != null)
                {
                    pre.StartTutorial();
                }
            }
            if (Input.GetKeyDown(KeyCode.F8))
            {
                TutorialHandler handler = TutorialHandler.main;
                if (handler != null)
                {
                    handler.StartCourse(typeof(SandboxCourse));
                    if (handler.courseSelectScreen != null)
                    {
                        handler.courseSelectScreen.SetActive(false);
                    }
                }
            }
            if (Input.GetKeyDown(KeyCode.F10))
            {
                PreRunScript pre = PreRunScript.instance;
                if (pre != null)
                {
                    pre.StartRun();
                }
            }
        }
    }
}
