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
        internal static ConfigEntry<bool> EnableMoves;
        internal static ConfigEntry<KeyboardShortcut> KeySlash;
        internal static ConfigEntry<KeyboardShortcut> KeyDash;
        internal static ConfigEntry<KeyboardShortcut> KeyNeedle;
        internal static ConfigEntry<KeyboardShortcut> KeyBind;
        internal static ConfigEntry<float> DashSpeed;
        internal static ConfigEntry<float> DashDuration;
        internal static ConfigEntry<float> DashCooldown;
        internal static ConfigEntry<float> SlashDamage;
        internal static ConfigEntry<float> SlashRange;
        internal static ConfigEntry<float> SlashReach;
        internal static ConfigEntry<float> SlashActive;
        internal static ConfigEntry<float> SlashCooldown;
        internal static ConfigEntry<float> PogoSpeed;
        internal static ConfigEntry<float> NeedleDamage;
        internal static ConfigEntry<float> NeedleSpeed;
        internal static ConfigEntry<float> NeedleReturnSpeed;
        internal static ConfigEntry<float> NeedleRange;
        internal static ConfigEntry<float> NeedleCooldown;
        internal static ConfigEntry<float> DoubleJumpSpeed;
        internal static ConfigEntry<float> WallSlideSpeed;
        internal static ConfigEntry<float> WallJumpX;
        internal static ConfigEntry<float> WallJumpY;

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

            EnableMoves = Config.Bind("Moves", "EnableMoves", true, "Enable Hornet's moves (dash/slash/pogo).");
            KeySlash = Config.Bind("Moves", "KeySlash", new KeyboardShortcut(KeyCode.J), "Slash (hold W = up, hold S in air = down/pogo).");
            KeyDash = Config.Bind("Moves", "KeyDash", new KeyboardShortcut(KeyCode.K), "Dash.");
            KeyNeedle = Config.Bind("Moves", "KeyNeedle", new KeyboardShortcut(KeyCode.L), "Throw the needle.");
            KeyBind = Config.Bind("Moves", "KeyBind", new KeyboardShortcut(KeyCode.H), "Bind (heal).");
            DashSpeed = Config.Bind("Moves", "DashSpeed", 14f, "Dash horizontal speed.");
            DashDuration = Config.Bind("Moves", "DashDuration", 0.55f, "Dash duration (s).");
            DashCooldown = Config.Bind("Moves", "DashCooldown", 0.55f, "Dash cooldown (s).");
            SlashDamage = Config.Bind("Moves", "SlashDamage", 12f, "Slash damage.");
            SlashRange = Config.Bind("Moves", "SlashRange", 2.2f, "Slash hitbox size.");
            SlashReach = Config.Bind("Moves", "SlashReach", 1.4f, "How far in front the slash hits.");
            SlashActive = Config.Bind("Moves", "SlashActive", 0.10f, "When in the clip the hit lands (s before the end).");
            SlashCooldown = Config.Bind("Moves", "SlashCooldown", 0.12f, "Slash cooldown (s).");
            PogoSpeed = Config.Bind("Moves", "PogoSpeed", 19f, "Upward speed of a pogo bounce.");
            NeedleDamage = Config.Bind("Moves", "NeedleDamage", 10f, "Thrown needle damage.");
            NeedleSpeed = Config.Bind("Moves", "NeedleSpeed", 26f, "Thrown needle speed.");
            NeedleReturnSpeed = Config.Bind("Moves", "NeedleReturnSpeed", 32f, "Needle return speed.");
            NeedleRange = Config.Bind("Moves", "NeedleRange", 9f, "Distance before the needle returns.");
            NeedleCooldown = Config.Bind("Moves", "NeedleCooldown", 0.35f, "Needle throw cooldown (s).");
            DoubleJumpSpeed = Config.Bind("Moves", "DoubleJumpSpeed", 12f, "Double jump upward speed.");
            WallSlideSpeed = Config.Bind("Moves", "WallSlideSpeed", 2.5f, "Downward speed while wall sliding.");
            WallJumpX = Config.Bind("Moves", "WallJumpX", 11f, "Wall jump horizontal speed.");
            WallJumpY = Config.Bind("Moves", "WallJumpY", 13f, "Wall jump vertical speed.");

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
