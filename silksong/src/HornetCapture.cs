using System.IO;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// Renders Hornet to a RenderTexture with a dedicated camera that only sees her layer,
    /// then reads it back. Used both for a one-time frame dump and, later, for the live link.
    /// </summary>
    internal static class HornetCapture
    {
        private static Camera _camera;
        private static RenderTexture _rt;
        private const int Size = 256;

        public static void CaptureToFile()
        {
            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                Plugin.Log.LogInfo("F8: no HeroController (not in gameplay).");
                return;
            }

            RenderTexture rt = Render(hero.gameObject);
            if (rt == null)
            {
                Plugin.Log.LogInfo("F8: render failed.");
                return;
            }

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            byte[] png = tex.EncodeToPNG();
            string path = Path.Combine(BepInEx.Paths.PluginPath, "HornetExporter", "hornet_capture.png");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, png);
            Plugin.Log.LogInfo("F8: wrote " + path + " (" + tex.width + "x" + tex.height + ")");
        }

        public static RenderTexture Render(GameObject hero)
        {
            EnsureCamera();
            if (_camera == null)
            {
                return null;
            }

            // Put Hornet on a dedicated layer so only she is captured.
            int layer = LayerMask.NameToLayer("HornetCapture");
            if (layer < 0)
            {
                Plugin.Log.LogInfo("Layer 'HornetCapture' does not exist; capturing all layers (will include scenery).");
                layer = hero.layer;
            }
            SetLayerRecursive(hero, layer);
            _camera.cullingMask = 1 << layer;
            _camera.transform.position = hero.transform.position + new Vector3(0f, 0f, -10f);
            _camera.transform.rotation = Quaternion.identity;
            _camera.Render();
            return _rt;
        }

        private static void EnsureCamera()
        {
            if (_camera != null)
            {
                return;
            }

            var go = new GameObject("HornetExporterCamera");
            Object.DontDestroyOnLoad(go);
            _camera = go.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.orthographic = true;
            _camera.orthographicSize = 5f;
            _camera.enabled = false; // manual Render()

            _rt = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32);
            _camera.targetTexture = _rt;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
            {
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
            }
        }
    }
}
