using Godot;
using System;

public partial class SlideComponent : Node
{
    [Export] public float SlideSpeed { get; set; } = 15f;
    [Export] public float SlideFriction { get; set; } = 0.3f;
    [Export] public float SlideDuration { get; set; } = 1.5f;
    [Export] public float MinSlideSpeed { get; set; } = 8f;

    [Signal] public delegate void SlideStartedEventHandler();
    [Signal] public delegate void SlideEndedEventHandler();

    private bool isSliding = false;
    private float slideTimer = 0f;
    private Vector3 slideStartVelocity = Vector3.Zero;
    private CrouchComponent crouchComponent;

    public override void _Ready()
    {
        crouchComponent = GetNode<CrouchComponent>("../CrouchComponent");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (isSliding)
        {
            UpdateSlide(delta);
        }
    }

    public bool TryStartSlide()
    {
        var character = GetParent<CharacterBody3D>();
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");

        if (character.IsOnFloor() && movementComponent.GetVelocity().Length() >= MinSlideSpeed)
        {
            StartSlide();
            return true;
        }

        return false;
    }

    private void StartSlide()
    {
        isSliding = true;
        slideTimer = SlideDuration;

        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        slideStartVelocity = movementComponent.GetVelocity();

        // Force crouch during slide
        crouchComponent?.ForceCrouch();

        EmitSignal(SignalName.SlideStarted);
    }

    private void UpdateSlide(double delta)
    {
        slideTimer -= (float)delta;

        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        var character = GetParent<CharacterBody3D>();

        // Calculate slide velocity
        var slideDirection = slideStartVelocity.Normalized();
        var currentSpeed = slideStartVelocity.Length() * (1f - SlideFriction * (SlideDuration - slideTimer) / SlideDuration);

        var slideVelocity = slideDirection * Mathf.Max(currentSpeed, SlideSpeed);

        // Maintain slide direction while allowing slight steering
        var inputDirection = GetInputDirection();
        if (inputDirection != Vector3.Zero)
        {
            slideVelocity = slideVelocity.Slide(inputDirection).Normalized() * slideVelocity.Length();
        }

        movementComponent.SetVelocity(slideVelocity);

        // Check end conditions
        if (ShouldEndSlide())
        {
            EndSlide();
        }
    }

    private bool ShouldEndSlide()
    {
        var character = GetParent<CharacterBody3D>();
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");

        // End slide if too slow, not on floor, or timer expired
        return slideTimer <= 0 ||
               !character.IsOnFloor() ||
               movementComponent.GetVelocity().Length() < MinSlideSpeed * 0.5f;
    }

    private void EndSlide()
    {
        isSliding = false;

        // Restore standing height
        crouchComponent?.TryUncrouch();

        EmitSignal(SignalName.SlideEnded);
    }

    private Vector3 GetInputDirection()
    {
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        return new Vector3(input[0], 0, input[1]).Rotated(Vector3.Up, GetParent<CharacterBody3D>().GlobalRotation[1]);
    }

    public bool IsSliding()
    {
        return isSliding;
    }
}
