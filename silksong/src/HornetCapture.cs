using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// Captures Hornet using the game's own main camera (URP-friendly): renders one frame
    /// with Hornet visible and one with her renderers hidden, then uses the difference as
    /// alpha. That isolates Hornet from the scenery without fighting URP's camera setup.
    /// </summary>
    internal static class HornetCapture
    {
        public static void CaptureToFile()
        {
            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                Plugin.Log.LogInfo("F8: no HeroController (not in gameplay).");
                return;
            }

            Camera cam = GameCameras.instance != null && GameCameras.instance.mainCamera != null
                ? GameCameras.instance.mainCamera
                : Camera.main;
            if (cam == null)
            {
                Plugin.Log.LogInfo("F8: no main camera.");
                return;
            }

            int w = Mathf.Min(Screen.width, 1920);
            int h = Mathf.Min(Screen.height, 1080);
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 24);
            RenderTexture prev = cam.targetTexture;

            Color32[] withHero = RenderRead(cam, rt, w, h);
            List<Renderer> renderers = VisibleRenderers(hero);
            var wasEnabled = new List<bool>();
            foreach (Renderer r in renderers)
            {
                wasEnabled.Add(r.enabled);
                r.enabled = false;
            }
            Color32[] without = RenderRead(cam, rt, w, h);
            for (int i = 0; i < renderers.Count; i++)
            {
                renderers[i].enabled = wasEnabled[i];
            }

            cam.targetTexture = prev;
            RenderTexture.ReleaseTemporary(rt);

            // Diff -> Hornet-only RGBA, then crop to the changed region.
            var img = new Color32[w * h];
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int i = 0; i < img.Length; i++)
            {
                Color32 a = withHero[i];
                Color32 b = without[i];
                int d = Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
                if (d > 16)
                {
                    img[i] = new Color32(a.r, a.g, a.b, (byte)Mathf.Min(255, d * 2));
                    int x = i % w;
                    int y = i / w;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < 0)
            {
                Plugin.Log.LogInfo("F8: Hornet diff found no pixels (renderers not captured).");
                return;
            }

            int cw = maxX - minX + 1;
            int ch = maxY - minY + 1;
            var cropped = new Color32[cw * ch];
            for (int y = 0; y < ch; y++)
            {
                for (int x = 0; x < cw; x++)
                {
                    cropped[y * cw + x] = img[(minY + y) * w + (minX + x)];
                }
            }

            var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            tex.SetPixels32(cropped);
            tex.Apply();

            byte[] png = tex.EncodeToPNG();
            string dir = Path.Combine(BepInEx.Paths.PluginPath, "HornetExporter");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "hornet_capture.png");
            File.WriteAllBytes(path, png);

            // Hornet's feet are the bottom of the crop; pivotX from world->crop mapping.
            Plugin.Log.LogInfo("F8: wrote " + path + " crop " + cw + "x" + ch +
                               " screenRect=(" + minX + "," + minY + ") screen=" + w + "x" + h);
        }

        private static Color32[] RenderRead(Camera cam, RenderTexture rt, int w, int h)
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Color32[] px = tex.GetPixels32();
            Object.Destroy(tex);
            return px;
        }

        /// <summary>
        /// Walks each named clip, steps Hornet's animator frame by frame, and writes an exact
        /// PNG + pivot per frame plus a manifest. One background render is reused for all frames.
        /// </summary>
        public static void Bake(string[] clipNames)
        {
            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                Plugin.Log.LogInfo("F5: no HeroController (not in gameplay).");
                return;
            }
            Camera cam = GameCameras.instance != null && GameCameras.instance.mainCamera != null
                ? GameCameras.instance.mainCamera
                : Camera.main;
            var animator = hero.GetComponentInChildren<tk2dSpriteAnimator>();
            List<Renderer> body = VisibleRenderers(hero);
            if (cam == null || animator == null || body.Count == 0)
            {
                Plugin.Log.LogInfo("F5: missing camera/animator/body.");
                return;
            }

            int w = Mathf.Min(Screen.width, 1920);
            int h = Mathf.Min(Screen.height, 1080);
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 24);

            // Background once (Hornet's body hidden).
            var wasEnabled = new List<bool>();
            foreach (Renderer r in body)
            {
                wasEnabled.Add(r.enabled);
                r.enabled = false;
            }
            Color32[] bg = RenderRead(cam, rt, w, h);
            for (int i = 0; i < body.Count; i++)
            {
                body[i].enabled = wasEnabled[i];
            }

            string dir = Path.Combine(BepInEx.Paths.PluginPath, "HornetExporter", "baked");
            Directory.CreateDirectory(Path.Combine(dir, "frames"));

            var manifest = new System.Text.StringBuilder();
            manifest.Append("{\"frames\":{");
            int total = 0;
            bool firstClip = true;

            foreach (string clipName in clipNames)
            {
                tk2dSpriteAnimationClip clip = FindClip(animator, clipName);
                if (clip == null)
                {
                    Plugin.Log.LogInfo("F5: no clip " + clipName);
                    continue;
                }
                if (!firstClip)
                {
                    manifest.Append(',');
                }
                firstClip = false;
                manifest.Append('"').Append(clipName).Append("\":{\"fps\":").Append(clip.fps).Append(",\"frames\":[");

                animator.Play(clipName);
                animator.Pause();
                for (int f = 0; f < clip.frames.Length; f++)
                {
                    animator.SetFrame(f);
                    Color32[] px = RenderRead(cam, rt, w, h);
                    float[] pivot = new float[2];
                    Texture2D frame = DiffCrop(px, bg, w, h, hero, cam, pivot);
                    if (frame == null)
                    {
                        continue;
                    }
                    string fname = Safe(clipName) + "_" + f.ToString("D2") + ".png";
                    File.WriteAllBytes(Path.Combine(dir, "frames", fname), frame.EncodeToPNG());
                    if (f > 0)
                    {
                        manifest.Append(',');
                    }
                    manifest.Append("{\"file\":\"frames/").Append(fname).Append("\",\"w\":").Append(frame.width)
                            .Append(",\"h\":").Append(frame.height)
                            .Append(",\"px\":").Append(pivot[0].ToString("0.0000"))
                            .Append(",\"py\":").Append(pivot[1].ToString("0.0000")).Append('}');
                    total++;
                    Object.Destroy(frame);
                }
                manifest.Append("]}");
            }
            manifest.Append("}}");

            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllText(Path.Combine(dir, "bake.json"), manifest.ToString());
            Plugin.Log.LogInfo("F5: baked " + total + " frames -> " + dir);
        }

        private static tk2dSpriteAnimationClip FindClip(tk2dSpriteAnimator animator, string name)
        {
            foreach (tk2dSpriteAnimationClip c in animator.Library.clips)
            {
                if (c.name == name)
                {
                    return c;
                }
            }
            return null;
        }

        // Diff a visible-Hornet frame against the cached background, crop, and compute the
        // pivot as Hornet's world origin within the crop.
        private static Texture2D DiffCrop(Color32[] px, Color32[] bg, int w, int h, HeroController hero,
            Camera cam, float[] pivot)
        {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            var img = new Color32[w * h];
            for (int i = 0; i < img.Length; i++)
            {
                Color32 a = px[i];
                Color32 b = bg[i];
                int d = Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
                if (d > 16)
                {
                    img[i] = new Color32(a.r, a.g, a.b, (byte)Mathf.Min(255, d * 2));
                    int x = i % w;
                    int y = i / w;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0)
            {
                return null;
            }
            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            var cropped = new Color32[cw * ch];
            for (int y = 0; y < ch; y++)
            {
                for (int x = 0; x < cw; x++)
                {
                    cropped[y * cw + x] = img[(minY + y) * w + (minX + x)];
                }
            }
            var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            tex.SetPixels32(cropped);
            tex.Apply();

            Vector3 sp = cam.WorldToScreenPoint(hero.transform.position);
            pivot[0] = (sp.x - minX) / cw;
            pivot[1] = (sp.y - minY) / ch;
            return tex;
        }

        private static string Safe(string s)
        {
            return s.Replace(' ', '_').Replace('/', '_');
        }

        private static List<Renderer> VisibleRenderers(HeroController hero)
        {
            // Only Hornet's own body renderer sits on the hero root; her children are
            // effects/lighting (disabling those changes the whole scene).
            var list = new List<Renderer>();
            foreach (Renderer r in hero.GetComponents<Renderer>())
            {
                if (r is SpriteRenderer || r.GetType().Name == "MeshRenderer")
                {
                    list.Add(r);
                }
            }
            return list;
        }
    }
}
