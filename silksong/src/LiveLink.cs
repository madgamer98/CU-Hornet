using HornetPassthrough;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// Live passthrough (Silksong side). Maps the host player's state/actions onto Hornet's animator
    /// and publishes her isolated frame. Actions come from the host's flag bits (rising edges).
    /// </summary>
    public class LiveLink : MonoBehaviour
    {
        private PassthroughLink _link;
        private tk2dSpriteAnimator _anim;

        private int _lastClipHash;
        private int _publishTick;
        private bool _capLogged;

        private bool _prevAttack, _prevDash, _prevNeedle;
        private string _clip = "Idle";
        private float _actionUntil;
        private int _lastFacing = 1;

        private void Awake()
        {
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
            float x, y, vx, vy;
            int facing, flags;
            bool grounded;
            _link.ReadCuState(out x, out y, out vx, out vy, out facing, out grounded, out flags);

            HeroController hero = HeroController.instance;
            if (hero == null)
            {
                return;
            }
            if (_anim == null)
            {
                _anim = hero.GetComponentInChildren<tk2dSpriteAnimator>();
            }

            bool attack = (flags & Proto.FlagAttack) != 0;
            bool dash = (flags & Proto.FlagDash) != 0;
            bool needle = (flags & Proto.FlagNeedle) != 0;
            bool up = (flags & Proto.FlagUp) != 0;
            bool down = (flags & Proto.FlagDown) != 0;
            bool attackEdge = attack && !_prevAttack;
            bool dashEdge = dash && !_prevDash;
            bool needleEdge = needle && !_prevNeedle;
            _prevAttack = attack;
            _prevDash = dash;
            _prevNeedle = needle;

            float speed = 1f;
            if (attackEdge)
            {
                _clip = up ? "UpSlash" : (down && !grounded ? "DownSpike" : "Slash");
                _actionUntil = Time.time + 0.30f;
            }
            else if (dashEdge)
            {
                _clip = "Dash";
                _actionUntil = Time.time + 0.40f;
            }
            else if (needleEdge)
            {
                _clip = "NeedleThrow Throwing";
                _actionUntil = Time.time + 0.40f;
            }
            else if (Time.time >= _actionUntil)
            {
                if (!grounded)
                {
                    _clip = vy > 1f ? "Airborne" : "Fall";
                }
                else
                {
                    float ax = Mathf.Abs(vx);
                    _clip = ax > 0.5f ? "Run" : "Idle";
                    if (ax > 0.5f)
                    {
                        speed = Mathf.Clamp(ax / 7f, 0.7f, 2f);
                    }
                }
            }

            if (_anim != null)
            {
                int hash = _clip.GetHashCode();
                if (hash != _lastClipHash)
                {
                    _anim.Play(_clip);
                    _lastClipHash = hash;
                    Plugin.Log.LogInfo("LiveLink: clip " + _clip);
                }
                if (_anim.CurrentClip != null)
                {
                    _anim.ClipFps = _anim.CurrentClip.fps * speed;
                }
            }

            // Flip Hornet to match the host's facing.
            if (facing != 0 && facing != _lastFacing)
            {
                _lastFacing = facing;
                Vector3 s = hero.transform.localScale;
                float mag = Mathf.Abs(s.x);
                hero.transform.localScale = new Vector3(mag * (facing > 0 ? 1f : -1f), s.y, s.z);
            }

            // Publish the isolated frame (cached diff capture) every frame.
            try
            {
                byte[] rgba;
                int w, h;
                float px, py, wx, wy;
                if (HornetCapture.CaptureDiffRgba(out rgba, out w, out h, out px, out py, out wx, out wy))
                {
                    _link.WriteFrame(rgba, w, h, px, py, wx, wy, _lastClipHash);
                    if (!_capLogged)
                    {
                        _capLogged = true;
                        Plugin.Log.LogInfo("LiveLink: publishing isolated frames " + w + "x" + h);
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
            _link?.Dispose();
        }
    }
}
