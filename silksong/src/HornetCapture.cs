using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// Captures Hornet using the game's own main camera: renders one frame with Hornet's body
    /// visible and one with it hidden, and uses the difference as alpha. Reads only a cropped
    /// region around Hornet so baking hundreds of frames stays fast.
    /// </summary>
    internal static class HornetCapture
    {
        private const int Pad = 110;

        private static int CaptureLayer = 31;
        private const int CapSize = 256;
        private static Camera _capCam;
        private static RenderTexture _capRt;
        private static Texture2D _capTex;
        private static bool _layerSet;

        private static void EnsureCaptureCamera()
        {
            if (_capCam != null)
            {
                return;
            }
            // Prefer the highest layer that has no name (most likely unused), so the capture sees
            // only Hornet.
            for (int i = 31; i >= 8; i--)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                {
                    CaptureLayer = i;
                    break;
                }
            }
            Plugin.Log.LogInfo("HornetCapture: using layer " + CaptureLayer);
            var go = new GameObject("HornetCaptureCam");
            Object.DontDestroyOnLoad(go);
            _capCam = go.AddComponent<Camera>();
            _capCam.enabled = false;
            _capCam.orthographic = true;
            _capCam.clearFlags = CameraClearFlags.SolidColor;
            _capCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _capCam.cullingMask = 1 << CaptureLayer;
            _capRt = new RenderTexture(CapSize, CapSize, 16, RenderTextureFormat.ARGB32);
            _capRt.Create();
            _capCam.targetTexture = _capRt;
            _capTex = new Texture2D(CapSize, CapSize, TextureFormat.RGBA32, false);
        }

        /// <summary>
        /// Cheap isolated capture: a dedicated camera on a Hornet-only layer renders her into a
        /// small RenderTexture. No full-screen readback, no diff, so it is safe to call often.
        /// </summary>
        public static bool CaptureIsolatedRgba(out byte[] rgba, out int width, out int height,
            out float pivotX, out float pivotY, out float worldX, out float worldY)
        {
            rgba = null;
            width = height = 0;
            pivotX = pivotY = worldX = worldY = 0f;

            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                return false;
            }
            EnsureCaptureCamera();
            if (_capCam == null)
            {
                return false;
            }

            if (!_layerSet)
            {
                SetLayerRecursive(hero.gameObject, CaptureLayer);
                _layerSet = true;
            }

            Bounds b = GetBounds(hero);
            Vector3 center = b.center;
            float half = Mathf.Max(b.extents.y, b.extents.x) + 0.4f;
            _capCam.transform.position = new Vector3(center.x, center.y, -10f);
            _capCam.transform.rotation = Quaternion.identity;
            _capCam.orthographicSize = Mathf.Max(half, 1f);
            _capCam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _capRt;
            _capTex.ReadPixels(new Rect(0, 0, CapSize, CapSize), 0, 0);
            _capTex.Apply();
            RenderTexture.active = prev;

            rgba = _capTex.GetRawTextureData<byte>().ToArray();
            width = CapSize;
            height = CapSize;

            Vector3 p = hero.transform.position;
            worldX = p.x;
            worldY = p.y;
            float span = _capCam.orthographicSize;
            pivotX = 0.5f + (p.x - center.x) / (2f * span);
            pivotY = 0.5f + (p.y - center.y) / (2f * span);
            return true;
        }

        /// <summary>Capture Hornet isolated and return raw RGBA32 bytes (bottom-up, Unity order).</summary>
        public static bool CaptureRgba(out byte[] rgba, out int width, out int height, out float pivotX,
            out float pivotY, out float worldX, out float worldY)
        {
            rgba = null;
            width = height = 0;
            pivotX = pivotY = worldX = worldY = 0f;

            HeroController hero = HeroController.instance;
            Camera cam = MainCamera();
            if (hero == null || cam == null)
            {
                return false;
            }

            RenderTexture rt = RenderTexture.GetTemporary(Screen.width, Screen.height, 24);
            ScreenRect rect = ComputeRect(hero, cam);

            Color32[] with = RenderRead(cam, rt, rect);
            SetBodyEnabled(hero, false);
            Color32[] without = RenderRead(cam, rt, rect);
            SetBodyEnabled(hero, true);
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);

            float[] pivot = new float[2];
            Texture2D tex = DiffCrop(with, without, rect, hero, cam, pivot);
            if (tex == null)
            {
                return false;
            }
            rgba = tex.GetRawTextureData<byte>().ToArray();
            width = tex.width;
            height = tex.height;
            pivotX = pivot[0];
            pivotY = pivot[1];
            Vector3 p = hero.transform.position;
            worldX = p.x;
            worldY = p.y;
            Object.Destroy(tex);
            return true;
        }

        public static void CaptureToFile()
        {
            HeroController hero = HeroController.instance;
            Camera cam = MainCamera();
            if (hero == null || cam == null)
            {
                Plugin.Log.LogInfo("F8: missing hero/camera.");
                return;
            }

            RenderTexture rt = RenderTexture.GetTemporary(Screen.width, Screen.height, 24);
            ScreenRect rect = ComputeRect(hero, cam);
            Color32[] with = RenderRead(cam, rt, rect);
            SetBodyEnabled(hero, false);
            Color32[] without = RenderRead(cam, rt, rect);
            SetBodyEnabled(hero, true);
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);

            float[] pivot = new float[2];
            Texture2D tex = DiffCrop(with, without, rect, hero, cam, pivot);
            if (tex == null)
            {
                Plugin.Log.LogInfo("F8: no Hornet pixels found.");
                return;
            }
            string dir = Path.Combine(BepInEx.Paths.PluginPath, "HornetExporter");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "hornet_capture.png"), tex.EncodeToPNG());
            Plugin.Log.LogInfo("F8: wrote hornet_capture.png " + tex.width + "x" + tex.height);
        }

        /// <summary>Bakes exact frames for each named clip.</summary>
        public static void Bake(string[] clipNames)
        {
            HeroController hero = HeroController.instance;
            Camera cam = MainCamera();
            var animator = hero != null ? hero.GetComponentInChildren<tk2dSpriteAnimator>() : null;
            if (hero == null || cam == null || animator == null || animator.Library == null)
            {
                Plugin.Log.LogInfo("F5: missing hero/camera/animator.");
                return;
            }

            RenderTexture rt = RenderTexture.GetTemporary(Screen.width, Screen.height, 24);
            ScreenRect rect = ComputeRect(hero, cam);

            SetBodyEnabled(hero, false);
            Color32[] bg = RenderRead(cam, rt, rect);
            SetBodyEnabled(hero, true);

            string dir = Path.Combine(BepInEx.Paths.PluginPath, "HornetExporter", "baked");
            Directory.CreateDirectory(Path.Combine(dir, "frames"));

            var manifest = new System.Text.StringBuilder();
            manifest.Append("{\"frames\":{");
            int total = 0;
            bool firstClip = true;

            foreach (string clipName in clipNames)
            {
                tk2dSpriteAnimationClip clip = FindClip(animator, clipName);
                if (clip == null || clip.frames == null || clip.frames.Length == 0)
                {
                    Plugin.Log.LogInfo("F5: skip clip " + clipName);
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
                bool firstFrame = true;
                for (int f = 0; f < clip.frames.Length; f++)
                {
                    animator.SetFrame(f);
                    Color32[] px = RenderRead(cam, rt, rect);
                    float[] pivot = new float[2];
                    Texture2D frame = DiffCrop(px, bg, rect, hero, cam, pivot);
                    if (frame == null)
                    {
                        continue;
                    }
                    string fname = Safe(clipName) + "_" + f.ToString("D2") + ".png";
                    File.WriteAllBytes(Path.Combine(dir, "frames", fname), frame.EncodeToPNG());
                    if (!firstFrame)
                    {
                        manifest.Append(',');
                    }
                    firstFrame = false;
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

        private struct ScreenRect
        {
            public int x, y, w, h;
        }

        private static Camera MainCamera()
        {
            if (GameCameras.instance != null && GameCameras.instance.mainCamera != null)
            {
                return GameCameras.instance.mainCamera;
            }
            return Camera.main;
        }

        private static ScreenRect ComputeRect(HeroController hero, Camera cam)
        {
            Bounds b = GetBounds(hero);
            Vector3 a = cam.WorldToScreenPoint(new Vector3(b.min.x, b.min.y, 0f));
            Vector3 c = cam.WorldToScreenPoint(new Vector3(b.max.x, b.max.y, 0f));
            int w = Screen.width;
            int h = Screen.height;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, c.x)) - Pad, 0, w - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, c.y)) - Pad, 0, h - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, c.x)) + Pad, x0 + 1, w);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, c.y)) + Pad, y0 + 1, h);
            return new ScreenRect { x = x0, y = y0, w = x1 - x0, h = y1 - y0 };
        }

        private static Color32[] RenderRead(Camera cam, RenderTexture rt, ScreenRect rect)
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rect.w, rect.h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(rect.x, rect.y, rect.w, rect.h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Color32[] px = tex.GetPixels32();
            Object.Destroy(tex);
            return px;
        }

        private static void SetBodyEnabled(HeroController hero, bool enabled)
        {
            foreach (Renderer r in VisibleRenderers(hero))
            {
                r.enabled = enabled;
            }
        }

        private static Texture2D DiffCrop(Color32[] px, Color32[] bg, ScreenRect rect, HeroController hero,
            Camera cam, float[] pivot)
        {
            int w = rect.w, h = rect.h;
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
            pivot[0] = ((sp.x - rect.x) - minX) / cw;
            pivot[1] = ((sp.y - rect.y) - minY) / ch;
            return tex;
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

        private static Bounds GetBounds(HeroController hero)
        {
            Renderer[] renderers = hero.GetComponentsInChildren<Renderer>(true);
            bool has = false;
            Bounds bounds = new Bounds(hero.transform.position, Vector3.one);
            foreach (Renderer r in renderers)
            {
                if (!(r is SpriteRenderer) && r.GetType().Name != "MeshRenderer")
                {
                    continue;
                }
                if (!has)
                {
                    bounds = r.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }
            return bounds;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
            }
        }

        private static List<Renderer> VisibleRenderers(HeroController hero)
        {
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

        private static string Safe(string s)
        {
            return s.Replace(' ', '_').Replace('/', '_');
        }
    }
}
