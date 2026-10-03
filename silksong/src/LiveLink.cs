using HornetPassthrough;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// Live passthrough (Silksong side), S0: input round-trip.
    /// The host's raw buttons are read from shared memory and injected into Hornet's real
    /// InControl actions (see <see cref="InputInjector"/>), so her own controller moves her.
    /// We no longer replay clips from the host's velocity. Her isolated frame is still published
    /// to the host for display.
    /// </summary>
    public class LiveLink : MonoBehaviour
    {
        /// <summary>Latest button bitfield from the host; consumed by InputInjector.</summary>
        public static int InjectedButtons;

        /// <summary>Capture mode: true = blank-slate (Hornet-only camera), false = main-camera diff.
        /// Toggled with F2 (S4 visual prototype).</summary>
        public static bool UseBlankCapture = true;

        /// <summary>The active link (for TerrainMirror to read the terrain region).</summary>
        public static LiveLink Instance;
        public PassthroughLink Link => _link;

        private PassthroughLink _link;
        private tk2dSpriteAnimator _anim;
        private Rigidbody2D _rb;
        private Collider2D _heroCol;
        private int _lastClipHash;
        private bool _capLogged;
        private int _capFail;
        private int _stateTick;

        private void Awake()
        {
            Instance = this;
            TryOpen();
        }

        private void TryOpen()
        {
            if (_link != null)
            {
                return;
            }
            try
            {
                _link = new PassthroughLink(false);
                Plugin.Log.LogInfo("LiveLink: connected to " + Proto.MappingName);
            }
            catch (System.Exception)
            {
                // Host (CU) not up yet; retry next frame.
            }
        }

        private void Update()
        {
            if (_link == null)
            {
                TryOpen();
                return;
            }

            _link.Heartbeat();
            InjectedButtons = _link.ReadInput();

            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                return;
            }
            if (_anim == null)
            {
                _anim = hero.GetComponentInChildren<tk2dSpriteAnimator>();
            }
            if (_rb == null)
            {
                _rb = hero.GetComponent<Rigidbody2D>();
            }
            if (_heroCol == null)
            {
                _heroCol = hero.GetComponent<Collider2D>();
            }

            // While on mirrored terrain, force the cached ground probe so the FSM keeps a correct
            // onGround state (otherwise the first jump is treated as the air/double jump).
            if (TerrainMirror.Active)
            {
                hero.CheckTouchingGround(true);
            }

            // S2: refresh the terrain proxy from the host window, then publish Hornet's state mapped
            // back into CU coordinates so the host can puppet its body/camera to her.
            TerrainMirror.Poll(_link);
            TerrainMirror.Diag(Time.deltaTime);
            if (TerrainMirror.Active && !_link.HostAlive(1500))
            {
                Plugin.Log.LogWarning("LiveLink: host heartbeat lost; restoring vanilla terrain.");
                TerrainMirror.Restore();
            }
            EntityProxies.Poll(_link);
            float cuX, cuY;
            Vector2 silkCenter = _heroCol != null ? (Vector2)_heroCol.bounds.center : (Vector2)hero.transform.position;
            bool active = TerrainMirror.TryMapToCu(silkCenter, out cuX, out cuY);
            Vector2 hv = _rb != null ? _rb.linearVelocity : Vector2.zero;
            float k = TerrainMirror.Scale;
            int facing = hero.transform.localScale.x >= 0f ? 1 : -1;
            bool grounded = hero.CheckTouchingGround();
            _link.WritePlayerState(cuX, cuY,
                k > 0.0001f ? hv.x / k : 0f, k > 0.0001f ? hv.y / k : 0f,
                facing, grounded, active);

            // S5 2A: publish Silksong's vitals - they are the source of truth for health/silk/geo.
            PlayerData pdv = PlayerData.instance;
            if (pdv != null)
            {
                bool dead = (hero.cState != null && hero.cState.dead) || pdv.health <= 0;
                _link.WriteVitals(pdv.health, pdv.maxHealth, pdv.healthBlue, pdv.silk, pdv.silkMax,
                    pdv.geo, dead);
            }

            // Diagnostics: log Hornet's own clip + velocity so we can prove input reached her.
            _stateTick++;
            if (_stateTick % 30 == 0)
            {
                string clip = _anim != null && _anim.CurrentClip != null ? _anim.CurrentClip.name : "?";
                Vector2 v = _rb != null ? _rb.linearVelocity : Vector2.zero;
                PlayerData pd = PlayerData.instance;
                string hp = pd != null ? pd.health + "/" + pd.maxHealth : "?";
                Plugin.Log.LogInfo("LiveLink state: clip=" + clip + " vel=(" + v.x.ToString("0.0") +
                                   "," + v.y.ToString("0.0") + ") pos=" + hero.transform.position.x.ToString("0.0") +
                                   "," + hero.transform.position.y.ToString("0.0") +
                                   " gnd=" + hero.CheckTouchingGround() +
                                   " hp=" + hp +
                                   " in=0x" + InjectedButtons.ToString("X"));
            }

            // Publish the isolated frame. Default is the blank-slate capture (Hornet alone on a
            // transparent background); F2 toggles back to the main-camera diff fallback.
            try
            {
                byte[] rgba;
                int w, h;
                float px, py, wx, wy;
                bool got = UseBlankCapture
                    ? HornetCapture.CaptureBlankRgba(out rgba, out w, out h, out px, out py, out wx, out wy)
                    : HornetCapture.CaptureDiffRgba(out rgba, out w, out h, out px, out py, out wx, out wy);
                if (got)
                {
                    _capFail = 0;
                    _link.WriteFrame(rgba, w, h, px, py, wx, wy, _lastClipHash);
                    if (!_capLogged)
                    {
                        _capLogged = true;
                        Plugin.Log.LogInfo("LiveLink: publishing isolated frames " + w + "x" + h +
                                           (UseBlankCapture ? " (blank)" : " (diff)"));
                    }
                }
                else
                {
                    _capFail++;
                    if (_capFail % 90 == 1)
                    {
                        Plugin.Log.LogWarning("LiveLink: capture returned no frame (" + _capFail + "x, " +
                                              (UseBlankCapture ? "blank" : "diff") + ")");
                    }
                }
            }
            catch (System.Exception e)
            {
                if (!_capLogged)
                {
                    _capLogged = true;
                    Plugin.Log.LogError("LiveLink capture failed: " + e);
                }
            }
        }

        private void OnDestroy()
        {
            InjectedButtons = 0;
            _link?.Dispose();
        }
    }
}
