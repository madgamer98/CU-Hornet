using UnityEngine;

namespace HornetInCasualties
{
    /// <summary>
    /// Adds Hornet's moves to the vanilla player body: dash, slash (forward/up/down) and pogo.
    /// It reads input, plays the matching baked clip via <see cref="HornetAvatar"/>, and applies
    /// velocity/impulses to the vanilla ragdoll. More moves (needle throw, bind, wall cling) follow.
    /// </summary>
    public class HornetController : MonoBehaviour
    {
        private Body _body;
        private HornetAvatar _avatar;
        private Rigidbody2D _rb;

        private float _dashTime;
        private float _dashDir;
        private float _dashReady;
        private bool _wasDashing;

        private float _slashUntil;
        private float _slashReady;
        private bool _slashHit;
        private string _slashClip;

        private HornetNeedle _needle;
        private float _needleReady;

        public void Init(HornetAvatar avatar, Body body)
        {
            _avatar = avatar;
            _body = body;
            _rb = body.rb;
        }

        private void LateUpdate()
        {
            if (_body == null || !Plugin.Enable.Value)
            {
                return;
            }

            float now = Time.time;
            HandleJump();
            HandleWall();
            HandleDash(now);
            HandleSlash(now);
            HandleNeedle(now);
        }

        private bool _usedDoubleJump;
        private int _wallSide;
        private float _wallActionUntil;

        private void HandleJump()
        {
            if (_body.grounded)
            {
                _usedDoubleJump = false;
            }
            if (!Input.GetKeyDown(KeyBinds.GetBind("jump")))
            {
                return;
            }
            if (_wallSide != 0)
            {
                return; // handled by HandleWall
            }
            if (!_body.grounded && !_usedDoubleJump)
            {
                _usedDoubleJump = true;
                if (_rb != null)
                {
                    _rb.velocity = new Vector2(_rb.velocity.x, Plugin.DoubleJumpSpeed.Value);
                }
                _avatar.PlayAction("Double Jump", 0.4f);
                Plugin.Log.LogInfo("move: double jump");
            }
        }

        private void HandleWall()
        {
            _wallSide = 0;
            if (_body.grounded || _rb == null)
            {
                return;
            }
            float reach = 0.9f;
            if (Physics2D.Raycast(_body.transform.position, Vector2.right, reach, LayerMask.GetMask("Ground")))
            {
                _wallSide = 1;
            }
            else if (Physics2D.Raycast(_body.transform.position, Vector2.left, reach, LayerMask.GetMask("Ground")))
            {
                _wallSide = -1;
            }
            if (_wallSide == 0)
            {
                return;
            }

            // Slide down the wall.
            if (_rb.velocity.y < 0f)
            {
                _rb.velocity = new Vector2(_rb.velocity.x, -Plugin.WallSlideSpeed.Value);
            }
            _usedDoubleJump = false;

            if (Input.GetKeyDown(KeyBinds.GetBind("jump")))
            {
                _rb.velocity = new Vector2(-_wallSide * Plugin.WallJumpX.Value, Plugin.WallJumpY.Value);
                _avatar.PlayAction("Walljump", 0.35f);
                Plugin.Log.LogInfo("move: wall jump side=" + _wallSide);
            }
            else if (Time.time >= _wallActionUntil)
            {
                _avatar.PlayAction("Wall Slide", 0.2f);
                _wallActionUntil = Time.time + 0.15f;
            }
        }

        private void HandleNeedle(float now)
        {
            if (_needle != null)
            {
                return;
            }
            if (!Input.GetKeyDown(Plugin.KeyNeedle.Value.MainKey) || now < _needleReady)
            {
                return;
            }
            _avatar.PlayAction("NeedleThrow Throwing", 0.4f);
            _needle = HornetNeedle.Spawn(_body, _avatar.FacingRight, this);
            _needleReady = now + Plugin.NeedleCooldown.Value;
            Plugin.Log.LogInfo("move: needle throw");
        }

        public void OnNeedleCaught()
        {
            _needle = null;
            _avatar.PlayAction("NeedleThrow Catch", 0.3f);
            Plugin.Log.LogInfo("move: needle caught");
        }

        private void HandleDash(float now)
        {
            // End an in-progress dash.
            if (_dashTime > 0f)
            {
                _dashTime -= Time.deltaTime;
                if (_rb != null)
                {
                    _rb.velocity = new Vector2(_dashDir * Plugin.DashSpeed.Value, 0f);
                    _rb.gravityScale = 0f;
                }
                _wasDashing = true;
                if (_dashTime <= 0f && _rb != null)
                {
                    _rb.gravityScale = 1f;
                }
                return;
            }

            if (_wasDashing)
            {
                _wasDashing = false;
                if (_rb != null)
                {
                    _rb.gravityScale = 1f;
                }
            }

            if (Input.GetKeyDown(Plugin.KeyDash.Value.MainKey) && now >= _dashReady)
            {
                _dashDir = _body.isRight ? 1f : -1f;
                _dashTime = Plugin.DashDuration.Value;
                _dashReady = now + Plugin.DashCooldown.Value;
                _avatar.PlayAction("Dash", Plugin.DashDuration.Value + 0.1f);
                Plugin.Log.LogInfo("move: dash dir=" + _dashDir);
            }
        }

        private void HandleSlash(float now)
        {
            if (now < _slashUntil)
            {
                // The active frames of the slash: check for a hit once.
                if (!_slashHit && now >= _slashUntil - Plugin.SlashActive.Value)
                {
                    _slashHit = true;
                    DoSlashHit();
                }
                return;
            }

            if (!Input.GetKeyDown(Plugin.KeySlash.Value.MainKey) || now < _slashReady)
            {
                return;
            }

            string clip;
            if (Input.GetKey(KeyCode.W))
            {
                clip = "UpSlash";
            }
            else if (Input.GetKey(KeyCode.S) && !_body.grounded)
            {
                clip = "DownSpike";
            }
            else
            {
                clip = "Slash";
            }

            HornetClip c;
            float dur = 0.25f;
            if (HornetSprites.Clips != null && HornetSprites.Clips.TryGetValue(clip, out c) && c.Frames.Length > 0)
            {
                dur = c.Frames.Length / c.Fps;
            }
            _slashClip = clip;
            _slashUntil = now + dur;
            _slashReady = now + dur + Plugin.SlashCooldown.Value;
            _slashHit = false;
            _avatar.PlayAction(clip, dur);
            Plugin.Log.LogInfo("move: " + clip + " (dur=" + dur.ToString("0.00") + ")");
        }

        private void DoSlashHit()
        {
            Vector2 origin = _body.transform.position;
            Vector2 dir = _slashClip == "UpSlash" ? Vector2.up
                : _slashClip == "DownSpike" ? Vector2.down
                : (_body.isRight ? Vector2.right : Vector2.left);
            Vector2 center = origin + dir * Plugin.SlashReach.Value;
            Vector2 size = new Vector2(Plugin.SlashRange.Value, Plugin.SlashRange.Value);

            bool hit = false;
            Collider2D[] cols = Physics2D.OverlapBoxAll(center, size, 0f);
            foreach (Collider2D col in cols)
            {
                if (col.transform == _body.transform)
                {
                    continue;
                }

                var be = col.GetComponent<BuildingEntity>();
                if (be != null && !be.cantHit)
                {
                    be.health -= Plugin.SlashDamage.Value;
                    if (be.animal)
                    {
                        col.gameObject.SendMessage("AnimalHit", Plugin.SlashDamage.Value,
                            SendMessageOptions.DontRequireReceiver);
                    }
                    else
                    {
                        col.gameObject.SendMessage("BuildingHit", null, SendMessageOptions.DontRequireReceiver);
                    }
                    WorldGeneration.CreateDamageNumber(col.transform.position, (int)Plugin.SlashDamage.Value);
                    hit = true;
                    continue;
                }

                // Terrain / hazards count as pogo surfaces.
                if (col.CompareTag("BlockGround") || col.GetComponent<Damageable>() != null)
                {
                    hit = true;
                }
            }

            // Pogo: a down-slash on an enemy/terrain while airborne bounces you up.
            if (hit && _slashClip == "DownSpike" && !_body.grounded && _rb != null)
            {
                _rb.velocity = new Vector2(_rb.velocity.x, Plugin.PogoSpeed.Value);
                Plugin.Log.LogInfo("move: pogo bounce");
            }
        }
    }
}
