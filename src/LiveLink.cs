using System.Collections.Generic;
using HornetPassthrough;
using UnityEngine;

namespace HornetInCasualties
{
    /// <summary>
    /// Live passthrough (Casualties side, the host). Publishes the player's state and displays
    /// Hornet's isolated frame published by Silksong. No baked assets are used.
    /// In S2 the host is also a puppet: Silksong's Hornet drives, and this sets the CU body to the
    /// mapped position so the camera and rendering follow her.
    /// </summary>
    public class LiveLink : MonoBehaviour
    {
        /// <summary>True while CU's body is driven by Silksong (Body.FixedUpdate is skipped).</summary>
        public static bool Puppeting;

        private PassthroughLink _link;
        private Body _body;
        private int _lastFrameId;
        private readonly List<Rigidbody2D> _frozen = new List<Rigidbody2D>();
        private readonly List<RigidbodyType2D> _frozenTypes = new List<RigidbodyType2D>();

        private byte[] _buf;
        private byte[] _frameBytes;
        private Texture2D _tex;
        private SpriteRenderer _display;
        private Transform _displayT;
        private int _lastW, _lastH;
        private bool _loggedFrame;
        private int _diag;

        private float _terrainTimer;
        private int _terrainRev;
        private readonly float[] _rectBuf = new float[Proto.MaxRects * 4];
        private readonly Collider2D[] _overlapBuf = new Collider2D[Proto.MaxRects];
        private static readonly int GroundMask = LayerMask.GetMask("Ground");

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
            // S0: forward the host's raw buttons so Silksong's own controller reacts to them.
            _link.WriteInput(ReadButtons());

            // S2: adopt Hornet's mapped position as CU's own, so the camera/render follow her.
            float hx, hy, hvx, hvy;
            int hfacing;
            bool hgrounded, hactive;
            if (_link.ReadPlayerState(out hx, out hy, out hvx, out hvy, out hfacing, out hgrounded, out hactive))
            {
                if (hactive)
                {
                    if (!Puppeting)
                    {
                        EnablePuppet();
                    }
                    PuppetTo(hx, hy, hfacing);
                }
                else if (Puppeting)
                {
                    DisablePuppet();
                }
            }

            // S1: publish a local window of ground AABBs so Silksong can collide against CU's world.
            _terrainTimer -= Time.deltaTime;
            if (_terrainTimer <= 0f)
            {
                _terrainTimer = 0.25f;
                PublishTerrain();
            }

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
                Vector3 cur = _body.transform.position;
                _displayT.position = new Vector3(
                    cur.x + Plugin.AvatarOffsetX.Value * scale,
                    cur.y + Plugin.AvatarOffsetY.Value * scale,
                    cur.z - 0.02f);
                _displayT.rotation = Quaternion.identity;
                _displayT.localScale = new Vector3(scale, scale, 1f);
                _display.flipX = !_body.isRight;
            }
        }

        // CU's ragdoll `grounded` flag is unreliable, so combine it with a feet-box test and add
        // hysteresis: stay "grounded" briefly after the last contact to stop clip flapping.
        private bool Grounded(float vy)
        {
            bool raw = _body.grounded;
            if (!raw && _body.col != null)
            {
                Vector2 size = new Vector2(Mathf.Max(_body.col.size.x * 0.9f, 0.3f), 0.22f);
                Vector2 pos = (Vector2)_body.transform.position + _body.col.offset + Vector2.down * 0.06f;
                raw = Physics2D.OverlapBox(pos, size, 0f, LayerMask.GetMask("Ground")) != null;
            }
            if (raw)
            {
                _lastGroundedTime = Time.time;
                return true;
            }
            // Grace window, unless clearly falling fast.
            return Time.time - _lastGroundedTime < 0.25f && vy > -3f;
        }

        private float _lastGroundedTime;

        /// <summary>
        /// Read the host's real binds into the wire bitfield. Movement/jump/attack use CU's own
        /// keybinds (so remaps are respected); dash/needle reuse the passthrough action keys.
        /// </summary>
        private int ReadButtons()
        {
            int b = Proto.BtnEnabled;
            if (Input.GetKey(KeyBinds.GetBind("left"))) b |= Proto.BtnLeft;
            if (Input.GetKey(KeyBinds.GetBind("right"))) b |= Proto.BtnRight;
            if (Input.GetKey(KeyBinds.GetBind("up"))) b |= Proto.BtnUp;
            if (Input.GetKey(KeyBinds.GetBind("down"))) b |= Proto.BtnDown;
            if (Input.GetKey(KeyBinds.GetBind("jump"))) b |= Proto.BtnJump;
            if (Input.GetKey(KeyBinds.GetBind("attack"))) b |= Proto.BtnAttack;
            if (Input.GetKey(Plugin.KeyDash.Value.MainKey)) b |= Proto.BtnDash;
            if (Input.GetKey(Plugin.KeyNeedle.Value.MainKey)) b |= Proto.BtnNeedle;
            return b;
        }

        /// <summary>Freeze CU's ragdoll (kinematic) so Silksong can drive the transform.</summary>
        private void EnablePuppet()
        {
            Puppeting = true;
            _frozen.Clear();
            _frozenTypes.Clear();
            foreach (Rigidbody2D rb in _body.GetComponentsInChildren<Rigidbody2D>(true))
            {
                if (rb == null)
                {
                    continue;
                }
                _frozen.Add(rb);
                _frozenTypes.Add(rb.bodyType);
                rb.bodyType = RigidbodyType2D.Kinematic;
            }
            Plugin.Log.LogInfo("LiveLink: S2 puppet enabled (" + _frozen.Count + " rigidbodies frozen).");
        }

        private void PuppetTo(float x, float y, int facing)
        {
            if (_body == null)
            {
                return;
            }
            Vector3 np = new Vector3(x, y, _body.transform.position.z);
            _body.transform.position = np;
            if (_body.rb != null)
            {
                _body.rb.position = new Vector2(x, y);
            }
            if (facing != 0)
            {
                _body.isRight = facing > 0;
            }
        }

        private void DisablePuppet()
        {
            Puppeting = false;
            for (int i = 0; i < _frozen.Count; i++)
            {
                if (_frozen[i] != null)
                {
                    _frozen[i].bodyType = _frozenTypes[i];
                }
            }
            _frozen.Clear();
            _frozenTypes.Clear();
            Plugin.Log.LogInfo("LiveLink: S2 puppet disabled.");
        }

        /// <summary>
        /// Collect ground colliders around the player as AABBs relative to the player's ground
        /// contact, in CU units, and publish them. Silksong scales them and builds a proxy.
        /// </summary>
        private void PublishTerrain()
        {
            if (_body == null || _body.col == null)
            {
                return;
            }

            Bounds cb = _body.col.bounds;
            Vector2 center = cb.center;

            // Anchor at the ground directly under the player so the floor maps to the proxy's y=0.
            float anchorY = cb.min.y;
            RaycastHit2D ground = Physics2D.Raycast(center, Vector2.down, cb.extents.y + 12f, GroundMask);
            if (ground.collider != null)
            {
                anchorY = ground.point.y;
            }
            Vector2 anchor = new Vector2(center.x, anchorY);

            const float half = 96f;
            int n = Physics2D.OverlapBoxNonAlloc(anchor, new Vector2(half * 2f, half * 2f), 0f,
                _overlapBuf, GroundMask);

            float minX = anchor.x - half, minY = anchor.y - half;
            float maxX = anchor.x + half, maxY = anchor.y + half;

            int count = 0;
            for (int i = 0; i < n && count < Proto.MaxRects; i++)
            {
                Collider2D c = _overlapBuf[i];
                if (c == null)
                {
                    continue;
                }
                Bounds b = c.bounds;
                float x0 = Mathf.Max(b.min.x, minX);
                float y0 = Mathf.Max(b.min.y, minY);
                float x1 = Mathf.Min(b.max.x, maxX);
                float y1 = Mathf.Min(b.max.y, maxY);
                if (x1 <= x0 || y1 <= y0)
                {
                    continue;
                }
                _rectBuf[count * 4 + 0] = x0;
                _rectBuf[count * 4 + 1] = y0;
                _rectBuf[count * 4 + 2] = x1 - x0;
                _rectBuf[count * 4 + 3] = y1 - y0;
                count++;
            }

            _terrainRev++;
            _link.WriteTerrain(cb.size.y, _rectBuf, count, _terrainRev);
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
