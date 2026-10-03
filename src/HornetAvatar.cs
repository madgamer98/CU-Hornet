using System.IO;
using UnityEngine;

namespace HornetInCasualties
{
    /// <summary>
    /// The visible Hornet that rides the vanilla player's physics body.
    /// Frames come from the user's own Silksong install and are drawn as quads
    /// (see HornetSprites); the clip is chosen from the vanilla Body's state.
    /// </summary>
    public class HornetAvatar : MonoBehaviour
    {
        private Body _body;
        private Transform _sprite;
        private SpriteRenderer _placeholder;
        private MeshRenderer _meshRenderer;
        private HornetMeshAnimator _animator;

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

            _placeholder = go.AddComponent<SpriteRenderer>();
            _placeholder.sprite = PlaceholderSprite.Create();
            _placeholder.sortingOrder = 5000;

            Plugin.Log.LogInfo("init step 1 (placeholder ok)");
            var filter = go.AddComponent<MeshFilter>();
            Plugin.Log.LogInfo("init step 1a (filter)");
            _meshRenderer = go.AddComponent<MeshRenderer>();
            Plugin.Log.LogInfo("init step 1b (renderer null=" + (_meshRenderer == null) + ")");
            if (_meshRenderer != null)
            {
                _meshRenderer.sortingOrder = 5001;
            }
            Plugin.Log.LogInfo("init step 1b2 (sorting ok)");
            _animator = go.AddComponent<HornetMeshAnimator>();
            Plugin.Log.LogInfo("init step 1c (animator null=" + (_animator == null) + ")");
            if (_animator != null)
            {
                _animator.Init(filter, _meshRenderer);
            }
            Plugin.Log.LogInfo("init step 2 (mesh components ok)");

            if (!HornetSprites.Ready)
            {
                string dir = Plugin.Instance != null && Plugin.Instance.Info != null
                    ? Path.GetDirectoryName(Plugin.Instance.Info.Location)
                    : null;
                Plugin.Log.LogInfo("init step 3 (dir=" + (dir ?? "<null>") + ")");
                HornetSprites.Load(dir);
            }

            if (HornetSprites.Ready)
            {
                _placeholder.enabled = false;
                _animator.Play("Idle");
                Plugin.Log.LogInfo("Hornet avatar using extracted frames.");
            }
        }

        private void LateUpdate()
        {
            if (_body == null || _sprite == null)
            {
                return;
            }

            bool ready = HornetSprites.Ready;
            _placeholder.enabled = !ready && Plugin.Enable.Value;
            _meshRenderer.enabled = ready && Plugin.Enable.Value;
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

            if (ready)
            {
                _animator.Play(ChooseClip());
            }

            if (Plugin.HideVanillaBody.Value && _body.limbs != null)
            {
                foreach (Limb limb in _body.limbs)
                {
                    if (limb == null)
                    {
                        continue;
                    }
                    SpriteRenderer sr = limb.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        sr.enabled = false;
                    }
                }
            }
        }

        private string ChooseClip()
        {
            float vy = _body.rb != null ? _body.rb.velocity.y : 0f;
            float vx = _body.rb != null ? _body.rb.velocity.x : 0f;

            if (!_body.grounded)
            {
                return vy > 1.5f ? "Jump" : "Fall";
            }

            return Mathf.Abs(vx) > 0.4f ? "Run" : "Idle";
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
                        c = new Color32(220, 60, 60, 255);
                    }
                    else if (torso || legs)
                    {
                        c = new Color32(240, 235, 230, 255);
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
