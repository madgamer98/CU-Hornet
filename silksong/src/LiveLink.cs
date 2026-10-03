using HornetPassthrough;
using UnityEngine;

namespace HornetExporter
{
    /// <summary>
    /// Live passthrough (Silksong side). Reads the host player's state, drives Hornet's animator
    /// to match, and publishes her isolated frame to shared memory. Step 1 publishes a 1x1 test
    /// pixel to validate the channel; real pixels follow.
    /// </summary>
    public class LiveLink : MonoBehaviour
    {
        private PassthroughLink _link;
        private tk2dSpriteAnimator _anim;
        private readonly byte[] _test = { 255, 0, 128, 255 };
        private int _lastClipHash;

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

            byte[] rgba;
            int w, h;
            float px, py, wx, wy;
            if (HornetCapture.CaptureRgba(out rgba, out w, out h, out px, out py, out wx, out wy))
            {
                _link.WriteFrame(rgba, w, h, px, py, wx, wy, hash);
            }
        }

        private void OnDestroy()
        {
            _link?.Dispose();
        }
    }
}
