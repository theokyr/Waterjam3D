using Godot;
using System;

public partial class MantleComponent : Node
{
    [Export] public float MantleHeight { get; set; } = 2f;
    [Export] public float MantleDistance { get; set; } = 1f;
    [Export] public float MantleDuration { get; set; } = 0.5f;
    [Export] public float MantleCooldown { get; set; } = 0.3f;

    [Signal] public delegate void MantleStartedEventHandler(Vector3 mantleTarget);
    [Signal] public delegate void MantleCompletedEventHandler();

    private bool canMantle = true;
    private float mantleCooldownTimer = 0f;
    private bool isMantling = false;
    private Tween mantleTween;

    public override void _PhysicsProcess(double delta)
    {
        UpdateMantleCooldown(delta);
    }

    public bool TryMantle()
    {
        if (!canMantle || isMantling) return false;

        var mantleCheck = PerformMantleCheck();
        if (mantleCheck.canMantle)
        {
            StartMantle(mantleCheck.mantlePosition);
            return true;
        }

        return false;
    }

    private MantleCheckResult PerformMantleCheck()
    {
        var character = GetParent<CharacterBody3D>();
        var checkOrigin = character.GlobalPosition + Vector3.Up * (MantleHeight * 0.5f);

        // Check for ledge in front of player
        var forwardCheck = new PhysicsRayQueryParameters3D
        {
            From = checkOrigin,
            To = checkOrigin + character.GlobalTransform.Basis.Z * MantleDistance,
            CollideWithAreas = false,
            CollideWithBodies = true
        };

        var forwardResult = character.GetWorld3D().DirectSpaceState.IntersectRay(forwardCheck);

        if (forwardResult.Count == 0) return new MantleCheckResult { canMantle = false };

        // Check for space above the ledge
        var ledgePosition = (Vector3)forwardResult["position"];
        var upwardCheck = new PhysicsRayQueryParameters3D
        {
            From = ledgePosition,
            To = ledgePosition + Vector3.Up * (MantleHeight * 0.5f),
            CollideWithAreas = false,
            CollideWithBodies = true
        };

        var upwardResult = character.GetWorld3D().DirectSpaceState.IntersectRay(upwardCheck);

        if (upwardResult.Count == 0)
        {
            var mantlePosition = ledgePosition + Vector3.Up * (MantleHeight * 0.3f) + character.GlobalTransform.Basis.Z * 0.5f;
            return new MantleCheckResult { canMantle = true, mantlePosition = mantlePosition };
        }

        return new MantleCheckResult { canMantle = false };
    }

    private void StartMantle(Vector3 targetPosition)
    {
        isMantling = true;
        canMantle = false;
        mantleCooldownTimer = MantleCooldown;

        // Disable movement during mantle
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent.SetMovementEnabled(false);

        // Animate mantle
        mantleTween = GetParent<CharacterBody3D>().CreateTween();
        mantleTween.TweenProperty(GetParent<CharacterBody3D>(), "global_position", targetPosition, MantleDuration);
        mantleTween.TweenCallback(Callable.From(CompleteMantle));

        EmitSignal(SignalName.MantleStarted, targetPosition);
    }

    private void CompleteMantle()
    {
        isMantling = false;

        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent.SetMovementEnabled(true);

        EmitSignal(SignalName.MantleCompleted);
    }

    private void UpdateMantleCooldown(double delta)
    {
        if (!canMantle && mantleCooldownTimer > 0)
        {
            mantleCooldownTimer -= (float)delta;
            if (mantleCooldownTimer <= 0)
            {
                canMantle = true;
            }
        }
    }

    private struct MantleCheckResult
    {
        public bool canMantle;
        public Vector3 mantlePosition;
    }

    public bool IsMantling()
    {
        return isMantling;
    }
}
