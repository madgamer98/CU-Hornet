using System.Collections.Generic;
using HornetPassthrough;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// S1 terrain mirror. Reads the host's local ground AABBs (CU units) and rebuilds them as
    /// BoxCollider2Ds on layer 8 ("Terrain"), which is exactly the layer Silksong's ground checks
    /// and physics already use (mask 0x2100 = layers 8 and 13). Vanilla terrain colliders are
    /// disabled for the test so Hornet can only stand on the imported floor.
    /// Triggered manually with F4 (apply) / F3 (restore).
    /// </summary>
    internal static class TerrainMirror
    {
        private const int TerrainLayer = 8;       // "Terrain"
        private const int HeroDetectorLayer = 13; // "Hero Detector"

        private static readonly float[] Rects = new float[Proto.MaxRects * 4];
        private static readonly List<Collider2D> Disabled = new List<Collider2D>();
        private static GameObject _root;
        private static Vector2 _anchor;

        public static bool Active => _root != null;

        public static void Restore()
        {
            for (int i = 0; i < Disabled.Count; i++)
            {
                if (Disabled[i] != null)
                {
                    Disabled[i].enabled = true;
                }
            }
            Disabled.Clear();
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
            Plugin.Log.LogInfo("TerrainMirror: restored vanilla terrain.");
        }

        public static void Apply()
        {
            LiveLink link = LiveLink.Instance;
            PassthroughLink l = link != null ? link.Link : null;
            if (l == null)
            {
                Plugin.Log.LogWarning("TerrainMirror: no link to host.");
                return;
            }

            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                Plugin.Log.LogWarning("TerrainMirror: no HeroController.");
                return;
            }
            Collider2D heroCol = hero.GetComponent<Collider2D>();
            if (heroCol == null)
            {
                Plugin.Log.LogWarning("TerrainMirror: hero has no Collider2D.");
                return;
            }

            int last = -1;
            int count, revision;
            float playerHeight;
            if (!l.ReadTerrain(Rects, out count, out revision, out playerHeight, ref last))
            {
                Plugin.Log.LogWarning("TerrainMirror: no terrain window from host yet.");
                return;
            }
            if (count == 0 || playerHeight <= 0.001f)
            {
                Plugin.Log.LogWarning("TerrainMirror: empty terrain window.");
                return;
            }

            // One scale factor: Hornet's collider height per CU player collider height.
            Bounds hb = heroCol.bounds;
            float k = hb.size.y / playerHeight;

            Restore();
            _anchor = new Vector2(hb.center.x, hb.min.y);

            _root = new GameObject("CuTerrainMirror");
            _root.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_root);

            DisableVanillaTerrain();

            for (int i = 0; i < count; i++)
            {
                float rx = Rects[i * 4 + 0], ry = Rects[i * 4 + 1];
                float rw = Rects[i * 4 + 2], rh = Rects[i * 4 + 3];
                if (rw <= 0f || rh <= 0f)
                {
                    continue;
                }
                var go = new GameObject("cu" + i);
                go.layer = TerrainLayer;
                go.transform.SetParent(_root.transform, false);
                go.transform.position = new Vector3(
                    _anchor.x + (rx + rw * 0.5f) * k,
                    _anchor.y + (ry + rh * 0.5f) * k,
                    0f);
                BoxCollider2D box = go.AddComponent<BoxCollider2D>();
                box.size = new Vector2(rw * k, rh * k);
            }

            TeleportHeroAbove(hero, _anchor.y + 3f);
            Plugin.Log.LogInfo("TerrainMirror: applied " + count + " boxes k=" + k.ToString("0.###") +
                               " hornetH=" + hb.size.y.ToString("0.##") + " cuH=" + playerHeight.ToString("0.##") +
                               " anchor=(" + _anchor.x.ToString("0.0") + "," + _anchor.y.ToString("0.0") +
                               ") rev=" + revision);
        }

        private static void DisableVanillaTerrain()
        {
            Collider2D[] cols = UnityEngine.Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D c = cols[i];
                if (c == null || !c.enabled)
                {
                    continue;
                }
                if (_root != null && c.transform.IsChildOf(_root.transform))
                {
                    continue;
                }
                int layer = c.gameObject.layer;
                if (layer == TerrainLayer || layer == HeroDetectorLayer)
                {
                    c.enabled = false;
                    Disabled.Add(c);
                }
            }
            Plugin.Log.LogInfo("TerrainMirror: disabled " + Disabled.Count + " vanilla terrain colliders.");
        }

        private static void TeleportHeroAbove(HeroController hero, float y)
        {
            Vector3 p = new Vector3(_anchor.x, y, hero.transform.position.z);
            hero.transform.position = p;
            Rigidbody2D rb = hero.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.position = p;
                rb.linearVelocity = Vector2.zero;
            }
        }
    }
}
