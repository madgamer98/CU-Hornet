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

        private static RenderTexture _liveRt;
        private static Texture2D _rectTex;
        private static int _rectW, _rectH;
        private static Color32[] _bufA;
        private static Color32[] _bufB;

        // ---- Blank-slate capture (S4 visual): main camera, narrowed culling mask, alpha-0 clear ----
        // Fixed frame size so CU never reallocates its texture. S5 task 1: raised 320 -> 480 (+50%,
        // the human's ask) and then -> 640 because a side slash still reached the 480 edge; the extra
        // ring is transparent. Protocol v9 double-buffers the pixels because a crop this big tears
        // under the old single-buffer seqlock.
        private const int BlankCrop = 640;
        // S5 task 1: HeroAttack (PhysLayers.HERO_ATTACK). Hornet's slash arcs live here, separate
        // from her body on Player(9). They are kept visible in the blank render (see HideEffects).
        private const int AttackLayer = 17;
        private static RenderTexture _blankRt;
        private static Texture2D _blankTex;
        private static int _blankW, _blankH;
        private static Color32[] _blankBuf;
        private static byte[] _blankRgba;
        private static int _heroMask;
        private static bool _heroMaskSet;
        private static bool _edgeLogged;
        private static HeroController _cacheHero;
        private static readonly List<Renderer> _bodyCache = new List<Renderer>();
        private static readonly List<Renderer> _effectCache = new List<Renderer>();

        /// <summary>
        /// Blank-slate capture: render Hornet alone on a transparent background using the game's own
        /// main camera with its culling mask narrowed to the layers her renderers actually occupy.
        /// Because it is the same camera instance, the lit/global state is already correct (the old
        /// dedicated offscreen camera came back dark). No environment, so no diff artifacts or
        /// background bleeding into semi-transparent pixels.
        /// </summary>
        public static bool CaptureBlankRgba(out byte[] rgba, out int width, out int height,
            out float pivotX, out float pivotY, out float worldX, out float worldY)
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
            EnsureHeroMask(hero);
            if (_heroMask == 0)
            {
                return false;
            }

            int sw = Screen.width, sh = Screen.height;
            if (_blankRt == null || _blankRt.width != sw || _blankRt.height != sh)
            {
                if (_blankRt != null)
                {
                    _blankRt.Release();
                }
                _blankRt = new RenderTexture(sw, sh, 24, RenderTextureFormat.ARGB32);
                _blankRt.Create();
            }

            // Center the camera on Hornet for this render. The crop comes from a screen-sized RT and
            // is clamped to the screen, so recentring also avoids her being cut off at the view edge.
            // Do NOT try to frame the slash arc here: the hero carries dozens of attack renderers
            // (many active but far away), and any union recentre moves the camera off her entirely -
            // the capture then returns no frame. The crop is simply made big enough instead.
            Vector3 prevCamPos = cam.transform.position;
            cam.transform.position = new Vector3(hero.transform.position.x, hero.transform.position.y,
                                                 prevCamPos.z);

            // Fixed-size crop centred on her: the published frame is always the same dimensions, so
            // CU can keep one texture/sprite and never reallocate (the old tight-to-alpha crop changed
            // size with every needle/pose and made CU hitch).
            int cw = Mathf.Min(BlankCrop, sw);
            int ch = Mathf.Min(BlankCrop, sh);
            ScreenRect rect = new ScreenRect
            {
                x = (sw - cw) / 2,
                y = (sh - ch) / 2,
                w = cw,
                h = ch,
            };
            if (_blankTex == null || _blankW != cw || _blankH != ch)
            {
                _blankTex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                _blankW = cw;
                _blankH = ch;
                _blankBuf = new Color32[cw * ch];
                _blankRgba = new byte[cw * ch * 4];
            }

            int prevMask = cam.cullingMask;
            CameraClearFlags prevClear = cam.clearFlags;
            Color prevBg = cam.backgroundColor;
            bool prevOrtho = cam.orthographic;
            float spx, spy;
            try
            {
                cam.cullingMask = _heroMask;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.targetTexture = _blankRt;
                HideEffects(hero);
                cam.Render();
                Vector3 sp = cam.WorldToScreenPoint(hero.transform.position);
                spx = sp.x;
                spy = sp.y;
                RenderTexture.active = _blankRt;
                _blankTex.ReadPixels(new Rect(rect.x, rect.y, rect.w, rect.h), 0, 0);
                _blankTex.Apply();
            }
            finally
            {
                RestoreEffects();
                RenderTexture.active = null;
                cam.targetTexture = null;
                cam.cullingMask = prevMask;
                cam.clearFlags = prevClear;
                cam.backgroundColor = prevBg;
                cam.orthographic = prevOrtho;
                cam.transform.position = prevCamPos;
            }

            _blankTex.GetRawTextureData<Color32>().CopyTo(_blankBuf);

            // Alpha bounds: lets us still return a frame when there is content, and detect when the
            // slash crescent is being clipped by the fixed crop (idle Hornet sits well inside it).
            int minX = cw, minY = ch, maxX = -1, maxY = -1;
            for (int i = 0; i < _blankBuf.Length; i++)
            {
                if (_blankBuf[i].a > 8)
                {
                    int x = i % cw;
                    int y = i / cw;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0)
            {
                return false;
            }
            bool touchesEdge = minX <= 1 || minY <= 1 || maxX >= cw - 2 || maxY >= ch - 2;
            if (touchesEdge && !_edgeLogged)
            {
                _edgeLogged = true;
                Plugin.Log.LogWarning("HornetCapture: content reaches the crop edge (bbox " + minX + "," +
                                      minY + ".." + maxX + "," + maxY + " of " + cw + "x" + ch +
                                      ") - a slash arc may be clipped; raise BlankCrop.");
            }
            else if (!touchesEdge)
            {
                _edgeLogged = false;
            }

            int o = 0;
            for (int i = 0; i < _blankBuf.Length; i++)
            {
                Color32 c = _blankBuf[i];
                _blankRgba[o++] = c.r;
                _blankRgba[o++] = c.g;
                _blankRgba[o++] = c.b;
                _blankRgba[o++] = c.a;
            }
            rgba = _blankRgba;
            width = cw;
            height = ch;
            // The camera is centred on her, so her origin lands inside the fixed crop.
            pivotX = (spx - rect.x) / cw;
            pivotY = (spy - rect.y) / ch;
            Vector3 p = hero.transform.position;
            worldX = p.x;
            worldY = p.y;
            return true;
        }

        private static void EnsureHeroMask(HeroController hero)
        {
            if (_heroMaskSet && _cacheHero == hero)
            {
                return;
            }
            bool firstTime = !_heroMaskSet;
            _heroMaskSet = true;
            _cacheHero = hero;
            // Hornet's body/hero light/dust live on Player(9); Hero Only(28) is a hero-exclusive
            // layer if the game uses it. Do NOT OR in every layer her effect renderers touch: many
            // effects sit on Default(0)/Terrain(8)/Enemies(11) etc., which would drag the whole
            // environment into the "blank" render.
            _heroMask = 1 << 9;
            // S5 task 1: keep Hornet's attack VFX (HERO_ATTACK) in the render so slash arcs pass
            // through. The layer is hero-only, so no environment bleeds in.
            _heroMask |= 1 << AttackLayer;
            if (!string.IsNullOrEmpty(LayerMask.LayerToName(28)))
            {
                _heroMask |= 1 << 28;
            }

            // Cache the body vs effect renderers so HideEffects does not walk/allocate the whole
            // hierarchy every frame. Rebuilt when the hero instance changes (room transitions).
            _bodyCache.Clear();
            _effectCache.Clear();
            var names = new System.Text.StringBuilder("HornetCapture blank: hero renderer layers: ");
            foreach (Renderer r in hero.GetComponentsInChildren<Renderer>(true))
            {
                bool isBody = r.transform == hero.transform &&
                              (r is SpriteRenderer || r.GetType().Name == "MeshRenderer");
                if (isBody)
                {
                    _bodyCache.Add(r);
                }
                else
                {
                    _effectCache.Add(r);
                }
                if (firstTime)
                {
                    int l = r.gameObject.layer;
                    names.Append(r.gameObject.name).Append("(L").Append(l).Append('/')
                         .Append(LayerMask.LayerToName(l)).Append(") ");
                }
            }
            if (firstTime)
            {
                Plugin.Log.LogInfo(names.ToString());
                Plugin.Log.LogInfo("HornetCapture blank: culling mask=0x" + _heroMask.ToString("X") +
                                   " body=" + _bodyCache.Count + " effects=" + _effectCache.Count);
            }
        }

        /// <summary>
        /// Cached main-camera diff capture: Hornet-visible vs Hornet-hidden, using the game's own
        /// camera (so lighting is correct and the background cancels to transparent). Reuses all
        /// buffers so it is safe to call repeatedly.
        /// </summary>
        public static bool CaptureDiffRgba(out byte[] rgba, out int width, out int height,
            out float pivotX, out float pivotY, out float worldX, out float worldY)
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

            int sw = Screen.width, sh = Screen.height;
            if (_liveRt == null || _liveRt.width != sw || _liveRt.height != sh)
            {
                if (_liveRt != null)
                {
                    _liveRt.Release();
                }
                _liveRt = new RenderTexture(sw, sh, 24, RenderTextureFormat.ARGB32);
                _liveRt.Create();
            }

            ScreenRect rect = ComputeRect(hero, cam);
            if (_rectTex == null || _rectW != rect.w || _rectH != rect.h)
            {
                _rectTex = new Texture2D(rect.w, rect.h, TextureFormat.RGBA32, false);
                _rectW = rect.w;
                _rectH = rect.h;
                _bufA = new Color32[rect.w * rect.h];
                _bufB = new Color32[rect.w * rect.h];
            }
            RenderInto(cam, rect, _bufA);
            SetBodyEnabled(hero, false);
            RenderInto(cam, rect, _bufB);
            SetBodyEnabled(hero, true);
            cam.targetTexture = null;

            int minX = rect.w, minY = rect.h, maxX = -1, maxY = -1;
            for (int i = 0; i < rect.w * rect.h; i++)
            {
                Color32 a = _bufA[i];
                Color32 b = _bufB[i];
                int d = Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
                if (d > 16)
                {
                    int x = i % rect.w;
                    int y = i / rect.w;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0)
            {
                return false;
            }

            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            if (cw > 320 || ch > 320)
            {
                return false; // a bad diff (whole-screen change); skip this frame
            }
            rgba = new byte[cw * ch * 4];
            int o = 0;
            for (int y = 0; y < ch; y++)
            {
                for (int x = 0; x < cw; x++)
                {
                    int i = (minY + y) * rect.w + (minX + x);
                    Color32 a = _bufA[i];
                    Color32 b = _bufB[i];
                    int d = Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
                    rgba[o++] = a.r;
                    rgba[o++] = a.g;
                    rgba[o++] = a.b;
                    rgba[o++] = (byte)Mathf.Min(255, d * 2);
                }
            }

            Vector3 sp = cam.WorldToScreenPoint(hero.transform.position);
            pivotX = ((sp.x - rect.x) - minX) / cw;
            pivotY = ((sp.y - rect.y) - minY) / ch;
            width = cw;
            height = ch;
            Vector3 p = hero.transform.position;
            worldX = p.x;
            worldY = p.y;
            return true;
        }

        private static void RenderInto(Camera cam, ScreenRect rect, Color32[] buf)
        {
            cam.targetTexture = _liveRt;
            cam.Render();
            RenderTexture.active = _liveRt;
            _rectTex.ReadPixels(new Rect(rect.x, rect.y, rect.w, rect.h), 0, 0);
            _rectTex.Apply();
            RenderTexture.active = null;
            _rectTex.GetRawTextureData<Color32>().CopyTo(buf);
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

        /// <summary>F1: write the blank-slate (Hornet-only) capture to a PNG for comparison.</summary>
        public static void CaptureBlankToFile()
        {
            byte[] rgba;
            int w, h;
            float px, py, wx, wy;
            if (!CaptureBlankRgba(out rgba, out w, out h, out px, out py, out wx, out wy))
            {
                Plugin.Log.LogInfo("F1: blank capture found no Hornet pixels.");
                return;
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(rgba);
            tex.Apply();
            string dir = Path.Combine(BepInEx.Paths.PluginPath, "HornetExporter");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "hornet_blank.png"), tex.EncodeToPNG());
            Plugin.Log.LogInfo("F1: wrote hornet_blank.png " + w + "x" + h);
            Object.Destroy(tex);
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

        /// <summary>
        /// For the blank capture: hide every renderer under Hornet except her body (the renderers
        /// the main-camera diff toggles). Her hero-light/glow/dust effects sit on the same Player
        /// layer and would otherwise dominate the crop; disabling them does not darken the body
        /// (its colour comes from the global lighting, not the additive light sprite). Only
        /// renderers that were enabled are touched, and they are restored afterwards.
        /// </summary>
        private static readonly List<Renderer> _blankHidden = new List<Renderer>();

        private static void HideEffects(HeroController hero)
        {
            _blankHidden.Clear();
            for (int i = 0; i < _effectCache.Count; i++)
            {
                Renderer r = _effectCache[i];
                // S5 task 1: leave enabled HeroAttack renderers alone (slash arcs), so a swing can be
                // captured. Everything else under Hornet (hero light/glow/dust) is still suppressed.
                if (r != null && r.enabled && r.gameObject.layer != AttackLayer)
                {
                    _blankHidden.Add(r);
                    r.enabled = false;
                }
            }
        }

        private static void RestoreEffects()
        {
            for (int i = 0; i < _blankHidden.Count; i++)
            {
                if (_blankHidden[i] != null)
                {
                    _blankHidden[i].enabled = true;
                }
            }
            _blankHidden.Clear();
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
