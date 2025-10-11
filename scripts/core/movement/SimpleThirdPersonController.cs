// ARCHIVED: 2025-10-09
// This monolithic controller has been replaced by the component-based
// DeadlockMovementPlayer system with MovementConfig resource.
// Kept for reference only.
// See: DeadlockMovementPlayer.cs and components in scripts/core/movement/

using Godot;
using System;
using Waterjam.UI.Components;

public partial class SimpleThirdPersonController : CharacterBody3D, IStaminaSource
{
	[ExportGroup("Config")]
	[Export] public MovementConfig Config { get; set; }

	[ExportGroup("Movement/Ground")]
	[Export] public float WalkSpeed { get; set; } = 6.0f;
	[Export] public float SprintMultiplier { get; set; } = 1.5f;
	[Export] public float Acceleration { get; set; } = 16.0f;

	[ExportGroup("Movement/Physics")]
	[Export] public float Gravity { get; set; } = 24.0f;

	[ExportGroup("Jumping")]
	[Export] public float JumpVelocity { get; set; } = 6.5f;

	[ExportGroup("Camera")]
	[Export] public float MouseSensitivity { get; set; } = 0.12f;
	[Export] public float CameraDistance { get; set; } = 4.5f;
	[Export] public float CameraHeight { get; set; } = 1.8f;
	[Export] public float MinPitchDeg { get; set; } = -60f;
	[Export] public float MaxPitchDeg { get; set; } = 80f;
    [Export] public bool AlignFacingWithMovement { get; set; } = false;

	// Lightweight stamina (for UI only when StaminaComponent absent)
	[ExportGroup("Stamina (Lightweight)")]
	[Export] public int PlayerMaxStamina { get; set; } = 3;
	[Export] public float PlayerRechargeSeconds { get; set; } = 3.0f;
	private int _lwCharges;
	private float _lwTimer;
	private int _lastLoggedCharges = int.MinValue;

	// Dash
	[ExportGroup("Dash")]
	[Export] public float DashSpeed { get; set; } = 18f;
	[Export] public float DashDuration { get; set; } = 0.15f;
	[Export] public float DashCooldown { get; set; } = 0.8f;
	[Export] public bool DashConsumesStamina { get; set; } = true;
	private bool _isDashing;
	private float _dashTimer;
	private float _dashCooldownTimer;
	private Vector3 _dashVelocity;

	// Slide
	[ExportGroup("Slide")]
	[Export] public float SlideDuration { get; set; } = 0.7f;
	[Export] public float MinSlideSpeed { get; set; } = 5f;
	[Export] public float SlideFriction { get; set; } = 6f;
	[Export] public float MaxWalkableSlopeDegrees { get; set; } = 40f;
	[Export] public float SlopeSlideAcceleration { get; set; } = 20f;
    [Export] public float MaxSlideSpeed { get; set; } = 18f;
    [Export] public float SlideGroundGraceSeconds { get; set; } = 0.12f;
    [Export] public float SlideSnapLength { get; set; } = 0.5f;
	private bool _isSliding;
	private float _slideTimer;
	private Vector3 _slideDir;
	public bool IsOnSlope { get; private set; }
	private Vector3 _floorNormal = Vector3.Up;
	private float _slopeAngleDeg;
    private float _slideGroundGraceTimer;

	private const string MetaAnimDash = "anim_dash";
	private const string MetaAnimSlide = "anim_slide";
	private const string MetaAnimWallJumpTime = "anim_wall_jump_time";

	// Wall jump
	[ExportGroup("Wall Jump")]
	[Export] public float WallJumpForce { get; set; } = 8f;
	[Export] public float WallDetectionDistance { get; set; } = 1.1f;
	[Export] public float WallNormalYThreshold { get; set; } = 0.5f;
	[Export] public bool WallJumpConsumesStamina { get; set; } = true;
	private Vector3 _lastWallNormal = Vector3.Zero;
	private float _lastWallHitTime;

	// Mantle
	[ExportGroup("Mantle")]
	[Export] public float MantleCheckDistance { get; set; } = 1.1f;
	[Export] public float MantleHeight { get; set; } = 1.2f;
	[Export] public float MantleDuration { get; set; } = 0.35f;
	private bool _isMantling;
	private Vector3 _mantleTarget;
	private Tween _mantleTween;

	// Air-strafe
	[ExportGroup("Air Movement")]
	[Export] public float AirAcceleration { get; set; } = 10f;
	[Export] public float AirControl { get; set; } = 0.35f;
	[Export] public float MaxAirSpeed { get; set; } = 8.5f;
	[Export] public float FastDescentVelocity { get; set; } = -20f;
	[Export] public float FastDescentDoubleTapWindow { get; set; } = 0.25f;
	[Export] public bool FastDescentConsumesStamina { get; set; } = true;
	private float _lastCrouchTapTime;

	// Momentum tuning
	[ExportGroup("Momentum")]
	[Export] public float MomentumCarryGround { get; set; } = 0.9f;
	[Export] public float MomentumCarryAir { get; set; } = 0.95f;
	[Export] public float DashToSlideWindow { get; set; } = 0.18f;
	private float _postDashWindow;

    private Camera3D _camera;
    private SpringArm3D _springArm; // when present, we read yaw from this instead of mouse
	private Label _speedLabel;
	private MovementUI _movementUi;

	private float _yawDegrees;
	private float _pitchDegrees;
	private Vector3 _velocity;

	private StaminaComponent _stamina; // optional heavy component

	public override void _Ready()
	{
        _camera = GetNodeOrNull<Camera3D>("Camera3D");
        _springArm = GetNodeOrNull<SpringArm3D>("SpringArm3D");
        if (_springArm != null && _camera == null)
        {
            _camera = _springArm.GetNodeOrNull<Camera3D>("Camera3D");
        }
		_stamina = GetNodeOrNull<StaminaComponent>("StaminaComponent");

		// Apply MovementConfig if provided, then propagate to components
		if (Config != null)
		{
			ApplyConfigValues();
			if (_stamina != null)
			{
				_stamina.Config = Config;
			}
		}
		_lwCharges = PlayerMaxStamina;
		_lwTimer = 0f;

		var ui = GetTree().CurrentScene?.GetNodeOrNull<Control>("MovementUI");
		if (ui == null)
		{
			var ps = GD.Load<PackedScene>("res://scenes/MovementUI.tscn");
			if (ps != null && GetTree().CurrentScene != null)
			{
				ui = ps.Instantiate<Control>();
				ui.Name = "MovementUI";
				GetTree().CurrentScene.AddChild(ui);
			}
		}
		if (ui != null)
		{
			_speedLabel = ui.GetNodeOrNull<Label>("SpeedLabel");
			(ui as MovementUI)?.SetTargetCharacter(this);
		}

		// Ensure PauseMenu exists in scene
		var pause = GetTree().CurrentScene?.GetNodeOrNull<Control>("PauseMenu");
		if (pause == null)
		{
			var pauseScene = GD.Load<PackedScene>("res://scenes/ui/PauseMenu.tscn");
			if (pauseScene != null && GetTree().CurrentScene != null)
			{
				pause = pauseScene.Instantiate<Control>();
				pause.Name = "PauseMenu";
				GetTree().CurrentScene.AddChild(pause);
			}
		}

        if (_springArm == null)
        {
            if (_camera == null)
            {
                _camera = new Camera3D();
                AddChild(_camera);
            }
        }

		_yawDegrees = RotationDegrees.Y;
		_pitchDegrees = 10f;
		Input.MouseMode = Input.MouseModeEnum.Captured;

        // Default light snap to keep us grounded over tiny bumps when not sliding
        FloorSnapLength = 0.1f;
	}

	private void ApplyConfigValues()
	{
		// Movement/Ground
		WalkSpeed = Config.WalkSpeed;
		SprintMultiplier = Config.SprintMultiplier;
		Acceleration = Config.Acceleration;

		// Movement/Physics
		Gravity = Config.Gravity;

		// Jumping
		JumpVelocity = Config.JumpVelocity;

		// Camera
		MouseSensitivity = Config.MouseSensitivity;
		CameraDistance = Config.CameraDistance;
		CameraHeight = Config.CameraHeight;
		MinPitchDeg = Config.MinPitchDeg;
		MaxPitchDeg = Config.MaxPitchDeg;

		// Stamina (lightweight fallback)
		PlayerMaxStamina = Config.MaxCharges;
		PlayerRechargeSeconds = Config.RechargeTimeSeconds;

		// Dash
		DashSpeed = Config.DashSpeed;
		DashDuration = Config.DashDuration;
		DashCooldown = Config.DashCooldown;
		DashConsumesStamina = Config.DashConsumesStamina;

		// Slide
		SlideDuration = Config.SlideDuration;
		MinSlideSpeed = Config.MinSlideSpeed;
		SlideFriction = Config.SlideFriction;
		MaxWalkableSlopeDegrees = Config.MaxWalkableSlopeDegrees;
		SlopeSlideAcceleration = Config.SlopeSlideAcceleration;

		// Wall Jump
		WallJumpForce = Config.WallJumpForce;
		WallDetectionDistance = Config.WallDetectionDistance;
		WallNormalYThreshold = Config.WallNormalYThreshold;
		WallJumpConsumesStamina = Config.WallJumpConsumesStamina;

		// Mantle
		MantleCheckDistance = Config.MantleCheckDistance;
		MantleHeight = Config.MantleHeight;
		MantleDuration = Config.MantleDuration;

		// Air Movement
		AirAcceleration = Config.AirAcceleration;
		AirControl = Config.AirControl;
		MaxAirSpeed = Config.MaxAirSpeed;
		FastDescentVelocity = Config.FastDescentVelocity;
		FastDescentDoubleTapWindow = Config.FastDescentDoubleTapWindow;
		FastDescentConsumesStamina = Config.FastDescentConsumesStamina;

		// Momentum
		MomentumCarryGround = Config.MomentumCarryGround;
		MomentumCarryAir = Config.MomentumCarryAir;
		DashToSlideWindow = Config.DashToSlideWindow;
	}

	private bool ConsumeStaminaIfEnabled(bool consumeFlag)
	{
		if (!consumeFlag) return true;
		// Prefer heavy component if present; otherwise lightweight charges
		if (_stamina != null)
		{
			return _stamina.TryUseCharge(1);
		}
		return TryUseCharge(1);
	}

	public override void _Process(double delta)
	{
		// Lightweight recharge (UI only) when heavy component absent
		if (_stamina == null && _lwCharges < PlayerMaxStamina)
		{
			_lwTimer += (float)delta;
			if (_lwTimer >= PlayerRechargeSeconds)
			{
				_lwTimer -= PlayerRechargeSeconds;
				_lwCharges = Mathf.Clamp(_lwCharges + 1, 0, PlayerMaxStamina);
			}
		}
	}

	// IStaminaSource implementation (used by StaminaWheel if StaminaComponent missing)
	public int GetMaxCharges() => _stamina != null ? _stamina.GetMaxCharges() : PlayerMaxStamina;
	public int GetCurrentCharges() => _stamina != null ? _stamina.GetCurrentCharges() : _lwCharges;
	public float GetNextChargeProgress01()
	{
		if (_stamina != null) return _stamina.GetNextChargeProgress01();
		if (_lwCharges >= PlayerMaxStamina) return 1f;
		return Mathf.Clamp(_lwTimer / Mathf.Max(0.0001f, PlayerRechargeSeconds), 0f, 1f);
	}
	public bool TryUseCharge(int amount = 1)
	{
		if (_stamina != null) return _stamina.TryUseCharge(amount);
		if (amount <= 0) return true;
		if (_lwCharges >= amount)
		{
			_lwCharges -= amount;
			return true;
		}
		return false;
	}

    public override void _Input(InputEvent @event)
	{
        if (_springArm == null && @event is InputEventMouseMotion motion)
		{
			_yawDegrees -= motion.Relative.X * MouseSensitivity;
			_pitchDegrees -= motion.Relative.Y * MouseSensitivity;
			_pitchDegrees = Mathf.Clamp(_pitchDegrees, MinPitchDeg, MaxPitchDeg);
		}

		if (@event.IsActionPressed("ui_cancel"))
		{
			Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
				? Input.MouseModeEnum.Visible
				: Input.MouseModeEnum.Captured;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		UpdateCamera();

		if (_isMantling)
		{
			UpdateUI();
			return;
		}

		// cooldowns
		if (_dashCooldownTimer > 0) _dashCooldownTimer -= (float)delta;

        // Movement input (camera-relative)
		var inputVec = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		var inputX = inputVec[0];
		var inputZ = -inputVec[1]; // invert because GetVector returns negative for our "forward"

        // Build camera-relative basis
        Vector3 camForward;
        Vector3 camRight;
        if (_camera != null)
        {
            // Godot forward is -Z; flatten to horizontal
            var fwd = (-_camera.GlobalTransform.Basis.Z);
            camForward = new Vector3(fwd[0], 0, fwd[2]).Normalized();
            var right = _camera.GlobalTransform.Basis.X;
            camRight = new Vector3(right[0], 0, right[2]).Normalized();
        }
        else
        {
            var yawRad = Mathf.DegToRad(_yawDegrees);
            var yawBasis = new Basis(Vector3.Up, yawRad);
            camForward = (yawBasis * Vector3.Forward).Normalized();
            camRight = (yawBasis * Vector3.Right).Normalized();
        }

		// Dash start (requires charge if stamina present)
		if (!_isDashing && Input.IsActionJustPressed("dash") && _dashCooldownTimer <= 0)
		{
			if (!DashConsumesStamina || ConsumeStaminaIfEnabled(DashConsumesStamina))
			{
				var dir = (!Mathf.IsZeroApprox(inputX) || !Mathf.IsZeroApprox(inputZ))
					? (camRight * inputX + camForward * inputZ).Normalized()
					: camForward;
				StartDash(dir);
				SetMeta(MetaAnimDash, true);
			}
			else
			{
				Waterjam.Core.Systems.Console.ConsoleSystem.LogWarn("[Stamina] Not enough charges for Dash", Waterjam.Core.Systems.Console.ConsoleChannel.Player);
			}
		}

		// Update slope state (uses last frame floor normal if available)
		UpdateSlopeState();

        // Update slide ground grace window
        if (IsOnFloor()) _slideGroundGraceTimer = SlideGroundGraceSeconds; else _slideGroundGraceTimer = Mathf.Max(0f, _slideGroundGraceTimer - (float)delta);

		// Slide start (grounded). If on a slope, allow crouch/slide to start regardless of current speed
		if (!_isSliding && IsOnFloor() && (Input.IsActionJustPressed("slide") || Input.IsActionJustPressed("crouch")))
		{
			var horizontalSpeed = new Vector3(_velocity[0], 0, _velocity[2]).Length();
			var canStart = IsOnSlope ? true : horizontalSpeed >= MinSlideSpeed;
			if (canStart)
			{
				var dir = IsOnSlope
					? GetDownSlopeDirection()
					: (new Vector3(_velocity[0], 0, _velocity[2]).Length() > 0.01f
						? new Vector3(_velocity[0], 0, _velocity[2]).Normalized()
						: -GlobalTransform.Basis.Z);
				StartSlide(dir);
				SetMeta(MetaAnimSlide, true);
			}
		}

		// Fast descent: double-tap crouch while airborne
		if (!IsOnFloor() && Input.IsActionJustPressed("crouch"))
		{
			var now = (float)Time.GetTicksMsec() * 0.001f;
			if (now - _lastCrouchTapTime <= FastDescentDoubleTapWindow)
			{
				if (!FastDescentConsumesStamina || ConsumeStaminaIfEnabled(FastDescentConsumesStamina))
				{
					_velocity[1] = FastDescentVelocity;
				}
				else
				{
					Waterjam.Core.Systems.Console.ConsoleSystem.LogWarn("[Stamina] Not enough charges for Fast Descent", Waterjam.Core.Systems.Console.ConsoleChannel.Player);
				}
			}
			_lastCrouchTapTime = now;
		}

		// Mantle start
		if (Input.IsActionJustPressed("mantle"))
		{
			TryMantle(camForward);
		}

		if (_isDashing)
		{
			_dashTimer -= (float)delta;
			_velocity = _dashVelocity; // override during dash
			if (_dashTimer <= 0)
			{
				_isDashing = false;
				_postDashWindow = DashToSlideWindow;
				// clear dash anim flag
				SetMeta(MetaAnimDash, false);
			}
		}
		else
		{
			// Regular movement / slide
			if (_isSliding)
			{
				// Calculate current speed along slide direction (no uphill component)
				var horizontalVel = new Vector3(_velocity[0], 0, _velocity[2]);
				var signedSpeed = horizontalVel.Dot(_slideDir);
				signedSpeed = Mathf.Max(0f, signedSpeed);

                // Stick to ground while sliding
                FloorSnapLength = SlideSnapLength;
                if (_velocity[1] > -2f) _velocity[1] = -2f;

				if (IsOnSlope)
				{
					// Continuously align with downslope and accelerate while slide is held
					var downSlopeDir = GetDownSlopeDirection();
					if (downSlopeDir != Vector3.Zero)
						_slideDir = _slideDir.Lerp(downSlopeDir, 0.2f).Normalized();

					if (Input.IsActionPressed("slide") || Input.IsActionPressed("crouch"))
					{
						signedSpeed += SlopeSlideAcceleration * Mathf.Sin(Mathf.DegToRad(_slopeAngleDeg)) * (float)delta;
						signedSpeed = Mathf.Min(signedSpeed, MaxSlideSpeed);
					}
				}
				else
				{
					// Flat ground: decay speed and end when timer expires
					signedSpeed = Mathf.Max(signedSpeed - SlideFriction * (float)delta, 0f);
					_slideTimer -= (float)delta;
				}

				var slideVel = _slideDir * signedSpeed;
				_velocity[0] = slideVel[0];
				_velocity[2] = slideVel[2];

                var groundedLike = IsOnFloor() || _slideGroundGraceTimer > 0f;
                if (!groundedLike || (!IsOnSlope && (_slideTimer <= 0 || signedSpeed < 0.1f)) || !(Input.IsActionPressed("slide") || Input.IsActionPressed("crouch")))
				{
					// End slide if airborne, or on flat after timer/low speed, or released
					_isSliding = false;
					SetMeta(MetaAnimSlide, false);
                    FloorSnapLength = 0.1f; // restore default
				}
			}
			else
			{
                if (!Mathf.IsZeroApprox(inputX) || !Mathf.IsZeroApprox(inputZ))
				{
					var wishDir = (camRight * inputX + camForward * inputZ).Normalized();
					// Optional: face movement direction. Disabled by default to avoid camera coupling.
					if (AlignFacingWithMovement && IsOnFloor())
					{
						var newYaw = Mathf.RadToDeg(Mathf.Atan2(wishDir[0], wishDir[2]));
						RotationDegrees = new Vector3(0, newYaw, 0);
					}
					var targetSpeed = WalkSpeed * (Input.IsActionPressed("sprint") && CanSprint() ? SprintMultiplier : 1f);
					var horizontalVel = new Vector3(_velocity[0], 0, _velocity[2]);
					if (IsOnFloor())
					{
						horizontalVel = horizontalVel.Lerp(wishDir * targetSpeed, (float)delta * Acceleration);
					}
					else
					{
						// air-strafe: project current velocity and accelerate towards wishDir with caps
						var wishSpeed = MaxAirSpeed;
						var currentSpeed = horizontalVel.Dot(wishDir);
						var addSpeed = wishSpeed - currentSpeed;
						if (addSpeed > 0)
						{
							var accelSpeed = AirAcceleration * (float)delta * wishSpeed * AirControl;
							if (accelSpeed > addSpeed) accelSpeed = addSpeed;
							horizontalVel += wishDir * accelSpeed;
						}
					}
					_velocity[0] = horizontalVel[0];
					_velocity[2] = horizontalVel[2];
				}
				else
				{
					var factor = IsOnFloor() ? MomentumCarryGround : MomentumCarryAir;
					_velocity[0] *= Mathf.Pow(factor, (float)delta * 60f);
					_velocity[2] *= Mathf.Pow(factor, (float)delta * 60f);
				}
			}

			// Jump and gravity
			if (IsOnFloor())
			{
				_velocity[1] = 0;
				if (Input.IsActionJustPressed("jump"))
				{
					if (!TryWallJump(camForward))
					{
						_velocity[1] = JumpVelocity;
					}
				}
			}
			else
			{
				_velocity[1] -= Gravity * (float)delta;
			}

			// No per-second sprint consumption in charge model
		}

		// dash->slide chaining window
		if (_postDashWindow > 0)
		{
			_postDashWindow -= (float)delta;
			if (Input.IsActionJustPressed("slide") && IsOnFloor())
			{
				StartSlide(new Vector3(_velocity[0], 0, _velocity[2]).Normalized());
				_postDashWindow = 0;
			}
		}

		Velocity = _velocity;
		MoveAndSlide();
		// Capture floor info after move when grounded
		if (IsOnFloor())
		{
			_floorNormal = GetFloorNormal();
		}
		// Capture any wall collisions from this frame
		CaptureWallCollision();

		UpdateUI();
	}

	private void StartDash(Vector3 dir)
	{
		_isDashing = true;
		_dashTimer = DashDuration;
		_dashCooldownTimer = DashCooldown;
		_dashVelocity = dir * DashSpeed;
		_dashVelocity[1] = 0; // horizontal dash only
	}

	private void StartSlide(Vector3 dir)
	{
		_isSliding = true;
		_slideTimer = SlideDuration;
		_slideDir = dir;
	}

	private bool TryWallJump(Vector3 camForward)
	{
		if (IsOnFloor()) return false;
    // Prefer recent collision normal captured after movement
    if (_lastWallNormal != Vector3.Zero && _lastWallNormal.Dot(Vector3.Up) < WallNormalYThreshold)
    {
        if (!WallJumpConsumesStamina || ConsumeStaminaIfEnabled(WallJumpConsumesStamina))
        {
            var jumpFromCollision = (_lastWallNormal + Vector3.Up).Normalized();
            _velocity = jumpFromCollision * WallJumpForce;
            SetMeta(MetaAnimWallJumpTime, (float)Time.GetTicksMsec() * 0.001f);
            return true;
        }
        else
        {
            Waterjam.Core.Systems.Console.ConsoleSystem.LogWarn("[Stamina] Not enough charges for Wall Jump", Waterjam.Core.Systems.Console.ConsoleChannel.Player);
            return false;
        }
    }

    // Fallback: raycast in a fan around camera forward
    var origin = GlobalPosition + Vector3.Up * 1.0f;
    var space = GetWorld3D().DirectSpaceState;
    var directions = GetWallProbeDirections(camForward);
    foreach (var dir in directions)
    {
        var to = origin + dir * WallDetectionDistance;
        var query = PhysicsRayQueryParameters3D.Create(origin, to);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        var hit = space.IntersectRay(query);
        if (hit.Count > 0)
        {
            var normal = (Vector3)hit["normal"];
            if (normal.Dot(Vector3.Up) < WallNormalYThreshold)
            {
                if (!WallJumpConsumesStamina || ConsumeStaminaIfEnabled(WallJumpConsumesStamina))
                {
                    var jumpDir = (normal + Vector3.Up).Normalized();
                    _velocity = jumpDir * WallJumpForce;
                    SetMeta(MetaAnimWallJumpTime, (float)Time.GetTicksMsec() * 0.001f);
                    _lastWallNormal = normal;
                    _lastWallHitTime = (float)Time.GetTicksMsec() * 0.001f;
                    return true;
                }
                else
                {
                    Waterjam.Core.Systems.Console.ConsoleSystem.LogWarn("[Stamina] Not enough charges for Wall Jump", Waterjam.Core.Systems.Console.ConsoleChannel.Player);
                    return false;
                }
            }
        }
    }
    return false;
	}

	private void TryMantle(Vector3 camForward)
	{
		if (_isMantling) return;
		var origin = GlobalPosition + Vector3.Up * 1.0f;
		var forwardHit = Raycast(origin, origin + camForward * MantleCheckDistance);
		if (forwardHit.hit)
		{
			var ledge = forwardHit.position;
			var upHit = Raycast(ledge, ledge + Vector3.Up * MantleHeight);
			if (!upHit.hit)
			{
				_mantleTarget = ledge + Vector3.Up * 0.5f;
				BeginMantle();
			}
		}
	}

	private (bool hit, Vector3 position) Raycast(Vector3 from, Vector3 to)
	{
		var space = GetWorld3D().DirectSpaceState;
		var q = PhysicsRayQueryParameters3D.Create(from, to);
		q.CollideWithAreas = false;
		q.CollideWithBodies = true;
		var r = space.IntersectRay(q);
		if (r.Count > 0)
		{
			return (true, (Vector3)r["position"]);
		}
		return (false, Vector3.Zero);
	}

	private void BeginMantle()
	{
		_isMantling = true;
		_mantleTween = CreateTween();
		_mantleTween.TweenProperty(this, "global_position", _mantleTarget, MantleDuration);
		_mantleTween.Finished += () => { _isMantling = false; };
	}

    private void UpdateCamera()
	{
        // If a SpringArm exists, derive yaw from it and do not control a separate camera
        if (_springArm != null)
        {
            // Use the camera's global yaw for movement alignment; do not rotate the player here
            _yawDegrees = _springArm.GlobalRotationDegrees.Y;
            return;
        }

        RotationDegrees = new Vector3(0, _yawDegrees, 0);

        if (_camera == null) return;
        var yawRad = Mathf.DegToRad(_yawDegrees);
        var yawBasis = new Basis(Vector3.Up, yawRad);
        var forward = (yawBasis * Vector3.Forward).Normalized();
        var right = forward.Cross(Vector3.Up).Normalized();
        var pitchRad = Mathf.DegToRad(_pitchDegrees);
        var camDir = forward.Rotated(right, pitchRad).Normalized();

        var target = GlobalPosition + Vector3.Up * CameraHeight;
        var camPos = target - camDir * CameraDistance;
        _camera.GlobalPosition = camPos;
        _camera.LookAt(target, Vector3.Up);
	}

	private void UpdateSlopeState()
	{
		if (IsOnFloor())
		{
			// Use current floor normal when available; may reflect previous frame before MoveAndSlide
			var n = GetFloorNormal();
			_floorNormal = n;
			var dot = Mathf.Clamp(n.Dot(Vector3.Up), -1f, 1f);
			_slopeAngleDeg = Mathf.RadToDeg(Mathf.Acos(dot));
			IsOnSlope = _slopeAngleDeg > 2f && _slopeAngleDeg < 89f;
		}
		else
		{
			IsOnSlope = false;
			_slopeAngleDeg = 0f;
		}
	}

	private Vector3 GetDownSlopeDirection()
	{
		// Project global down onto the plane defined by the floor normal, then flatten to horizontal
		var downslope = (-Vector3.Up).Slide(_floorNormal);
		var horizontal = new Vector3(downslope[0], 0, downslope[2]);
		if (horizontal.Length() < 0.001f)
		{
			return Vector3.Zero;
		}
		return horizontal.Normalized();
	}

	private bool CanSprint()
	{
		return _stamina == null || _stamina.CanSprint();
	}

	private void UpdateUI()
	{
		if (_speedLabel != null)
		{
			var hSpeed = new Vector3(Velocity[0], 0, Velocity[2]).Length();
			_speedLabel.Text = $"Speed: {hSpeed:F2} m/s";
		}
		// StaminaWheel now binds directly to StaminaComponent via MovementUI; add debug logs for charges
		if (_stamina != null)
		{
			int charges = _stamina.GetCurrentCharges();
			if (charges != _lastLoggedCharges)
			{
				GD.Print($"[Stamina] charges={charges}/{_stamina.GetMaxCharges()} progress={_stamina.GetNextChargeProgress01():F2}");
				_lastLoggedCharges = charges;
			}
		}
	}

	private void CaptureWallCollision()
	{
		// Iterate slide collisions from last move
		var count = GetSlideCollisionCount();
		for (int i = 0; i < count; i++)
		{
			var col = GetSlideCollision(i);
			var n = col.GetNormal();
			// Ignore floor-like normals
			if (n.Dot(Vector3.Up) < WallNormalYThreshold)
			{
				_lastWallNormal = n;
				_lastWallHitTime = (float)Time.GetTicksMsec() * 0.001f;
				break;
			}
		}
		// Expire after short time window
		if (_lastWallNormal != Vector3.Zero)
		{
			var age = (float)Time.GetTicksMsec() * 0.001f - _lastWallHitTime;
			if (age > 0.25f) _lastWallNormal = Vector3.Zero;
		}
	}

	private Vector3[] GetWallProbeDirections(Vector3 camForward)
	{
		var forward = camForward.Normalized();
		var right = forward.Cross(Vector3.Up).Normalized();
		var dirs = new Vector3[5];
		dirs[0] = forward;
		dirs[1] = (forward.Rotated(Vector3.Up, Mathf.DegToRad(20f))).Normalized();
		dirs[2] = (forward.Rotated(Vector3.Up, Mathf.DegToRad(-20f))).Normalized();
		dirs[3] = right;
		dirs[4] = -right;
		return dirs;
	}
}
