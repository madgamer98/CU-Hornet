using System;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// Silksong side of the passthrough. Streams Hornet's captured frame, HUD, vitals and input to
    /// Casualties: Unknown over shared memory (<see cref="LiveLink"/>) and mirrors CU's terrain/actors
    /// (<see cref="TerrainMirror"/>, <see cref="EntityProxies"/>).
    /// Hotkeys: F4 apply mirror, F3 restore, F7 dump Hornet info.
    /// </summary>
    [BepInPlugin(Guid, "Hornet Exporter", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.cuhornet.silksong.exporter";
        public const string Version = "0.1.0";

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

            Log.LogInfo("Hornet Exporter v" + Version + " loaded.");

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

    /// <summary>F4 = apply the mirror; F3 = restore; F7 = dump Hornet info.</summary>
    internal class Hotkeys : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7))
            {
                DumpHornet();
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
