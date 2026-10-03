using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// S5 2B: renders Silksong's HUD camera to a half-resolution RenderTexture with a transparent
    /// clear and reads it back, so the host can draw the real HUD over its world. Published
    /// on-change by <see cref="LiveLink"/> (not every frame) to keep the shared-memory traffic small.
    /// </summary>
    internal static class HudCapture
    {
        private static RenderTexture _rt;
        private static Texture2D _tex;
        private static int _w, _h;
        private static Color32[] _buf;
        private static byte[] _rgba;

        public static bool Capture(out byte[] rgba, out int width, out int height)
        {
            rgba = null;
            width = height = 0;

            if (GameCameras.instance == null)
            {
                return false;
            }
            Camera cam = GameCameras.instance.hudCamera;
            if (cam == null || !cam.gameObject.activeInHierarchy)
            {
                return false; // HUD camera inactive/off-screen (transition, menus); skip this frame
            }

            int w = Mathf.Max(1, Screen.width / 2);
            int h = Mathf.Max(1, Screen.height / 2);
            if (w > HornetPassthrough.Proto.MaxHudWidth || h > HornetPassthrough.Proto.MaxHudHeight)
            {
                Plugin.Log.LogWarning("HudCapture: half-res " + w + "x" + h +
                                      " exceeds protocol max " + HornetPassthrough.Proto.MaxHudWidth + "x" +
                                      HornetPassthrough.Proto.MaxHudHeight + "; skipping.");
                return false;
            }

            if (_rt == null || _w != w || _h != h)
            {
                if (_rt != null)
                {
                    _rt.Release();
                }
                _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
                _rt.Create();
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                _w = w;
                _h = h;
                _buf = new Color32[w * h];
                _rgba = new byte[w * h * 4];
            }

            CameraClearFlags prevClear = cam.clearFlags;
            Color prevBg = cam.backgroundColor;
            RenderTexture prevTarget = cam.targetTexture;
            try
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.targetTexture = _rt;
                cam.Render();
                RenderTexture.active = _rt;
                _tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                _tex.Apply();
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = prevTarget;
                cam.clearFlags = prevClear;
                cam.backgroundColor = prevBg;
            }

            _tex.GetRawTextureData<Color32>().CopyTo(_buf);

            // Skip frames with no HUD content (fades/transitions render an empty overlay).
            bool any = false;
            for (int i = 0; i < _buf.Length; i++)
            {
                if (_buf[i].a > 8)
                {
                    any = true;
                    break;
                }
            }
            if (!any)
            {
                return false;
            }

            int o = 0;
            for (int i = 0; i < _buf.Length; i++)
            {
                Color32 c = _buf[i];
                _rgba[o++] = c.r;
                _rgba[o++] = c.g;
                _rgba[o++] = c.b;
                _rgba[o++] = c.a;
            }
            rgba = _rgba;
            width = w;
            height = h;
            return true;
        }
    }
}
