using Godot;

[GlobalClass]
public partial class MovementConfig : Resource
{
	// Ground Movement
	[ExportGroup("Ground Movement")]
	[Export] public float WalkSpeed { get; set; } = 6.0f;
	[Export] public float SprintMultiplier { get; set; } = 1.5f;
	[Export] public float Acceleration { get; set; } = 16.0f;
	[Export] public float AirControl { get; set; } = 0.35f;
	[Export] public float AirAcceleration { get; set; } = 10f;
	[Export] public float MaxAirSpeed { get; set; } = 8.5f;
	[Export] public float MomentumCarryGround { get; set; } = 0.9f;
	[Export] public float MomentumCarryAir { get; set; } = 0.95f;

	[ExportGroup("Momentum")]
	[Export] public float DashToSlideWindow { get; set; } = 0.18f;

	// Physics
	[ExportGroup("Physics")]
	[Export] public float Gravity { get; set; } = 24.0f;
	[Export] public float JumpVelocity { get; set; } = 6.5f;

	// Dash
	[ExportGroup("Dash")]
	[Export] public float DashSpeed { get; set; } = 18f;
	[Export] public float DashDuration { get; set; } = 0.15f;
	[Export] public float DashCooldown { get; set; } = 0.8f;
	[Export] public bool DashConsumesStamina { get; set; } = true;

	// Slide
	[ExportGroup("Slide")]
	[Export] public float SlideDuration { get; set; } = 0.7f;
	[Export] public float MinSlideSpeed { get; set; } = 5f;
	[Export] public float SlideFriction { get; set; } = 6f;
	[Export] public float SlopeSlideAcceleration { get; set; } = 20f;
	[Export] public float MaxWalkableSlopeDegrees { get; set; } = 40f;

	// Wall Jump
	[ExportGroup("Wall Jump")]
	[Export] public float WallJumpForce { get; set; } = 8f;
	[Export] public float WallDetectionDistance { get; set; } = 1.1f;
	[Export] public float WallNormalYThreshold { get; set; } = 0.5f;
	[Export] public bool WallJumpConsumesStamina { get; set; } = true;

	// Mantle
	[ExportGroup("Mantle")]
	[Export] public float MantleCheckDistance { get; set; } = 1.1f;
	[Export] public float MantleHeight { get; set; } = 1.2f;
	[Export] public float MantleDuration { get; set; } = 0.35f;

	// Air Movement
	[ExportGroup("Air Movement")]
	[Export] public float FastDescentVelocity { get; set; } = -20f;
	[Export] public float FastDescentDoubleTapWindow { get; set; } = 0.25f;
	[Export] public bool FastDescentConsumesStamina { get; set; } = true;
	[Export] public bool EnableBunnyHop { get; set; } = false;

	// Stamina
	[ExportGroup("Stamina")]
	[Export] public int MaxCharges { get; set; } = 3;
	[Export] public float RechargeTimeSeconds { get; set; } = 3.0f;
	[Export] public bool AutoRecharge { get; set; } = true;

	// Camera
	[ExportGroup("Camera")]
	[Export] public float MouseSensitivity { get; set; } = 0.12f;
	[Export] public float CameraDistance { get; set; } = 4.5f;
	[Export] public float CameraHeight { get; set; } = 1.8f;
	[Export] public float MinPitchDeg { get; set; } = -60f;
	[Export] public float MaxPitchDeg { get; set; } = 80f;
}

