using HornetPassthrough;
using UnityEngine;

namespace HornetInCasualties
{
    /// <summary>
    /// Live passthrough (Casualties side, the host). Publishes the player's state to Silksong and
    /// draws Hornet's published frame as a sprite. This replaces the baked-frame port while active.
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

            _link.Heartbeat();
            float vx = _body.rb != null ? _body.rb.velocity.x : 0f;
            float vy = _body.rb != null ? _body.rb.velocity.y : 0f;
            Vector3 p = _body.transform.position;
            _link.WriteCuState(p.x, p.y, vx, vy, _body.isRight ? 1 : -1, _body.grounded, 1);

            int w, h, frameId;
            float px, py, wx, wy;
            if (_link.ReadFrame(_buf, out w, out h, out px, out py, out wx, out wy, out frameId, ref _lastFrameId))
            {
                ApplyFrame(w, h, px, py);
            }

            if (_display != null)
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
            if (w <= 0 || h <= 0)
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
        }

        private void OnDestroy()
        {
            _link?.Dispose();
        }
    }
}
