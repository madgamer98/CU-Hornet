using UnityEngine;
using UnityEngine.UI;

namespace HornetInCasualties
{
    /// <summary>
    /// S5 2B: draws Silksong's HUD (published half-res by the guest) over CU's world on a screen-space
    /// canvas. The texture is only updated when a new HUD frame arrives, which is on-change.
    /// </summary>
    public class HudOverlay : MonoBehaviour
    {
        private static HudOverlay _instance;

        private Texture2D _tex;
        private RawImage _image;
        private byte[] _buf;
        private int _lastW, _lastH;

        public static HudOverlay Ensure()
        {
            if (_instance != null)
            {
                return _instance;
            }
            var go = new GameObject("HornetHudOverlay");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<HudOverlay>();
            return _instance;
        }

        public static void Hide()
        {
            if (_instance != null && _instance._image != null)
            {
                _instance._image.enabled = false;
            }
        }

        private void Awake()
        {
            var canvasGo = new GameObject("HudCanvas");
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            var imgGo = new GameObject("HudImage");
            imgGo.transform.SetParent(canvasGo.transform, false);
            _image = imgGo.AddComponent<RawImage>();
            _image.raycastTarget = false; // never intercept CU's mouse-driven attack
            _image.enabled = false;
            RectTransform rt = _image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public void SetFrame(byte[] rgba, int width, int height)
        {
            if (rgba == null || width < 1 || height < 1)
            {
                return;
            }
            if (_tex == null || _lastW != width || _lastH != height)
            {
                _lastW = width;
                _lastH = height;
                _buf = new byte[width * height * 4];
                if (_tex != null)
                {
                    Destroy(_tex);
                }
                _tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear
                };
                _image.texture = _tex;
            }
            int n = Mathf.Min(rgba.Length, _buf.Length);
            System.Array.Copy(rgba, 0, _buf, 0, n);
            _tex.LoadRawTextureData(_buf);
            _tex.Apply();
            _image.enabled = true;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
