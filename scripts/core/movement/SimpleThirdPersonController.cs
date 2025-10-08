using Godot;
using System;

public partial class SimpleThirdPersonController : CharacterBody3D
{
	[Export] public float WalkSpeed { get; set; } = 6.0f;
	[Export] public float SprintMultiplier { get; set; } = 1.5f;
	[Export] public float Acceleration { get; set; } = 16.0f;
	[Export] public float Gravity { get; set; } = 24.0f;
	[Export] public float JumpVelocity { get; set; } = 6.5f;
	[Export] public float MouseSensitivity { get; set; } = 0.12f;
	[Export] public float CameraDistance { get; set; } = 4.5f;
	[Export] public float CameraHeight { get; set; } = 1.8f;
	[Export] public float MinPitchDeg { get; set; } = -60f;
	[Export] public float MaxPitchDeg { get; set; } = 80f;

	// Dash
	[Export] public float DashSpeed { get; set; } = 18f;
	[Export] public float DashDuration { get; set; } = 0.15f;
	[Export] public float DashCooldown { get; set; } = 0.8f;
	private bool _isDashing;
	private float _dashTimer;
	private float _dashCooldownTimer;
	private Vector3 _dashVelocity;

	// Slide
	[Export] public float SlideDuration { get; set; } = 0.7f;
	[Export] public float MinSlideSpeed { get; set; } = 5f;
	[Export] public float SlideFriction { get; set; } = 6f;
	private bool _isSliding;
	private float _slideTimer;
	private Vector3 _slideDir;

	// Wall jump
	[Export] public float WallJumpForce { get; set; } = 8f;
	[Export] public float WallDetectionDistance { get; set; } = 1.1f;

	// Mantle
	[Export] public float MantleCheckDistance { get; set; } = 1.1f;
	[Export] public float MantleHeight { get; set; } = 1.2f;
	[Export] public float MantleDuration { get; set; } = 0.35f;
	private bool _isMantling;
	private Vector3 _mantleTarget;
	private Tween _mantleTween;

	// Air-strafe
	[Export] public float AirAcceleration { get; set; } = 10f;
	[Export] public float AirControl { get; set; } = 0.35f;
	[Export] public float MaxAirSpeed { get; set; } = 8.5f;

	// Momentum tuning
	[Export] public float MomentumCarryGround { get; set; } = 0.9f;
	[Export] public float MomentumCarryAir { get; set; } = 0.95f;
	[Export] public float DashToSlideWindow { get; set; } = 0.18f;
	private float _postDashWindow;

	private Camera3D _camera;
	private Label _speedLabel;
	private ProgressBar _staminaBar;

	private float _yawDegrees;
	private float _pitchDegrees;
	private Vector3 _velocity;

	private StaminaComponent _stamina;
	private int _lastShownCharges = -1;

	public override void _Ready()
	{
		_camera = GetNodeOrNull<Camera3D>("Camera3D");
		_stamina = GetNodeOrNull<StaminaComponent>("StaminaComponent");

		var ui = GetTree().CurrentScene?.GetNodeOrNull<Node>("MovementUI");
		if (ui != null)
		{
			_speedLabel = ui.GetNodeOrNull<Label>("SpeedLabel");
			_staminaBar = ui.GetNodeOrNull<ProgressBar>("StaminaBar");
		}

		if (_camera == null)
		{
			_camera = new Camera3D();
			AddChild(_camera);
		}

		_yawDegrees = RotationDegrees.Y;
		_pitchDegrees = 10f;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motion)
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

		// Build yaw basis (Godot forward is -Z)
		var yawRad = Mathf.DegToRad(_yawDegrees);
		var yawBasis = new Basis(Vector3.Up, yawRad);
		var camForward = (yawBasis * Vector3.Forward).Normalized(); // Vector3.Forward = (0,0,-1)
		var camRight = (yawBasis * Vector3.Right).Normalized();

		// Dash start (requires charge if stamina present)
		if (!_isDashing && Input.IsActionJustPressed("dash") && _dashCooldownTimer <= 0)
		{
			if (_stamina == null || _stamina.TryUseCharge(1))
			{
				var dir = (!Mathf.IsZeroApprox(inputX) || !Mathf.IsZeroApprox(inputZ))
					? (camRight * inputX + camForward * inputZ).Normalized()
					: camForward;
				StartDash(dir);
			}
		}

		// Slide start
		if (!_isSliding && IsOnFloor() && (Input.IsActionJustPressed("slide") || Input.IsActionJustPressed("crouch")) && new Vector3(_velocity[0], 0, _velocity[2]).Length() >= MinSlideSpeed)
		{
			StartSlide(new Vector3(_velocity[0], 0, _velocity[2]).Normalized());
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
			}
		}
		else
		{
			// Regular movement / slide
			if (_isSliding)
			{
				_slideTimer -= (float)delta;
				var speed = new Vector3(_velocity[0], 0, _velocity[2]).Length();
				speed = Mathf.Max(speed - SlideFriction * (float)delta, MinSlideSpeed * 0.5f);
				var slideVel = _slideDir * speed;
				_velocity[0] = slideVel[0];
				_velocity[2] = slideVel[2];
				if (_slideTimer <= 0 || !IsOnFloor())
				{
					_isSliding = false;
				}
			}
			else
			{
				if (!Mathf.IsZeroApprox(inputX) || !Mathf.IsZeroApprox(inputZ))
				{
					var wishDir = (camRight * inputX + camForward * inputZ).Normalized();
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
		var from = GlobalPosition + Vector3.Up * 1.0f;
		var to = from + camForward * WallDetectionDistance;
		var space = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(from, to);
		query.CollideWithAreas = false;
		query.CollideWithBodies = true;
		var result = space.IntersectRay(query);
		if (result.Count > 0)
		{
			var normal = (Vector3)result["normal"];
			var jumpDir = (normal + Vector3.Up).Normalized();
			_velocity = jumpDir * WallJumpForce;
			return true;
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
		if (_staminaBar != null && _stamina != null)
		{
			// Represent charges on a progress bar: value shows next charge progress, max is max charges
			_staminaBar.MaxValue = _stamina.GetMaxCharges();
			var charges = _stamina.GetCurrentCharges();
			var next = _stamina.GetNextChargeProgress01();
			_staminaBar.Value = Mathf.Clamp(charges + next, 0, _stamina.GetMaxCharges());
		}
	}
}
