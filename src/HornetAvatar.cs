using UnityEngine;

namespace HornetInCasualties
{
    /// <summary>
    /// The visible Hornet that rides the vanilla player's physics body.
    /// Stage 1 uses a generated placeholder sprite; later stages swap in the real
    /// Hornet frames loaded from the user's Silksong install.
    /// </summary>
    public class HornetAvatar : MonoBehaviour
    {
        private Body _body;
        private SpriteRenderer _renderer;

        public static HornetAvatar Ensure(Body body)
        {
            if (body == null)
            {
                return null;
            }

            HornetAvatar existing = body.GetComponent<HornetAvatar>();
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject("HornetAvatar");
            go.transform.SetParent(body.transform, false);
            var avatar = go.AddComponent<HornetAvatar>();
            avatar.Init(body);
            Plugin.Log.LogInfo("HornetAvatar attached to the player.");
            return avatar;
        }

        private void Init(Body body)
        {
            _body = body;
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = PlaceholderSprite.Create();
            _renderer.sortingOrder = 5000;
        }

        private void LateUpdate()
        {
            if (_body == null)
            {
                Destroy(gameObject);
                return;
            }

            Transform anchor = _body.baseLimb != null ? _body.baseLimb.transform : _body.transform;
            Vector3 p = anchor.position;
            transform.position = new Vector3(p.x, p.y, p.z - 0.02f);
            transform.rotation = Quaternion.identity;

            _renderer.flipX = !_body.isRight;
            _renderer.enabled = Plugin.Enable.Value;
        }
    }

    /// <summary>A generated stand-in so the render hook can be proven before real art exists.</summary>
    internal static class PlaceholderSprite
    {
        private const int W = 24;
        private const int H = 40;
        private static Sprite _cached;

        public static Sprite Create()
        {
            if (_cached != null)
            {
                return _cached;
            }

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    bool head = y >= 30;
                    bool torso = y >= 8 && y < 32 && x >= 8 && x < 16;
                    bool legs = y < 8 && x >= 9 && x < 15;
                    Color32 c;
                    if (head)
                    {
                        c = new Color32(220, 60, 60, 255);      // Hornet red
                    }
                    else if (torso || legs)
                    {
                        c = new Color32(240, 235, 230, 255);    // pale cloak/body
                    }
                    else
                    {
                        c = new Color32(0, 0, 0, 0);
                    }
                    px[y * W + x] = c;
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            _cached = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 16f);
            return _cached;
        }
    }
}
