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
            _link.WriteCuState(p.x, p.y, vx, vy, _body.isRight ? 1 : -1, _body.grounded, 1);

            int w, h, fid;
            float px, py, wx, wy;
            if (_link.ReadFrame(_buf, out w, out h, out px, out py, out wx, out wy, out fid, ref _lastFrameId))
            {
                if (w > 1 && h > 1)
                {
                    ApplyFrame(w, h, px, py);
                }
            }

            if (_display != null && _display.enabled)
            {
                float scale = Plugin.AvatarScale.Value;
                _displayT.position = new Vector3(
                    p.x + Plugin.AvatarOffsetX.Value * scale,
                    p.y + Plugin.AvatarOffsetY.Value * scale,
                    p.z - 0.02f);
                _displayT.rotation = Quaternion.identity;
                _displayT.localScale = Vector3.one;
                _display.flipX = !_body.isRight;
            }
        }

        private void ApplyFrame(int w, int h, float px, float py)
        {
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
