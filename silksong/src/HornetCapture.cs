using System.Collections.Generic;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// Captures Hornet with the game's own main camera: <see cref="CaptureBlankRgba"/> renders her
    /// alone on alpha 0 via a narrowed culling mask (which keeps the game's lighting) into a fixed
    /// crop the host draws as a sprite.
    /// </summary>
    internal static class HornetCapture
    {
        // Fixed frame size so CU never reallocates its texture. Raised 320 -> 640 because a side slash
        // reached the 320 edge; the extra ring is transparent. Protocol v9 double-buffers the pixels
        // because a crop this big tears under a single-buffer seqlock.
        private const int BlankCrop = 640;
        // HeroAttack (PhysLayers.HERO_ATTACK). Hornet's slash arcs live here, separate from her body
        // on Player(9). They are kept visible in the blank render (see HideEffects).
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
        /// Because it is the same camera instance, the lit/global state is already correct (a dedicated
        /// offscreen camera came back dark). No environment, so no background bleeding into
        /// semi-transparent pixels.
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
            // CU can keep one texture/sprite and never reallocate.
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
            // Keep Hornet's attack VFX (HERO_ATTACK) in the render so slash arcs pass through. The
            // layer is hero-only, so no environment bleeds in.
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
        /// For the blank capture: hide every renderer under Hornet except her body (and enabled
        /// HeroAttack renderers - slash arcs). Her hero-light/glow/dust effects sit on the same Player
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
    }
}
