using HornetPassthrough;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// Live passthrough (Silksong side). Reads the host player's state, drives Hornet's animator to
    /// match, and publishes the live animator state (clip name + frame + facing) so the host can
    /// render her. Pixel streaming is attempted when a frame can be captured.
    /// </summary>
    public class LiveLink : MonoBehaviour
    {
        private PassthroughLink _link;
        private tk2dSpriteAnimator _anim;
        private int _lastClipHash;
        private int _frameCounter;
        private int _publishTick;
        private bool _capLogged;

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

            string clip = !grounded ? (vy > 1f ? "Airborne" : "Fall")
                : (Mathf.Abs(vx) > 0.5f ? "Run" : "Idle");
            int hash = clip.GetHashCode();
            if (_anim != null && hash != _lastClipHash)
            {
                _anim.Play(clip);
                _lastClipHash = hash;
                Plugin.Log.LogInfo("LiveLink: host state -> Hornet clip " + clip);
            }

            string live = _anim != null && _anim.CurrentClip != null ? _anim.CurrentClip.name : clip;
            int frame = _anim != null ? _anim.CurrentFrame : 0;

            // Publish every frame (cached diff capture) for smoother motion.
            if (true)
            {
                try
                {
                    byte[] rgba;
                    int w, h;
                    float px, py, wx, wy;
                    if (HornetCapture.CaptureDiffRgba(out rgba, out w, out h, out px, out py, out wx, out wy))
                    {
                        _link.WriteFrame(rgba, w, h, px, py, wx, wy, hash);
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
        }

        private void OnDestroy()
        {
            _link?.Dispose();
        }
    }
}
