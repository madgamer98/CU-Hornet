using System.Collections.Generic;
using HarmonyLib;
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
        private readonly List<Transform> _limbT = new List<Transform>();
        private readonly List<Rigidbody2D> _limbRb = new List<Rigidbody2D>();
        private readonly List<Vector3> _limbOff = new List<Vector3>();

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
        private readonly List<int[]> _openRects = new List<int[]>();
        private readonly List<int[]> _curRuns = new List<int[]>();
        private readonly Collider2D[] _groundHits = new Collider2D[128];
        private const float TerrainMargin = 0.05f;
        private static readonly int GroundMask = LayerMask.GetMask("Ground");

        private readonly float[] _entBuf = new float[Proto.MaxEntities * Proto.EntityStrideFloats];
        private readonly Collider2D[] _entHits = new Collider2D[256];
        private readonly List<Collider2D> _entCols = new List<Collider2D>();
        private readonly HashSet<int> _entSeen = new HashSet<int>();
        private readonly List<GameObject> _dummies = new List<GameObject>();
        private readonly List<float> _dummyHp = new List<float>();
        private readonly Dictionary<int, BuildingEntity> _entityById = new Dictionary<int, BuildingEntity>();
        private readonly List<int> _deadIds = new List<int>();
        private Sprite _dummySprite;
        private int _entRev;
        private int _eventReadIndex;
        private float _pupX, _pupY;
        private bool _pupValid;
        private bool _vtValid;
        private int _vtHealth, _vtMax, _vtBlue, _vtSilk, _vtSilkMax, _vtGeo;
        private bool _vtDead;
        private byte[] _hudBuf;
        private int _lastHudFrameId;

        public void Init(Body body)
        {
            if (_display != null)
            {
                return; // already initialised (guards against a double display)
            }
            _body = body;
            _buf = new byte[Proto.PixelsSize];
            _hudBuf = new byte[Proto.HudPixelsSize];

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
            if (_diag % 300 == 0)
            {
                LogDisplayDiag();
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
                // Honor "active" only while Silksong is actually heartbeating; otherwise a stale
                // active flag would re-puppet CU after the guest is gone.
                bool live = hactive && _link.SilkAlive(1000);
                if (live)
                {
                    if (!Puppeting)
                    {
                        EnablePuppet();
                    }
                    PuppetTo(hx, hy, hfacing);
                    _pupX = hx;
                    _pupY = hy;
                    _pupValid = true;
                }
                else if (Puppeting)
                {
                    Plugin.Log.LogWarning("LiveLink: Silksong inactive/heartbeat lost; releasing puppet.");
                    DisablePuppet();
                }
            }

            // S5 2A: read Silksong's vitals (the source of truth) and log them on change so they can
            // be checked against Silksong's HUD. The HUD overlay itself is 2B.
            int vh, vmax, vblue, vsilk, vsmax, vgeo;
            bool vdead;
            if (_link.ReadVitals(out vh, out vmax, out vblue, out vsilk, out vsmax, out vgeo, out vdead))
            {
                if (!_vtValid || vh != _vtHealth || vmax != _vtMax || vblue != _vtBlue ||
                    vsilk != _vtSilk || vsmax != _vtSilkMax || vgeo != _vtGeo || vdead != _vtDead)
                {
                    _vtValid = true;
                    _vtHealth = vh; _vtMax = vmax; _vtBlue = vblue; _vtSilk = vsilk;
                    _vtSilkMax = vsmax; _vtGeo = vgeo; _vtDead = vdead;
                    Plugin.Log.LogInfo("LiveLink vitals: hp=" + vh + "/" + vmax + " blue=" + vblue +
                                       " silk=" + vsilk + "/" + vsmax + " geo=" + vgeo +
                                       " dead=" + vdead);
                }
            }

            // S5 2B: draw Silksong's HUD over CU's world (published on change, half-res). Hide it when
            // the guest is gone so a stale HUD does not linger.
            int hudW, hudH, hudFid;
            if (_link.ReadHud(_hudBuf, out hudW, out hudH, out hudFid, ref _lastHudFrameId))
            {
                if (hudW > 1 && hudH > 1)
                {
                    HudOverlay.Ensure().SetFrame(_hudBuf, hudW, hudH);
                }
            }
            if (!_link.SilkAlive(1000))
            {
                HudOverlay.Hide();
            }

            // S1: publish a local window of ground AABBs so Silksong can collide against CU's world.
            _terrainTimer -= Time.deltaTime;
            if (_terrainTimer <= 0f)
            {
                _terrainTimer = 0.25f;
                PublishTerrain();
                PublishEntities();
            }

            if (Plugin.DebugKeys.Value && Input.GetKeyDown(KeyCode.F7))
            {
                SpawnDummy();
            }

            DrainEvents();

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

            // S5 2C: second, late-frame pass so damage applied after Body.Update does not linger a
            // whole frame.
            if (Puppeting)
            {
                DamagePinner.Pin(_body);
            }

            if (_display != null && _display.enabled)
            {
                float scale = Plugin.AvatarScale.Value;
                Vector3 cur = _body.transform.position;
                _displayT.position = new Vector3(
                    cur.x + Plugin.AvatarOffsetX.Value * scale,
                    cur.y + Plugin.AvatarOffsetY.Value * scale,
                    cur.z - 0.02f);
                _displayT.rotation = Quaternion.identity;
                // CU's Body flips its own root transform scale (Body.Flip) when it changes facing.
                // This child inherits that, which mirrors the sprite a second time on top of the
                // facing already baked into the captured pixels. Divide out the parent's scale (sign
                // included) so the display's world scale is always +scale and never mirrored.
                Vector3 ls = _body.transform.lossyScale;
                float sx = Mathf.Abs(ls.x) > 1e-4f ? scale / ls.x : scale;
                float sy = Mathf.Abs(ls.y) > 1e-4f ? scale / ls.y : scale;
                _displayT.localScale = new Vector3(sx, sy, 1f);
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
            // The camera follows the average ragdoll limb position (PlayerCamera), so the limbs must
            // travel with the puppeted root or the camera sinks below while the sprite stays up.
            _limbT.Clear();
            _limbRb.Clear();
            _limbOff.Clear();
            if (_body.limbs != null)
            {
                for (int i = 0; i < _body.limbs.Length; i++)
                {
                    Limb l = _body.limbs[i];
                    if (l == null)
                    {
                        continue;
                    }
                    Rigidbody2D lrb = l.GetComponent<Rigidbody2D>();
                    if (lrb != null && !_frozen.Contains(lrb))
                    {
                        _frozen.Add(lrb);
                        _frozenTypes.Add(lrb.bodyType);
                        lrb.bodyType = RigidbodyType2D.Kinematic;
                    }
                    _limbT.Add(l.transform);
                    _limbRb.Add(lrb);
                    _limbOff.Add(l.transform.position - _body.transform.position);
                }
            }
            Plugin.Log.LogInfo("LiveLink: S2 puppet enabled (" + _frozen.Count + " rigidbodies frozen, " +
                               _limbT.Count + " limbs pinned).");
        }

        private void PuppetTo(float x, float y, int facing)
        {
            if (_body == null)
            {
                return;
            }
            // PlayerState carries the mapped collider centre; place the body so its collider centre lands there.
            Vector2 off = _body.col != null ? _body.col.offset : Vector2.zero;
            float bz = _body.transform.position.z;
            Vector3 np = new Vector3(x - off.x, y - off.y, bz);
            _body.transform.position = np;
            if (_body.rb != null)
            {
                _body.rb.position = np;
            }
            // Pin each ragdoll limb to the root at its captured offset.
            for (int i = 0; i < _limbT.Count; i++)
            {
                if (_limbT[i] == null)
                {
                    continue;
                }
                Vector3 lp = np + _limbOff[i];
                _limbT[i].position = lp;
                if (_limbRb[i] != null)
                {
                    _limbRb[i].position = lp;
                }
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
        /// Sample CU's block grid around the player and publish solid rows as absolute AABBs (row
        /// runs merged), plus the mapping anchor. Real-run ground is chunk tilemaps, so reading the
        /// world blocks is exact — collider bounds there are 64x64 chunk AABBs and useless.
        /// </summary>
        private void PublishTerrain()
        {
            if (_body == null || _body.col == null)
            {
                return;
            }
            WorldGeneration w = WorldGeneration.world;
            if (w == null)
            {
                return;
            }

            Bounds cb = _body.col.bounds;
            Vector2 center = cb.center;

            // Anchor at the ground directly under the player (mapping origin / proxy y=0).
            float anchorY = cb.min.y;
            RaycastHit2D ground = Physics2D.Raycast(center, Vector2.down, cb.extents.y + 12f, GroundMask);
            if (ground.collider != null)
            {
                anchorY = ground.point.y;
            }
            Vector2 anchor = new Vector2(center.x, anchorY);

            const float half = 48f;
            Vector2Int bmin = w.WorldToBlockPos(new Vector2(anchor.x - half, anchor.y - half));
            Vector2Int bmax = w.WorldToBlockPos(new Vector2(anchor.x + half, anchor.y + half));

            // Greedy-merge the solid grid into maximal rectangles (horizontal runs, then extend
            // identical runs downward). A stair/wall used to be a stack of 1-unit-tall boxes; the
            // seams between them snagged Hornet's collider (internal edges), popping her up and off.
            // Merging turns a column/wall into one tall box with a clean surface.
            int count = 0;
            _openRects.Clear();
            for (int by = bmin.y; by <= bmax.y && count < Proto.MaxRects; by++)
            {
                _curRuns.Clear();
                int runStart = -1;
                for (int bx = bmin.x; bx <= bmax.x + 1; bx++)
                {
                    bool solid = false;
                    if (bx <= bmax.x)
                    {
                        BlockInfo info = w.GetBlockInfo(w.GetBlock(new Vector2Int(bx, by)));
                        solid = info != null && info.health > 0f;
                    }
                    if (solid)
                    {
                        if (runStart < 0)
                        {
                            runStart = bx;
                        }
                    }
                    else if (runStart >= 0)
                    {
                        _curRuns.Add(new int[] { runStart, bx });
                        runStart = -1;
                    }
                }

                int preCount = _openRects.Count;
                var matched = new bool[preCount];
                for (int i = 0; i < _curRuns.Count; i++)
                {
                    int rs = _curRuns[i][0], re = _curRuns[i][1];
                    bool found = false;
                    for (int j = 0; j < preCount; j++)
                    {
                        if (!matched[j] && _openRects[j][0] == rs && _openRects[j][1] == re)
                        {
                            matched[j] = true;
                            found = true;
                            break;
                        }
                    }
                    if (!found && count < Proto.MaxRects)
                    {
                        _openRects.Add(new int[] { rs, re, by }); // {startBx, endBxExclusive, y0}
                    }
                }
                for (int j = preCount - 1; j >= 0; j--)
                {
                    if (!matched[j])
                    {
                        int[] r = _openRects[j];
                        EmitRect(w, r[0], r[1], r[2], by - r[2], ref count);
                        _openRects.RemoveAt(j);
                    }
                }
            }
            for (int j = _openRects.Count - 1; j >= 0 && count < Proto.MaxRects; j--)
            {
                int[] r = _openRects[j];
                EmitRect(w, r[0], r[1], r[2], bmax.y + 1 - r[2], ref count);
            }
            _openRects.Clear();

            // Non-block ground colliders (placed structures/props/steps) are not in worldBlocks, so
            // the block pass above misses them. Include their (window-clipped) bounds too. Skip the
            // terrain tilemap/composite colliders, whose chunk bounds are huge AABBs and already
            // covered exactly by the block pass.
            int gh = Physics2D.OverlapBoxNonAlloc(anchor, new Vector2(half * 2f, half * 2f), 0f,
                _groundHits, GroundMask);
            int geomHits = 0;
            for (int i = 0; i < gh && count < Proto.MaxRects; i++)
            {
                Collider2D c = _groundHits[i];
                if (c == null)
                {
                    continue;
                }
                string tn = c.GetType().Name;
                if (tn == "TilemapCollider2D" || tn == "CompositeCollider2D")
                {
                    continue; // chunk tilemap colliders: covered exactly by the block pass
                }
                Bounds b = c.bounds;
                float x0 = Mathf.Max(b.min.x, anchor.x - half);
                float x1 = Mathf.Min(b.max.x, anchor.x + half);
                float y0 = Mathf.Max(b.min.y, anchor.y - half);
                float y1 = Mathf.Min(b.max.y, anchor.y + half);
                if (x1 > x0 && y1 > y0)
                {
                    EmitRectAbs(x0, y0, x1 - x0, y1 - y0, ref count);
                    geomHits++;
                }
            }

            _terrainRev++;
            _link.WriteTerrain(cb.size.y, anchor.x, anchor.y, _rectBuf, count, _terrainRev);
            // Topmost solid CU block directly under the player's column (compare with the anchor):
            // if this is far below the collider, CU's own terrain has a drop there.
            Vector2Int pb = w.WorldToBlockPos(new Vector2(center.x, cb.min.y - 0.5f));
            int pbx = pb.x;
            int pby = pb.y;
            float blockTop = -9999f;
            for (int by = pby; by >= bmin.y; by--)
            {
                BlockInfo bi = w.GetBlockInfo(w.GetBlock(new Vector2Int(pbx, by)));
                if (bi != null && bi.health > 0f)
                {
                    blockTop = w.BlockToWorldPos(new Vector2Int(pbx, by)).y + 0.5f;
                    break;
                }
            }
            Plugin.Log.LogInfo("[" + System.DateTime.Now.ToString("HH:mm:ss.fff") + "] LiveLink terrain: anchor=(" +
                               anchor.x.ToString("0.0") + "," +
                               anchor.y.ToString("0.0") + ") rects=" + count +
                               " playerMinY=" + cb.min.y.ToString("0.0") +
                               " blockTop=" + blockTop.ToString("0.0") +
                               " geom=" + geomHits +
                               " psSeq=" + _link.DebugPlayerSeq +
                               (Puppeting && _pupValid
                                   ? " pup=(" + _pupX.ToString("0.0") + "," + _pupY.ToString("0.0") + ")"
                                   : " pup=off") +
                               " rev=" + _terrainRev);
        }

        /// <summary>Emit one merged solid rectangle (block range -> CU-space AABB, small margin).</summary>
        private void EmitRect(WorldGeneration w, int startBx, int endBx, int y0, int height, ref int count)
        {
            if (height <= 0 || count >= Proto.MaxRects || endBx <= startBx)
            {
                return;
            }
            Vector2 p0 = w.BlockToWorldPos(new Vector2Int(startBx, y0));
            EmitRectAbs(p0.x - 0.5f, p0.y - 0.5f, endBx - startBx, height, ref count);
        }

        /// <summary>Emit a rect from absolute CU coords (expanded by the terrain margin).</summary>
        private void EmitRectAbs(float x, float y, float w, float h, ref int count)
        {
            if (w <= 0f || h <= 0f || count >= Proto.MaxRects)
            {
                return;
            }
            _rectBuf[count * 4 + 0] = x - TerrainMargin;
            _rectBuf[count * 4 + 1] = y - TerrainMargin;
            _rectBuf[count * 4 + 2] = w + TerrainMargin * 2f;
            _rectBuf[count * 4 + 3] = h + TerrainMargin * 2f;
            count++;
        }

        /// <summary>Spawn a visible dummy near the player to pogo off (F7 in the sandbox).</summary>
        private void SpawnDummy()
        {
            if (_body == null)
            {
                return;
            }
            if (_dummySprite == null)
            {
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _dummySprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            }
            var go = new GameObject("HornetPogoDummy");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _dummySprite;
            sr.color = new Color(1f, 0.35f, 0.25f, 0.95f);
            sr.sortingOrder = 6000;
            go.transform.localScale = new Vector3(1.2f, 1.2f, 1f);
            go.transform.position = _body.transform.position + new Vector3(0f, -0.4f, 0f);
            _dummies.Add(go);
            _dummyHp.Add(100f);
            Plugin.Log.LogInfo("Spawned pogo dummy at " + go.transform.position);
        }

        /// <summary>Publish nearby CU actors (pogo dummies + real BuildingEntities) for Silksong proxies.</summary>
        private void PublishEntities()
        {
            if (_body == null)
            {
                return;
            }
            int count = 0;
            for (int i = 0; i < _dummies.Count && count < Proto.MaxEntities; i++)
            {
                GameObject d = _dummies[i];
                if (d == null)
                {
                    continue;
                }
                Vector3 p = d.transform.position;
                AddEnt(ref count, -(i + 1), p.x, p.y, 1.2f, 1.2f, 100f, 100f,
                    Proto.EntFlagBounceable | Proto.EntFlagAlive);
            }

            Vector2 c = _body.transform.position;
            PruneEntityMap();
            _entSeen.Clear();
            int n = Physics2D.OverlapCircleNonAlloc(c, 30f, _entHits, ~0);
            for (int i = 0; i < n && count < Proto.MaxEntities; i++)
            {
                Collider2D col = _entHits[i];
                if (col == null)
                {
                    continue;
                }
                BuildingEntity be = col.GetComponentInParent<BuildingEntity>();
                if (be == null || be.health <= 0.5f)
                {
                    continue;
                }
                // A single actor has several colliders; publish it once or the proxy count (and the
                // contact damage Hornet takes) multiplies per limb.
                int eid = be.GetInstanceID() & 0xFFFFFF;
                if (!_entSeen.Add(eid))
                {
                    continue;
                }
                // One proxy covering the whole actor, centred on its combined collider bounds.
                Bounds b = col.bounds;
                _entCols.Clear();
                be.GetComponentsInChildren(true, _entCols);
                for (int ci = 0; ci < _entCols.Count; ci++)
                {
                    Collider2D cc = _entCols[ci];
                    if (cc != null && cc.enabled)
                    {
                        b.Encapsulate(cc.bounds);
                    }
                }
                int flags = Proto.EntFlagAlive;
                if (be.animal)
                {
                    flags |= Proto.EntFlagBounceable;
                }
                // S4 phase 3: biters damage Hornet on contact via a proxy DamageHero. CU animals
                // damage limbs through SpiderHandler (and its subclasses), so use that as the signal.
                if (be.GetComponent<SpiderHandler>() != null || be.GetComponentInParent<SpiderHandler>() != null)
                {
                    flags |= Proto.EntFlagContactDamage;
                }
                _entityById[eid] = be;
                AddEnt(ref count, eid, b.center.x, b.center.y,
                    b.size.x, b.size.y, be.health, be.health, flags);
            }

            _entRev++;
            _link.WriteEntities(_entBuf, count, _entRev);
        }

        private void AddEnt(ref int count, int id, float x, float y, float w, float h,
            float hp, float maxHp, int flags)
        {
            int v = count * Proto.EntityStrideFloats;
            _entBuf[v + 0] = id;
            _entBuf[v + 1] = x;
            _entBuf[v + 2] = y;
            _entBuf[v + 3] = w;
            _entBuf[v + 4] = h;
            _entBuf[v + 5] = hp;
            _entBuf[v + 6] = maxHp;
            _entBuf[v + 7] = flags;
            count++;
        }

        /// <summary>Drain Silksong's event ring and apply damage to CU actors.</summary>
        private void DrainEvents()
        {
            if (_link == null)
            {
                return;
            }
            int writeIdx = _link.EventWriteIndex;
            if (writeIdx - _eventReadIndex > Proto.MaxEvents)
            {
                _eventReadIndex = writeIdx - Proto.MaxEvents; // fell behind; skip overwritten
            }
            while (_eventReadIndex < writeIdx)
            {
                int type, id;
                float a, b, c;
                if (_link.ReadEvent(_eventReadIndex, out type, out id, out a, out b, out c) &&
                    type == Proto.EventHitEntity)
                {
                    ApplyHit(id, a);
                }
                _eventReadIndex++;
            }
        }

        private BuildingEntity FindEntityById(int id)
        {
            BuildingEntity[] all = UnityEngine.Object.FindObjectsByType<BuildingEntity>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                BuildingEntity b = all[i];
                if (b != null && (b.GetInstanceID() & 0xFFFFFF) == id)
                {
                    return b;
                }
            }
            return null;
        }

        private void PruneEntityMap()
        {
            _deadIds.Clear();
            foreach (KeyValuePair<int, BuildingEntity> kv in _entityById)
            {
                if (kv.Value == null)
                {
                    _deadIds.Add(kv.Key);
                }
            }
            for (int i = 0; i < _deadIds.Count; i++)
            {
                _entityById.Remove(_deadIds[i]);
            }
        }

        private void ApplyHit(int id, float damage)
        {
            if (id < 0)
            {
                int di = -id - 1;
                if (di < 0 || di >= _dummyHp.Count)
                {
                    return;
                }
                _dummyHp[di] -= damage;
                Plugin.Log.LogInfo("Dummy " + di + " hit for " + damage + " -> hp " + _dummyHp[di]);
                if (_dummyHp[di] <= 0f && di < _dummies.Count && _dummies[di] != null)
                {
                    UnityEngine.Object.Destroy(_dummies[di]);
                }
                return;
            }

            BuildingEntity be;
            if (!_entityById.TryGetValue(id, out be) || be == null)
            {
                be = FindEntityById(id);
            }
            if (be != null)
            {
                be.health -= damage;
                Plugin.Log.LogInfo("Entity " + id + " hit for " + damage + " -> hp " + be.health);
                try
                {
                    WorldGeneration.CreateDamageNumber(be.transform.position, Mathf.RoundToInt(damage));
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError("damage number failed: " + e.Message);
                }
            }
            else
            {
                Plugin.Log.LogInfo("Entity " + id + " hit but not found live");
            }
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

        /// <summary>Diagnostic for the "double sprite": count displays/avatars and any visible body sprites.</summary>
        private void LogDisplayDiag()
        {
            int displays = 0;
            SpriteRenderer[] all = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
            foreach (SpriteRenderer sr in all)
            {
                if (sr != null && sr.gameObject.name == "HornetLive")
                {
                    displays++;
                }
            }
            int avatars = UnityEngine.Object.FindObjectsByType<HornetAvatar>(FindObjectsSortMode.None).Length;
            int links = UnityEngine.Object.FindObjectsByType<LiveLink>(FindObjectsSortMode.None).Length;
            int visibleBody = 0;
            if (_body != null)
            {
                foreach (SpriteRenderer sr in _body.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (sr != null && sr.enabled && sr != _display && !sr.transform.IsChildOf(_displayT))
                    {
                        visibleBody++;
                    }
                }
            }
            Plugin.Log.LogInfo("LiveLink displays=" + displays + " avatars=" + avatars + " links=" + links +
                               " visibleBodySprites=" + visibleBody);
            float limbY = 0f;
            int n = 0;
            if (_body.limbs != null)
            {
                for (int i = 0; i < _body.limbs.Length; i++)
                {
                    if (_body.limbs[i] != null)
                    {
                        limbY += _body.limbs[i].transform.position.y;
                        n++;
                    }
                }
            }
            if (n > 0)
            {
                limbY /= n;
            }
            float camY = PlayerCamera.main != null ? PlayerCamera.main.transform.position.y : 0f;
            float l0 = (_body.limbs != null && _body.limbs.Length > 0 && _body.limbs[0] != null)
                ? _body.limbs[0].transform.position.y : 0f;
            Plugin.Log.LogInfo("LiveLink camY=" + camY.ToString("0.0") + " limbAvgY=" + limbY.ToString("0.0") +
                               " limb0Y=" + l0.ToString("0.0") +
                               " rootY=" + _body.transform.position.y.ToString("0.0") + " puppeting=" + Puppeting);
        }

        private int _rejectStreak;

        private void ApplyFrame(int w, int h, float px, float py)
        {
            if (w > Proto.MaxWidth || h > Proto.MaxHeight)
            {
                return; // guard against a bad capture
            }
            // Ignore sudden size jumps (the diff occasionally catches a large effect), which would
            // otherwise render Hornet huge for a frame. But never latch: after a room transition or
            // capture-mode change the size can settle somewhere new, so accept after a short streak.
            if (_tex != null && (w > _lastW * 1.6f || h > _lastH * 1.6f || w < _lastW * 0.6f || h < _lastH * 0.6f))
            {
                _rejectStreak++;
                if (_rejectStreak < 20)
                {
                    if (_rejectStreak == 1 || _rejectStreak % 30 == 0)
                    {
                        Plugin.Log.LogInfo("LiveLink: frame size " + w + "x" + h + " jumped from " +
                                           _lastW + "x" + _lastH + "; holding (" + _rejectStreak + ")");
                    }
                    return;
                }
                Plugin.Log.LogWarning("LiveLink: accepting new frame size " + w + "x" + h +
                                      " after " + _rejectStreak + " rejects.");
            }
            _rejectStreak = 0;
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

    /// <summary>
    /// While puppeting, CU's speech text follows the head limb (Talker.LateUpdate), which leans with
    /// movement, so the text drifts off Hornet's sprite. Anchor it to the body root instead.
    /// </summary>
    [HarmonyPatch(typeof(Talker), "LateUpdate")]
    internal static class TalkerLateUpdatePatch
    {
        private static void Postfix(Talker __instance)
        {
            if (!LiveLink.Puppeting || __instance == null || __instance.body == null ||
                __instance.text == null || __instance.text.rectTransform == null)
            {
                return;
            }
            Vector3 p = __instance.body.transform.position;
            __instance.text.rectTransform.position = new Vector3(p.x, p.y + 3.2f, -2.6f);
        }
    }
}
