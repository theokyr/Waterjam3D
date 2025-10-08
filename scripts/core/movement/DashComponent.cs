using Godot;
using System;

public partial class DashComponent : Node
{
    [Export] public float DashSpeed { get; set; } = 25f;
    [Export] public float DashDuration { get; set; } = 0.15f;
    [Export] public float DashCooldown { get; set; } = 1f;
    [Export] public float StaminaCost { get; set; } = 25f;
    [Export] public float AirDashSpeed { get; set; } = 20f;

    [Signal] public delegate void DashStartedEventHandler(Vector3 dashDirection);
    [Signal] public delegate void DashEndedEventHandler();

    private bool canDash = true;
    private bool isDashing = false;
    private float dashTimer = 0f;
    private float cooldownTimer = 0f;
    private Vector3 dashDirection = Vector3.Zero;
    private Vector3 originalVelocity = Vector3.Zero;

    private StaminaComponent staminaComponent;

    public override void _Ready()
    {
        staminaComponent = GetNode<StaminaComponent>("../StaminaComponent");
    }

    public override void _PhysicsProcess(double delta)
    {
        UpdateDash(delta);
        UpdateCooldown(delta);
    }

    public bool TryDash(Vector3 direction)
    {
        if (!canDash || isDashing) return false;

        if (staminaComponent != null && !staminaComponent.TryConsumeSprint(1.0f))
        {
            return false;
        }

        StartDash(direction);
        return true;
    }

    private void StartDash(Vector3 direction)
    {
        isDashing = true;
        canDash = false;
        dashTimer = DashDuration;
        cooldownTimer = DashCooldown;

        // Store current velocity for strafe dashing
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        originalVelocity = movementComponent.GetVelocity();

        // Calculate dash direction (combine input with current momentum)
        dashDirection = CalculateDashDirection(direction);

        // Apply dash effects
        EmitSignal(SignalName.DashStarted, dashDirection);
    }

    private Vector3 CalculateDashDirection(Vector3 inputDirection)
    {
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        var currentVelocity = movementComponent.GetVelocity();

        // Strafe dashing: preserve horizontal momentum, add dash force
        if (currentVelocity.Length() > 0.1f)
        {
            var strafeDirection = new Vector3(currentVelocity[0], 0, currentVelocity[2]).Normalized();
            return (strafeDirection * 0.7f + inputDirection * 0.3f).Normalized();
        }

        return inputDirection.Normalized();
    }

    private void UpdateDash(double delta)
    {
        if (!isDashing) return;

        dashTimer -= (float)delta;
        if (dashTimer <= 0)
        {
            EndDash();
            return;
        }

        // Apply dash velocity
        var character = GetParent<CharacterBody3D>();
        var dashVelocity = dashDirection * (character.IsOnFloor() ? DashSpeed : AirDashSpeed);

        // Preserve some vertical momentum for air dashes
        if (!character.IsOnFloor())
        {
            dashVelocity[1] = originalVelocity[1] * 0.5f;
        }

        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent.SetVelocity(dashVelocity);
    }

    private void EndDash()
    {
        isDashing = false;
        EmitSignal(SignalName.DashEnded);

        // Restore partial control after dash
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        var currentVelocity = movementComponent.GetVelocity();
        movementComponent.SetVelocity(currentVelocity * 0.3f);
    }

    private void UpdateCooldown(double delta)
    {
        if (!canDash && cooldownTimer > 0)
        {
            cooldownTimer -= (float)delta;
            if (cooldownTimer <= 0)
            {
                canDash = true;
            }
        }
    }

    public bool IsDashing()
    {
        return isDashing;
    }
}
