using System;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// Silksong-side helper. Finds the live Hornet (HeroController) and, on F7, logs her
    /// sprite hierarchy and current animation, and writes one isolated frame to a PNG.
    /// This is the seed of the passthrough source: the same plugin will serve Hornet frames
    /// to Casualties: Unknown over a shared-memory link.
    /// </summary>
    [BepInPlugin(Guid, "Hornet Exporter", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.cuhornet.silksong.exporter";
        public const string Version = "0.0.9";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            // Keep Silksong simulating while the host game is focused.
            Application.runInBackground = true;
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            var go = new GameObject("HornetExporter.Hotkeys");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Hotkeys>();

            var link = new GameObject("HornetExporter.LiveLink");
            DontDestroyOnLoad(link);
            link.hideFlags = HideFlags.HideAndDontSave;
            link.AddComponent<LiveLink>();

            Log.LogInfo("Hornet Exporter v" + Version + " loaded. Press F7 in gameplay to inspect Hornet.");

            var layers = new StringBuilder("Layers: ");
            for (int i = 0; i < 32; i++)
            {
                string n = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(n))
                {
                    layers.Append(i).Append('=').Append(n).Append("  ");
                }
            }
            Log.LogInfo(layers.ToString());
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }

    /// <summary>F7 = dump Hornet info; F8 = write an isolated Hornet PNG.</summary>
    internal class Hotkeys : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7))
            {
                DumpHornet();
            }

            if (Input.GetKeyDown(KeyCode.F8))
            {
                HornetCapture.CaptureToFile();
            }

            if (Input.GetKeyDown(KeyCode.F6))
            {
                DumpClips();
            }

            if (Input.GetKeyDown(KeyCode.F5))
            {
                HornetCapture.Bake(DefaultClips);
            }

            if (Input.GetKeyDown(KeyCode.F4))
            {
                TerrainMirror.Apply();
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                TerrainMirror.Restore();
            }
        }

        private static readonly string[] DefaultClips =
        {
            // locomotion
            "Idle", "Idle To Run", "Idle To Run Short", "Run", "Run To Idle", "Turn", "Turn Quick", "Walk",
            "Airborne", "Fall", "Land", "HardLand", "HardLand Quick", "Land to Run",
            "Sprint", "Sprint Air", "Sprint Turn", "Super Jump Loop",
            // dash
            "Dash", "Dash Down", "Air Dash", "Dash Attack", "Dash Attack Antic", "Dash Attack Recover",
            "Dash To Idle",
            // melee / needle
            "Slash", "SlashAlt", "Slash_Charged", "SlashEffect", "SlashEffectAlt", "UpSlash", "UpSlashEffect",
            "DownSpike", "DownSpike Antic", "Downspike Recovery", "DownSlashEffect", "Wall Slash", "Recoil",
            // wall / mantle
            "Wall Slide", "Wall Cling", "Walljump", "Wall Scramble", "Wall Scramble Antic", "Mantle Cling",
            "Double Jump", "Double Jump Effect",
            // needle throw / harpoon
            "NeedleThrow AnticA", "NeedleThrow AnticG", "NeedleThrow Throwing", "NeedleThrow Out",
            "NeedleThrow Return", "NeedleThrow Catch", "NeedleThrow Thunk",
            "Harpoon Antic", "Harpoon Throw", "Harpoon Catch", "Harpoon Needle", "Harpoon Needle Return",
            // bind / utility
            "BindCharge Ground", "BindBurst Ground", "BindCancel Ground", "Bind Silk",
            "Hurt To Idle", "Idle Hurt", "Death",
        };

        private void DumpClips()
        {
            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                Plugin.Log.LogInfo("F6: no HeroController.");
                return;
            }
            var animator = hero.GetComponentInChildren<tk2dSpriteAnimator>();
            if (animator == null || animator.Library == null)
            {
                Plugin.Log.LogInfo("F6: no tk2d library.");
                return;
            }

            var sb = new StringBuilder();
            sb.Append("{\"clips\":[");
            tk2dSpriteAnimationClip[] clips = animator.Library.clips;
            for (int i = 0; i < clips.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                tk2dSpriteAnimationClip c = clips[i];
                sb.Append("{\"name\":\"").Append(Escape(c.name)).Append("\",\"fps\":").Append(c.fps)
                  .Append(",\"frames\":[");
                if (c.frames != null)
                {
                    for (int f = 0; f < c.frames.Length; f++)
                    {
                        if (f > 0)
                        {
                            sb.Append(',');
                        }
                        tk2dSpriteAnimationFrame fr = c.frames[f];
                        string coll = fr.spriteCollection != null ? fr.spriteCollection.spriteCollectionName : null;
                        string spr = "?";
                        if (fr.spriteCollection != null && fr.spriteCollection.spriteDefinitions != null &&
                            fr.spriteId >= 0 && fr.spriteId < fr.spriteCollection.spriteDefinitions.Length)
                        {
                            spr = fr.spriteCollection.spriteDefinitions[fr.spriteId].name;
                        }
                        sb.Append("{\"c\":\"").Append(Escape(coll)).Append("\",\"i\":").Append(fr.spriteId)
                          .Append(",\"n\":\"").Append(Escape(spr)).Append("\"}");
                    }
                }
                sb.Append("]}");
            }
            sb.Append("]}");

            string dir = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "HornetExporter");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, "clips.json");
            System.IO.File.WriteAllText(path, sb.ToString());
            Plugin.Log.LogInfo("F6: wrote " + path + " (" + clips.Length + " clips)");
        }

        private static string Escape(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private void DumpHornet()
        {
            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                Plugin.Log.LogInfo("F7: HeroController.instance is null (not in gameplay).");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("F7: Hornet hierarchy:");
            DumpTransform(hero.transform, sb, 0);
            Plugin.Log.LogInfo(sb.ToString());

            var animator = hero.GetComponentInChildren<tk2dSpriteAnimator>();
            if (animator != null)
            {
                sb = new StringBuilder();
                sb.AppendLine("tk2dSpriteAnimator found: " + animator.name);
                if (animator.CurrentClip != null)
                {
                    sb.AppendLine("  current clip: " + animator.CurrentClip.name +
                                  " fps=" + animator.CurrentClip.fps +
                                  " frames=" + animator.CurrentClip.frames.Length);
                }
                if (animator.Library != null)
                {
                    sb.AppendLine("  library clips: " + animator.Library.clips.Length);
                    int n = Mathf.Min(animator.Library.clips.Length, 40);
                    for (int i = 0; i < n; i++)
                    {
                        sb.AppendLine("    " + animator.Library.clips[i].name);
                    }
                }
                Plugin.Log.LogInfo(sb.ToString());
            }
            else
            {
                Plugin.Log.LogInfo("No tk2dSpriteAnimator found under HeroController.");
            }
        }

        private static void DumpTransform(Transform t, StringBuilder sb, int depth)
        {
            if (depth > 4)
            {
                return;
            }
            sb.Append(' ', depth * 2).Append(t.name);
            var sr = t.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sb.Append("  [SpriteRenderer sprite=" + (sr.sprite != null ? sr.sprite.name : "null") + "]");
            }
            var tk = t.GetComponent<tk2dSprite>();
            if (tk != null)
            {
                sb.Append("  [tk2dSprite]");
            }
            sb.AppendLine();
            for (int i = 0; i < t.childCount; i++)
            {
                DumpTransform(t.GetChild(i), sb, depth + 1);
            }
        }
    }
}
