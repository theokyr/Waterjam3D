using Godot;
using System;

public enum MovementState
{
    Idle, Walking, Sprinting, Crouching,
    Jumping, Falling, Sliding, Dashing,
    WallSliding, Mantling, Ziplining
}

public partial class MovementComponent : Node
{
    [Export] public float WalkSpeed { get; set; } = 7f;
    [Export] public float SprintSpeed { get; set; } = 10f;
    [Export] public float Acceleration { get; set; } = 15f;
    [Export] public float Friction { get; set; } = 8f;
    [Export] public float AirControl { get; set; } = 0.3f;
    [Export] public float AirAcceleration { get; set; } = 5f;
    [Export] public float Gravity { get; set; } = 20f;
    [Export] public float JumpForce { get; set; } = 8f;

    private CharacterBody3D character;
    private Vector3 velocity = Vector3.Zero;
    private bool canMove = true;
    private bool sprintEnabled = false;
    private float speedMultiplier = 1f;
    private float coyoteTime = 0.1f;
    private float coyoteTimer = 0f;

    public MovementState CurrentState { get; private set; } = MovementState.Idle;

    public override void _Ready()
    {
        character = GetParent<CharacterBody3D>();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!canMove) return;

        UpdateCoyoteTime(delta);
        HandleGroundMovement(delta);
        HandleAirMovement(delta);
        ApplyGravity(delta);
        UpdateVelocity();
        UpdateMovementState();
    }

    public void SetMovementEnabled(bool enabled) => canMove = enabled;
    public void SetSprintEnabled(bool enabled) => sprintEnabled = enabled;
    public void SetSpeedMultiplier(float multiplier) => speedMultiplier = multiplier;
    public Vector3 GetVelocity() => velocity;
    public void SetVelocity(Vector3 newVelocity) => velocity = newVelocity;
    public MovementState GetCurrentState() => CurrentState;

    public void HandleJump()
    {
        if (CanJump())
        {
            velocity[1] = JumpForce;
            coyoteTimer = 0f;
        }
    }

    private bool CanJump()
    {
        return (character.IsOnFloor() || coyoteTimer > 0) && canMove;
    }

    private void UpdateCoyoteTime(double delta)
    {
        if (character.IsOnFloor())
        {
            coyoteTimer = coyoteTime;
        }
        else
        {
            coyoteTimer -= (float)delta;
        }
    }

    private void HandleGroundMovement(double delta)
    {
        if (!character.IsOnFloor()) return;

        var inputDirection = GetInputDirection();
        var targetSpeed = sprintEnabled ? SprintSpeed : WalkSpeed;
        targetSpeed *= speedMultiplier;

        // Smooth acceleration to target speed
        var currentSpeed = new Vector3(velocity[0], 0, velocity[2]).Length();
        var speedDifference = targetSpeed - currentSpeed;

        if (speedDifference > 0)
        {
            var accelerationAmount = Mathf.Min(speedDifference, Acceleration * (float)delta);
            velocity += inputDirection * (float)accelerationAmount;
        }
        else
        {
            velocity = velocity.Lerp(Vector3.Zero, Friction * (float)delta);
        }

        // Orient movement direction
        if (inputDirection != Vector3.Zero)
        {
            var targetDirection = inputDirection.Normalized();
            velocity = velocity.Normalized() * Mathf.Clamp(velocity.Length(), 0, targetSpeed);
            velocity = velocity.Slide(targetDirection).Normalized() * velocity.Length();
        }
    }

    private void HandleAirMovement(double delta)
    {
        if (character.IsOnFloor()) return;

        var inputDirection = GetInputDirection();

        // Apply air strafing
        velocity[0] = Mathf.Lerp(velocity[0], inputDirection[0] * WalkSpeed * AirControl * speedMultiplier, AirAcceleration * (float)delta);
        velocity[2] = Mathf.Lerp(velocity[2], inputDirection[2] * WalkSpeed * AirControl * speedMultiplier, AirAcceleration * (float)delta);

        // Clamp maximum air speed
        var horizontalVelocity = new Vector3(velocity[0], 0, velocity[2]);
        var maxAirSpeed = WalkSpeed * 1.2f * speedMultiplier;

        if (horizontalVelocity.Length() > maxAirSpeed)
        {
            horizontalVelocity = horizontalVelocity.Normalized() * maxAirSpeed;
            velocity = new Vector3(horizontalVelocity[0], velocity[1], horizontalVelocity[2]);
        }
    }

    private void ApplyGravity(double delta)
    {
        if (!character.IsOnFloor())
        {
            velocity[1] -= Gravity * (float)delta;
        }
        else
        {
            velocity[1] = 0;
        }
    }

    private void UpdateVelocity()
    {
        character.Velocity = velocity;
        character.MoveAndSlide();
        velocity = character.Velocity;
    }

    private void UpdateMovementState()
    {
        if (!character.IsOnFloor())
        {
            CurrentState = velocity[1] > 0 ? MovementState.Jumping : MovementState.Falling;
        }
        else
        {
            var inputDirection = GetInputDirection();
            var speed = velocity.Length();

            if (inputDirection == Vector3.Zero)
            {
                CurrentState = MovementState.Idle;
            }
            else if (sprintEnabled && speed > WalkSpeed * 0.9f)
            {
                CurrentState = MovementState.Sprinting;
            }
            else
            {
                CurrentState = MovementState.Walking;
            }
        }
    }

    private Vector3 GetInputDirection()
    {
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        return new Vector3(input[0], 0, input[1]).Rotated(Vector3.Up, character.GlobalRotation[1]);
    }
}
