using System.IO;
using UnityEngine;

namespace HornetInCasualties
{
    /// <summary>
    /// The visible Hornet that rides the vanilla player's physics body.
    /// Frames are baked from the user's own Silksong install (Silksong HornetExporter plugin)
    /// and drawn on a SpriteRenderer; the clip is chosen from the vanilla Body's state.
    /// </summary>
    public class HornetAvatar : MonoBehaviour
    {
        private Body _body;
        private Transform _sprite;
        private SpriteRenderer _renderer;
        private HornetAnimator _animator;
        private string _actionClip;
        private float _actionUntil;

        /// <summary>Play a one-shot action clip (slash, dash, ...) then return to locomotion.</summary>
        public void PlayAction(string name, float duration)
        {
            _actionClip = name;
            _actionUntil = Time.time + duration;
            if (_animator != null)
            {
                _animator.Play(name, true);
            }
        }

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
            HornetAvatar avatar = body.gameObject.AddComponent<HornetAvatar>();
            try
            {
                avatar.Init(body);
                Plugin.Log.LogInfo("HornetAvatar attached to the player.");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("HornetAvatar init failed: " + e);
            }
            return avatar;
        }

        private void Init(Body body)
        {
            _body = body;

            var go = new GameObject("HornetSprite");
            go.transform.SetParent(body.transform, false);
            _sprite = go.transform;

            _renderer = go.AddComponent<SpriteRenderer>();
            _renderer.sortingOrder = 5000;
            _animator = go.AddComponent<HornetAnimator>();
            _animator.Init(_renderer);

            if (!HornetSprites.Ready)
            {
                HornetSprites.Load(Path.GetDirectoryName(Plugin.Instance.Info.Location));
            }
            if (HornetSprites.Ready)
            {
                _animator.Play("Idle");
            }
            else
            {
                _renderer.sprite = PlaceholderSprite.Create();
            }

            if (Plugin.EnableMoves.Value)
            {
                HornetController controller = body.gameObject.GetComponent<HornetController>();
                if (controller == null)
                {
                    controller = body.gameObject.AddComponent<HornetController>();
                }
                controller.Init(this, body);
            }
        }

        private void LateUpdate()
        {
            if (_body == null || _sprite == null)
            {
                return;
            }

            _renderer.enabled = Plugin.Enable.Value;
            if (!Plugin.Enable.Value)
            {
                return;
            }

            Vector3 p = _body.transform.position;
            float scale = Plugin.AvatarScale.Value;
            _sprite.position = new Vector3(
                p.x + Plugin.AvatarOffsetX.Value * scale,
                p.y + Plugin.AvatarOffsetY.Value * scale,
                p.z - 0.02f);
            _sprite.rotation = Quaternion.identity;
            float sx = _body.isRight ? scale : -scale;
            _sprite.localScale = new Vector3(sx, scale, 1f);

            if (HornetSprites.Ready)
            {
                if (!string.IsNullOrEmpty(_actionClip) && Time.time < _actionUntil)
                {
                    _animator.Play(_actionClip);
                }
                else
                {
                    _actionClip = null;
                    _animator.Play(ChooseClip());
                }
            }

            if (Plugin.HideVanillaBody.Value)
            {
                foreach (SpriteRenderer sr in _body.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (sr == _renderer || sr.transform.IsChildOf(_sprite))
                    {
                        continue;
                    }
                    sr.enabled = false;
                }
            }
        }

        private string _lastClip;

        private string ChooseClip()
        {
            float vy = _body.rb != null ? _body.rb.velocity.y : 0f;
            float vx = _body.rb != null ? _body.rb.velocity.x : 0f;

            string clip;
            if (!_body.grounded && vy > 1f)
            {
                clip = "Airborne";
            }
            else if (!_body.grounded && vy < -0.5f)
            {
                clip = "Fall";
            }
            else if (Mathf.Abs(vx) > 0.5f)
            {
                clip = "Run";
            }
            else
            {
                clip = "Idle";
            }

            if (clip != _lastClip)
            {
                _lastClip = clip;
                Plugin.Log.LogInfo("Hornet clip -> " + clip + " (grounded=" + _body.grounded +
                                   " vx=" + vx.ToString("0.00") + " vy=" + vy.ToString("0.00") + ")");
            }
            return clip;
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
                    Color32 c = head ? new Color32(220, 60, 60, 255)
                        : (torso || legs) ? new Color32(240, 235, 230, 255)
                        : new Color32(0, 0, 0, 0);
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
