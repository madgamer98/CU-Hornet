using UnityEngine;

namespace HornetInCasualties
{
    /// <summary>
    /// Adds Hornet's moves to the vanilla player. CU's ragdoll movement still runs, but Hornet
    /// owns **jumping** (ground/double/wall) with its own ground raycasts, plus dash, slash,
    /// pogo and the needle throw. The ragdoll's flaky `grounded` flag is not trusted for jumps.
    /// </summary>
    public class HornetController : MonoBehaviour
    {
        private Body _body;
        private HornetAvatar _avatar;
        private Rigidbody2D _rb;

        private float _dashTime;
        private float _dashDir;
        private float _dashReady;
        private float _savedMaxSpeed;

        private float _slashUntil;
        private float _slashReady;
        private bool _slashHit;
        private string _slashClip;

        private HornetNeedle _needle;
        private float _needleReady;

        private int _airJumps;
        private bool _ourGrounded;
        private int _wallSide;
        private float _wallActionUntil;
        private bool _lastFacingRight = true;

        public void Init(HornetAvatar avatar, Body body)
        {
            _avatar = avatar;
            _body = body;
            _rb = body.rb;
            Plugin.Log.LogInfo("Hornet controller ready (movement=vanilla, jumps=Hornet).");
        }

        private void FixedUpdate()
        {
            if (_rb == null || _dashTime <= 0f)
            {
                return;
            }

            _dashTime -= Time.fixedDeltaTime;
            _rb.gravityScale = 0f;
            _rb.velocity = new Vector2(_dashDir * Plugin.DashSpeed.Value, 0f);

            // Dash is hold-to-continue: letting go ends it early, up to DashDuration.
            if (!Input.GetKey(Plugin.KeyDash.Value.MainKey))
            {
                _dashTime = 0f;
            }
            if (_dashTime <= 0f)
            {
                _rb.gravityScale = 1f;
                _body.maxSpeed = _savedMaxSpeed;
            }
        }

        private void LateUpdate()
        {
            if (_body == null || !Plugin.Enable.Value)
            {
                return;
            }

            float now = Time.time;
            UpdateGroundAndWall();
            HandleTurn();
            HandleJump();
            HandleWallAnim();
            HandleDash(now);
            HandleSlash(now);
            HandleNeedle(now);
        }

        private bool OurGrounded()
        {
            // While rising we are never grounded, even if the feet box still overlaps the ground.
            if (_rb != null && _rb.velocity.y > 0.5f)
            {
                return false;
            }
            if (_body.col == null)
            {
                return _body.grounded;
            }
            Vector2 size = new Vector2(Mathf.Max(_body.col.size.x * 0.9f, 0.3f), 0.22f);
            Vector2 pos = (Vector2)_body.transform.position + _body.col.offset + Vector2.down * 0.06f;
            return Physics2D.OverlapBox(pos, size, 0f, LayerMask.GetMask("Ground")) != null;
        }

        private void UpdateGroundAndWall()
        {
            _ourGrounded = OurGrounded();
            _body.grounded = _ourGrounded;
            if (_ourGrounded)
            {
                _airJumps = 0;
            }

            _wallSide = 0;
            if (!_ourGrounded)
            {
                if (Physics2D.Raycast(_body.transform.position, Vector2.right, 0.85f, LayerMask.GetMask("Ground")))
                {
                    _wallSide = 1;
                }
                else if (Physics2D.Raycast(_body.transform.position, Vector2.left, 0.85f, LayerMask.GetMask("Ground")))
                {
                    _wallSide = -1;
                }
            }
        }

        private void HandleTurn()
        {
            bool facing = _avatar.FacingRight;
            if (facing != _lastFacingRight)
            {
                _lastFacingRight = facing;
                if (_ourGrounded && Mathf.Abs(_body.moveDir.x) > 0.1f)
                {
                    _avatar.PlayAction("Turn", 0.22f);
                }
            }
        }

        private void HandleJump()
        {
            KeyCode jump = KeyBinds.GetBind("jump");
            if (jump == KeyCode.None || !Input.GetKeyDown(jump) || _rb == null)
            {
                return;
            }

            if (_ourGrounded)
            {
                _body.endedJump = false; // CU doubles gravity while endedJump is set
                _body.grounded = false;
                _rb.gravityScale = 1f;
                _rb.velocity = new Vector2(_rb.velocity.x, Plugin.JumpSpeed.Value);
                _avatar.PlayAction("Airborne", 0.25f);
                Plugin.Log.LogInfo("move: ground jump vy=" + Plugin.JumpSpeed.Value);
                return;
            }

            if (_wallSide != 0)
            {
                _body.endedJump = false;
                _body.grounded = false;
                _rb.gravityScale = 1f;
                _rb.velocity = new Vector2(-_wallSide * Plugin.WallJumpX.Value, Plugin.WallJumpY.Value);
                _airJumps = 0; // clinging refreshes the double jump
                _avatar.PlayAction("Walljump", 0.3f);
                Plugin.Log.LogInfo("move: wall jump side=" + _wallSide);
                return;
            }

            if (_airJumps < 1)
            {
                _airJumps++;
                _body.endedJump = false;
                _rb.gravityScale = 1f;
                _rb.velocity = new Vector2(_rb.velocity.x, Plugin.DoubleJumpSpeed.Value);
                _avatar.PlayAction("Double Jump", 0.4f);
                Plugin.Log.LogInfo("move: double jump");
            }
        }

        private void HandleWallAnim()
        {
            if (_ourGrounded || _wallSide == 0 || _rb == null || _rb.velocity.y >= 0f)
            {
                return;
            }
            if (Time.time >= _wallActionUntil)
            {
                _avatar.PlayAction("Wall Slide", 0.2f);
                _wallActionUntil = Time.time + 0.15f;
            }
        }

        private void HandleDash(float now)
        {
            if (_dashTime > 0f || now < _dashReady || !Input.GetKeyDown(Plugin.KeyDash.Value.MainKey))
            {
                return;
            }
            _dashDir = _avatar.FacingRight ? 1f : -1f;
            _dashTime = Plugin.DashDuration.Value;
            _dashReady = now + Plugin.DashDuration.Value + Plugin.DashCooldown.Value;
            _savedMaxSpeed = _body.maxSpeed;
            _body.maxSpeed = Mathf.Max(_savedMaxSpeed, Plugin.DashSpeed.Value * 2f);
            _body.endedJump = false;
            _avatar.PlayAction("Dash", Plugin.DashDuration.Value + 0.1f);
            Plugin.Log.LogInfo("move: dash dir=" + _dashDir);
        }

        private void HandleSlash(float now)
        {
            if (now < _slashUntil)
            {
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
            else if (Input.GetKey(KeyCode.S) && !_ourGrounded)
            {
                clip = "DownSpike";
            }
            else
            {
                clip = "Slash";
            }

            float dur = 0.25f;
            HornetClip c;
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
                : (_avatar.FacingRight ? Vector2.right : Vector2.left);
            Vector2 center = origin + dir * Plugin.SlashReach.Value;
            Vector2 size = new Vector2(Plugin.SlashRange.Value, Plugin.SlashRange.Value);

            bool hit = false;
            foreach (Collider2D col in Physics2D.OverlapBoxAll(center, size, 0f))
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
                if (col.CompareTag("BlockGround") || col.GetComponent<Damageable>() != null)
                {
                    hit = true;
                }
            }

            if (hit && _slashClip == "DownSpike" && !_ourGrounded)
            {
                Pogo();
            }
        }

        private void Pogo()
        {
            if (_rb == null)
            {
                return;
            }
            float up = Plugin.PogoSpeed.Value;
            if (Input.GetKey(KeyBinds.GetBind("jump")))
            {
                up += Plugin.PogoHeldBonus.Value;
            }
            _body.endedJump = false;
            _rb.gravityScale = 1f;
            _rb.velocity = new Vector2(_rb.velocity.x, up);
            if (_body.baseLimb != null && _body.baseLimb.rb != null)
            {
                _body.baseLimb.rb.velocity = new Vector2(_body.baseLimb.rb.velocity.x, up);
            }
            _airJumps = 0;
            Plugin.Log.LogInfo("move: pogo bounce (" + up + ")");
        }

        private void HandleNeedle(float now)
        {
            if (_needle != null || now < _needleReady || !Input.GetKeyDown(Plugin.KeyNeedle.Value.MainKey))
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
    }
}
