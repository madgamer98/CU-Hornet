using HornetPassthrough;
using UnityEngine;

namespace HornetInCasualties
{
    /// <summary>
    /// Live passthrough (Casualties side, the host). Publishes the player's state and displays
    /// Hornet's isolated frame published by Silksong. No baked assets are used.
    /// </summary>
    public class LiveLink : MonoBehaviour
    {
        private PassthroughLink _link;
        private Body _body;
        private int _lastFrameId;

        private byte[] _buf;
        private byte[] _frameBytes;
        private Texture2D _tex;
        private SpriteRenderer _display;
        private Transform _displayT;
        private int _lastW, _lastH;
        private bool _loggedFrame;
        private int _diag;

        public void Init(Body body)
        {
            _body = body;
            _buf = new byte[Proto.PixelsSize];

            var go = new GameObject("HornetLive");
            go.transform.SetParent(body.transform, false);
            _displayT = go.transform;
            _display = go.AddComponent<SpriteRenderer>();
            _display.sortingOrder = 5000;
            _display.enabled = false;

            try
            {
                _link = new PassthroughLink(true);
                Plugin.Log.LogInfo("LiveLink: host mapping created " + Proto.MappingName);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("LiveLink host create failed: " + e.Message);
            }
        }

        private void LateUpdate()
        {
            if (_link == null || _body == null)
            {
                return;
            }

            _diag++;
            if (_diag % 120 == 0)
            {
                Plugin.Log.LogInfo("LiveLink diag: fid=" + _link.DebugFrameId + " w=" + _link.DebugWidth +
                                   " seq=" + _link.DebugSeq + " hasPx=" + _link.DebugHasPixels +
                                   " silkAlive=" + _link.SilkAlive());
            }

            _link.Heartbeat();
            float vx = _body.rb != null ? _body.rb.velocity.x : 0f;
            float vy = _body.rb != null ? _body.rb.velocity.y : 0f;
            Vector3 p = _body.transform.position;

            int flags = Proto.FlagEnabled;
            if (Input.GetKey(Plugin.KeySlash.Value.MainKey)) flags |= Proto.FlagAttack;
            if (Input.GetKey(Plugin.KeyDash.Value.MainKey)) flags |= Proto.FlagDash;
            if (Input.GetKey(Plugin.KeyNeedle.Value.MainKey)) flags |= Proto.FlagNeedle;
            if (Input.GetKey(KeyCode.W)) flags |= Proto.FlagUp;
            if (Input.GetKey(KeyCode.S)) flags |= Proto.FlagDown;

            _link.WriteCuState(p.x, p.y, vx, vy, _body.isRight ? 1 : -1, Grounded(vy), flags);

            int w, h, fid;
            float px, py, wx, wy;
            if (_link.ReadFrame(_buf, out w, out h, out px, out py, out wx, out wy, out fid, ref _lastFrameId))
            {
                if (w > 1 && h > 1)
                {
                    ApplyFrame(w, h, px, py);
                }
            }

            HideVanillaBody();

            if (_display != null && _display.enabled)
            {
                float scale = Plugin.AvatarScale.Value;
                _displayT.position = new Vector3(
                    p.x + Plugin.AvatarOffsetX.Value * scale,
                    p.y + Plugin.AvatarOffsetY.Value * scale,
                    p.z - 0.02f);
                _displayT.rotation = Quaternion.identity;
                _displayT.localScale = new Vector3(scale, scale, 1f);
                _display.flipX = !_body.isRight;
            }
        }

        // CU's ragdoll `grounded` flag is unreliable; use a feet-box test instead.
        private bool Grounded(float vy)
        {
            if (_body.col == null)
            {
                return _body.grounded;
            }
            if (Mathf.Abs(vy) > 0.6f)
            {
                return false;
            }
            Vector2 size = new Vector2(Mathf.Max(_body.col.size.x * 0.9f, 0.3f), 0.22f);
            Vector2 pos = (Vector2)_body.transform.position + _body.col.offset + Vector2.down * 0.06f;
            return Physics2D.OverlapBox(pos, size, 0f, LayerMask.GetMask("Ground")) != null;
        }

        // Hide the vanilla experiment's sprites the same way the baked port did.
        private void HideVanillaBody()
        {
            if (!Plugin.HideVanillaBody.Value)
            {
                return;
            }
            foreach (SpriteRenderer sr in _body.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == _display || sr.transform.IsChildOf(_displayT))
                {
                    continue;
                }
                sr.enabled = false;
            }
        }

        private void ApplyFrame(int w, int h, float px, float py)
        {
            if (w > 320 || h > 320)
            {
                return; // guard against a bad capture
            }
            // Ignore sudden size jumps (the diff occasionally catches a large effect), which would
            // otherwise render Hornet huge for a frame.
            if (_tex != null && (w > _lastW * 1.6f || h > _lastH * 1.6f || w < _lastW * 0.6f || h < _lastH * 0.6f))
            {
                return;
            }
            if (_tex == null || _lastW != w || _lastH != h)
            {
                _lastW = w;
                _lastH = h;
                _frameBytes = new byte[w * h * 4];
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                _display.sprite = Sprite.Create(_tex, new Rect(0, 0, w, h), new Vector2(px, py), 64f);
                _display.enabled = true;
            }
            System.Array.Copy(_buf, 0, _frameBytes, 0, _frameBytes.Length);
            _tex.LoadRawTextureData(_frameBytes);
            _tex.Apply();
            if (!_loggedFrame)
            {
                _loggedFrame = true;
                Plugin.Log.LogInfo("LiveLink: received live Hornet frame " + w + "x" + h);
            }
        }

        private void OnDestroy()
        {
            _link?.Dispose();
        }
    }
}
