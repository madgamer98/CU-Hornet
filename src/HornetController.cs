using UnityEngine;

namespace HornetInCasualties
{
    /// <summary>
    /// Adds Hornet's moves to the vanilla player body. Movement physics (walk, jump, wall
    /// slide/jump) stay CU's; this adds dash, slash (fwd/up/down), pogo, double jump and the
    /// needle throw, and drives the matching baked clips.
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
        private float _wallActionUntil;
        private float _wallJumpUntil;
        private bool _wasSliding;
        private bool _lastFacingRight = true;
        private float _lastGroundedTime;

        public void Init(HornetAvatar avatar, Body body)
        {
            _avatar = avatar;
            _body = body;
            _rb = body.rb;
            Plugin.Log.LogInfo("Hornet: kinematic mode (limbs puppeted).");
        }

        // Keep the ragdoll's colliders (they provide world collision) but stop it flopping by
        // matching every limb's velocity to the body each physics step.
        private void PuppetRagdoll()
        {
            if (_body.limbs == null || _rb == null)
            {
                return;
            }
            Vector2 v = _rb.velocity;
            foreach (Limb limb in _body.limbs)
            {
                if (limb != null && limb.rb != null)
                {
                    limb.rb.velocity = v;
                }
            }
        }

        private void FixedUpdate()
        {
            if (_rb == null)
            {
                return;
            }

            if (_dashTime <= 0f)
            {
                // Clean kinematic locomotion (vanilla FixedUpdate is skipped in this mode).
                if (Plugin.KinematicMode.Value && Plugin.EnableMoves.Value)
                {
                    _rb.gravityScale = 1f;
                    float target = _body.moveDir.x * Plugin.MoveSpeed.Value;
                    float accel = Plugin.MoveAccel.Value * (_body.grounded ? 1f : 0.65f);
                    float vx = Mathf.MoveTowards(_rb.velocity.x, target, accel * Time.fixedDeltaTime);
                    _rb.velocity = new Vector2(vx, _rb.velocity.y);
                    PuppetRagdoll();
                }
                return;
            }
            _dashTime -= Time.fixedDeltaTime;
            _rb.gravityScale = 0f;
            if (Plugin.KinematicMode.Value)
            {
                _rb.MovePosition(_rb.position + new Vector2(_dashDir * Plugin.DashSpeed.Value * Time.fixedDeltaTime, 0f));
                PuppetRagdoll();
            }
            else
            {
                // Vanilla movement still runs; the lifted speed cap lets this velocity stick.
                _rb.velocity = new Vector2(_dashDir * Plugin.DashSpeed.Value, 0f);
            }
            if (_dashTime <= 0f)
            {
                _rb.gravityScale = 1f;
                // Restore the vanilla speed cap we lifted for the dash.
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
            if (_body.grounded)
            {
                _lastGroundedTime = now;
            }
            HandleTurn();
            HandleJump();
            HandleWallAnim();
            HandleDash(now);
            HandleSlash(now);
            HandleNeedle(now);
        }

        private void HandleTurn()
        {
            bool facing = _avatar.FacingRight;
            if (facing != _lastFacingRight)
            {
                _lastFacingRight = facing;
                if (_body.grounded && Mathf.Abs(_body.moveDir.x) > 0.1f)
                {
                    _avatar.PlayAction("Turn", 0.22f);
                }
            }
        }

        private void HandleJump()
        {
            KeyCode jump = KeyBinds.GetBind("jump");
            if (jump == KeyCode.None || !Input.GetKeyDown(jump))
            {
                return;
            }

            if (_body.grounded)
            {
                _airJumps = 0;
                return; // CU does the ground jump.
            }

            if (_wallJumpUntil > Time.time)
            {
                return; // a wall jump just happened
            }
            if (Time.time - _lastGroundedTime < 0.15f)
            {
                return; // ignore the press that left the ground
            }
            if (_airJumps < 1)
            {
                _airJumps++;
                if (_rb != null)
                {
                    _rb.velocity = new Vector2(_rb.velocity.x, Plugin.DoubleJumpSpeed.Value);
                }
                _avatar.PlayAction("Double Jump", 0.4f);
                Plugin.Log.LogInfo("move: double jump");
            }
        }

        // CU owns wall slide/jump physics; we only play Hornet's animations for them.
        private void HandleWallAnim()
        {
            if (_body.grounded)
            {
                _wasSliding = false;
                _airJumps = 0;
                return;
            }

            bool sliding = _body.timeSlidfor > 0.05f;
            if (sliding && _rb != null && _rb.velocity.y < 0f)
            {
                if (Time.time >= _wallActionUntil)
                {
                    _avatar.PlayAction("Wall Slide", 0.2f);
                    _wallActionUntil = Time.time + 0.15f;
                }
                _airJumps = 0; // touching a wall refreshes the double jump
            }
            else if (_wasSliding && _rb != null && _rb.velocity.y > 1f)
            {
                _avatar.PlayAction("Walljump", 0.3f);
                _wallJumpUntil = Time.time + 0.25f;
                Plugin.Log.LogInfo("move: wall jump (vanilla)");
            }
            _wasSliding = sliding;
        }

        private void HandleDash(float now)
        {
            if (_dashTime > 0f || now < _dashReady)
            {
                return;
            }
            if (!Input.GetKeyDown(Plugin.KeyDash.Value.MainKey))
            {
                return;
            }
            _dashDir = _avatar.FacingRight ? 1f : -1f;
            _dashTime = Plugin.DashDuration.Value;
            _dashReady = now + Plugin.DashDuration.Value + Plugin.DashCooldown.Value;
            // CU clamps rb.velocity.x to actualMaxSpeed while grounded; lift the cap (and clear
            // endedJump, which would double gravity) so the dash keeps its distance.
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
            else if (Input.GetKey(KeyCode.S) && !_body.grounded)
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
                if (col.CompareTag("BlockGround") || col.GetComponent<Damageable>() != null)
                {
                    hit = true;
                }
            }

            if (hit && _slashClip == "DownSpike" && !_body.grounded)
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
            _rb.velocity = new Vector2(_rb.velocity.x, Plugin.PogoSpeed.Value);
            if (_body.baseLimb != null && _body.baseLimb.rb != null)
            {
                _body.baseLimb.rb.velocity = new Vector2(_body.baseLimb.rb.velocity.x, Plugin.PogoSpeed.Value);
            }
            _body.grounded = false;
            _airJumps = 0;
            Plugin.Log.LogInfo("move: pogo bounce");
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
    }
}
