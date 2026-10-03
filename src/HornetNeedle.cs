using System.Collections.Generic;
using UnityEngine;

namespace HornetInCasualties
{
    /// <summary>
    /// Hornet's thrown needle: flies out, damages on contact, stops at terrain,
    /// then returns to her (a boomerang). Mirrors Silksong's NeedleThrow/Harpoon behaviour.
    /// </summary>
    public class HornetNeedle : MonoBehaviour
    {
        private Body _body;
        private HornetController _owner;
        private Vector2 _dir;
        private float _travelled;
        private bool _returning;
        private readonly HashSet<int> _hit = new HashSet<int>();
        private SpriteRenderer _renderer;

        public static HornetNeedle Spawn(Body body, bool right, HornetController owner)
        {
            var go = new GameObject("HornetNeedle");
            var needle = go.AddComponent<HornetNeedle>();
            needle._body = body;
            needle._owner = owner;
            needle._dir = right ? Vector2.right : Vector2.left;
            needle._renderer = go.AddComponent<SpriteRenderer>();
            needle._renderer.sprite = NeedleSprite.Create();
            needle._renderer.sortingOrder = 6000;
            needle._renderer.flipX = !right;
            go.transform.position = body.transform.position + (Vector3)(needle._dir * 0.6f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, right ? -45f : 45f);
            return needle;
        }

        private void Update()
        {
            if (_body == null || _owner == null)
            {
                Destroy(gameObject);
                return;
            }

            float dt = Time.deltaTime;
            if (!_returning)
            {
                float step = Plugin.NeedleSpeed.Value * dt;
                transform.position += (Vector3)(_dir * step);
                _travelled += step;

                if (HitWall() || _travelled >= Plugin.NeedleRange.Value)
                {
                    _returning = true;
                    _hit.Clear();
                    _renderer.flipX = !_renderer.flipX;
                }
            }
            else
            {
                Vector2 target = _body.transform.position;
                Vector2 pos = transform.position;
                Vector2 toTarget = target - pos;
                if (toTarget.magnitude < 0.6f)
                {
                    _owner.OnNeedleCaught();
                    Destroy(gameObject);
                    return;
                }
                _dir = toTarget.normalized;
                transform.position += (Vector3)(_dir * Plugin.NeedleReturnSpeed.Value * dt);
            }

            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg);
            DamageAlong();
        }

        private bool HitWall()
        {
            RaycastHit2D hit = Physics2D.Raycast(transform.position, _dir, 0.4f, LayerMask.GetMask("Ground"));
            return hit.collider != null;
        }

        private void DamageAlong()
        {
            Collider2D[] cols = Physics2D.OverlapCircleAll(transform.position, 0.5f);
            foreach (Collider2D col in cols)
            {
                var be = col.GetComponent<BuildingEntity>();
                if (be == null || be.cantHit)
                {
                    continue;
                }
                int id = col.gameObject.GetInstanceID();
                if (_hit.Contains(id))
                {
                    continue;
                }
                _hit.Add(id);
                be.health -= Plugin.NeedleDamage.Value;
                if (be.animal)
                {
                    col.gameObject.SendMessage("AnimalHit", Plugin.NeedleDamage.Value,
                        SendMessageOptions.DontRequireReceiver);
                }
                WorldGeneration.CreateDamageNumber(col.transform.position, (int)Plugin.NeedleDamage.Value);
            }
        }
    }

    internal static class NeedleSprite
    {
        private static Sprite _cached;

        public static Sprite Create()
        {
            if (_cached != null)
            {
                return _cached;
            }
            const int W = 40, H = 8;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[W * H];
            var shaft = new Color32(210, 210, 220, 255);
            var tip = new Color32(240, 240, 245, 255);
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    bool band = y >= 3 && y <= 4;
                    bool eye = x < 5 && (y == 1 || y == 6 || (x < 2 && y >= 1 && y <= 6));
                    Color32 c = eye ? new Color32(160, 160, 175, 255)
                        : (band ? (x > W - 6 ? tip : shaft) : new Color32(0, 0, 0, 0));
                    px[y * W + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _cached = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 32f);
            return _cached;
        }
    }
}
