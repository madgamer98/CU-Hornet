using System.Collections.Generic;
using HornetPassthrough;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// S4 phase 1: rebuild CU actors as pogo-able proxy colliders in Silksong, at the same fixed
    /// mapping as the terrain mirror. Pogo discovery reuses Silksong's own down-spike path:
    /// a collider on layer 19 (INTERACTIVE_OBJECT) or 17 (HERO_ATTACK) makes
    /// HeroDownAttack.ContinueBounceTrigger fire without needing a heavyweight HealthManager.
    /// </summary>
    internal static class EntityProxies
    {
        private static readonly float[] Values = new float[Proto.MaxEntities * Proto.EntityStrideFloats];
        private static readonly List<GameObject> Pool = new List<GameObject>();
        private static GameObject _root;
        private static int _lastRev = -1;
        private static int _layer = -1;

        private static readonly int AttackLayer = 17; // PhysLayers.HERO_ATTACK

        private static int ProxyLayer
        {
            get
            {
                if (_layer < 0)
                {
                    bool ignore1719 = Physics2D.GetIgnoreLayerCollision(AttackLayer, 19);
                    bool ignore1717 = Physics2D.GetIgnoreLayerCollision(AttackLayer, AttackLayer);
                    _layer = !ignore1719 ? 19 : (!ignore1717 ? 17 : 19);
                    Plugin.Log.LogInfo("EntityProxies: ignore 17<->19=" + ignore1719 +
                                       " 17<->17=" + ignore1717 + " -> proxy layer " + _layer);
                }
                return _layer;
            }
        }

        public static void Poll(PassthroughLink l)
        {
            if (!TerrainMirror.Active)
            {
                Clear();
                return;
            }
            if (l == null)
            {
                return;
            }

            int count, revision;
            if (!l.ReadEntities(Values, out count, out revision, ref _lastRev))
            {
                return;
            }

            if (_root == null)
            {
                _root = new GameObject("CuEntities");
                _root.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(_root);
            }
            EnsurePool(count);

            float k = TerrainMirror.Scale;
            for (int i = 0; i < count; i++)
            {
                int v = i * Proto.EntityStrideFloats;
                float cuX = Values[v + 1], cuY = Values[v + 2];
                float w = Values[v + 3], h = Values[v + 4];
                int flags = Mathf.RoundToInt(Values[v + 7]);

                GameObject go = Pool[i];
                Vector2 silk;
                if ((flags & Proto.EntFlagAlive) == 0 ||
                    !TerrainMirror.TryMapToSilk(new Vector2(cuX, cuY), out silk))
                {
                    go.SetActive(false);
                    continue;
                }

                go.SetActive(true);
                go.layer = ProxyLayer;
                go.transform.position = new Vector3(silk.x, silk.y, 0f);
                BoxCollider2D box = go.GetComponent<BoxCollider2D>();
                box.isTrigger = true;
                box.size = new Vector2(Mathf.Max(w, 0.2f) * k, Mathf.Max(h, 0.2f) * k);
            }
            for (int i = count; i < Pool.Count; i++)
            {
                Pool[i].SetActive(false);
            }
            Physics2D.SyncTransforms();

            if (count != _lastLogged)
            {
                _lastLogged = count;
                Plugin.Log.LogInfo("EntityProxies: " + count + " proxies, layer " + ProxyLayer);
                LogHeroLayersOnce();
            }
        }

        private static int _lastLogged = -1;
        private static bool _loggedHero;

        private static void LogHeroLayersOnce()
        {
            if (_loggedHero)
            {
                return;
            }
            _loggedHero = true;
            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                return;
            }
            var sb = new System.Text.StringBuilder("EntityProxies hero colliders: ");
            foreach (Collider2D c in hero.GetComponentsInChildren<Collider2D>(true))
            {
                sb.Append(c.gameObject.name).Append("(L").Append(c.gameObject.layer)
                  .Append(c.isTrigger ? "T" : "S").Append(") ");
            }
            Plugin.Log.LogInfo(sb.ToString());

            var sb2 = new System.Text.StringBuilder("ignored vs layer 19: ");
            for (int l = 0; l < 32; l++)
            {
                if (Physics2D.GetIgnoreLayerCollision(l, 19))
                {
                    sb2.Append(l).Append(' ');
                }
            }
            Plugin.Log.LogInfo(sb2.ToString());
        }

        private static void EnsurePool(int count)
        {
            while (Pool.Count < count)
            {
                var go = new GameObject("ent" + Pool.Count);
                go.transform.SetParent(_root.transform, false);
                go.AddComponent<BoxCollider2D>();
                Pool.Add(go);
            }
        }

        private static void Clear()
        {
            Pool.Clear();
            _lastRev = -1;
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }
    }
}
