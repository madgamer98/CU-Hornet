using System.Collections.Generic;
using HornetPassthrough;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// S1/S2 terrain mirror. Reads the host's local ground AABBs (absolute CU units) and rebuilds
    /// them as BoxCollider2Ds on layer 8 ("Terrain") — the layer Silksong's ground checks and physics
    /// already use (mask 0x2100 = layers 8 and 13), so no mask patches are needed.
    ///
    /// A fixed mapping captured on apply keeps the floor stable while Hornet moves:
    ///   cu(silk)   = _cuOrigin + (silk - _silkOrigin) / k
    ///   silk(cu)   = _silkOrigin + k * (cu - _cuOrigin)
    /// with k = HornetColliderHeight / CuPlayerColliderHeight.
    /// Vanilla terrain is disabled so only the imported floor supports her (F4 apply / F3 restore).
    /// </summary>
    internal static class TerrainMirror
    {
        private const int TerrainLayer = 8;       // "Terrain"
        private const int HeroDetectorLayer = 13; // "Hero Detector"

        private static readonly float[] Rects = new float[Proto.MaxRects * 4];
        private static readonly List<Collider2D> Disabled = new List<Collider2D>();
        private static readonly List<BoxCollider2D> Pool = new List<BoxCollider2D>();
        private static GameObject _root;
        private static Vector2 _cuOrigin;
        private static Vector2 _silkOrigin;
        private static float _k = 1f;
        private static int _lastRev = -1;
        private static int _hash;

        public static bool Active => _root != null;
        public static float Scale => _k;

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
            Pool.Clear();
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
            _lastRev = -1;
            _hash = 0;
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

            float cx, cy, cvx, cvy;
            int cfacing;
            bool cgrounded;
            int cflags;
            l.ReadCuState(out cx, out cy, out cvx, out cvy, out cfacing, out cgrounded, out cflags);

            Restore();
            _cuOrigin = new Vector2(cx, cy);
            _silkOrigin = new Vector2(hero.transform.position.x, hero.transform.position.y);
            _k = heroCol.bounds.size.y / playerHeight;
            _lastRev = revision;
            _hash = HashRects(count);

            _root = new GameObject("CuTerrainMirror");
            _root.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_root);

            DisableVanillaTerrain();
            BuildBoxes(count);

            Vector3 p = new Vector3(_silkOrigin.x, _silkOrigin.y + 3f, hero.transform.position.z);
            hero.transform.position = p;
            Rigidbody2D rb = hero.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.position = p;
                rb.linearVelocity = Vector2.zero;
            }

            Plugin.Log.LogInfo("TerrainMirror: applied " + count + " boxes k=" + _k.ToString("0.###") +
                               " hornetH=" + heroCol.bounds.size.y.ToString("0.##") +
                               " cuH=" + playerHeight.ToString("0.##") + " rev=" + revision);
        }

        /// <summary>Called each frame; rebuilds the proxy when the host's terrain window changes.</summary>
        public static void Poll(PassthroughLink l)
        {
            if (_root == null || l == null)
            {
                return;
            }
            int count, revision;
            float playerHeight;
            if (!l.ReadTerrain(Rects, out count, out revision, out playerHeight, ref _lastRev))
            {
                return;
            }
            int hash = HashRects(count);
            if (hash == _hash)
            {
                return;
            }
            _hash = hash;
            BuildBoxes(count);
        }

        public static bool TryMapToCu(Vector2 silk, out float cuX, out float cuY)
        {
            if (_root == null || _k <= 0.0001f)
            {
                cuX = 0f;
                cuY = 0f;
                return false;
            }
            cuX = _cuOrigin.x + (silk.x - _silkOrigin.x) / _k;
            cuY = _cuOrigin.y + (silk.y - _silkOrigin.y) / _k;
            return true;
        }

        public static bool TryMapToSilk(Vector2 cu, out Vector2 silk)
        {
            if (_root == null || _k <= 0.0001f)
            {
                silk = Vector2.zero;
                return false;
            }
            silk = new Vector2(
                _silkOrigin.x + (cu.x - _cuOrigin.x) * _k,
                _silkOrigin.y + (cu.y - _cuOrigin.y) * _k);
            return true;
        }

        private static int HashRects(int count)
        {
            int h = count * 397;
            for (int i = 0; i < count * 4; i++)
            {
                h = (h * 31) ^ Mathf.RoundToInt(Rects[i] * 8f);
            }
            return h;
        }

        private static void BuildBoxes(int count)
        {
            if (_root == null)
            {
                return;
            }
            EnsurePool(count);
            for (int i = 0; i < count; i++)
            {
                float rx = Rects[i * 4 + 0], ry = Rects[i * 4 + 1];
                float rw = Rects[i * 4 + 2], rh = Rects[i * 4 + 3];
                BoxCollider2D box = Pool[i];
                if (rw <= 0f || rh <= 0f)
                {
                    box.gameObject.SetActive(false);
                    continue;
                }
                box.gameObject.SetActive(true);
                box.transform.position = new Vector3(
                    _silkOrigin.x + (rx + rw * 0.5f - _cuOrigin.x) * _k,
                    _silkOrigin.y + (ry + rh * 0.5f - _cuOrigin.y) * _k,
                    0f);
                box.size = new Vector2(rw * _k, rh * _k);
            }
            for (int i = count; i < Pool.Count; i++)
            {
                Pool[i].gameObject.SetActive(false);
            }
            // Make the new geometry visible to physics this step, so there is never a gap.
            Physics2D.SyncTransforms();
        }

        private static void EnsurePool(int count)
        {
            while (Pool.Count < count)
            {
                var go = new GameObject("cu" + Pool.Count);
                go.layer = TerrainLayer;
                go.transform.SetParent(_root.transform, false);
                Pool.Add(go.AddComponent<BoxCollider2D>());
            }
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
    }
}
