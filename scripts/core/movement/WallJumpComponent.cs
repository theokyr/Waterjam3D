using Godot;
using System;

public partial class WallJumpComponent : Node
{
    [Export] public float WallJumpForce { get; set; } = 12f;
    [Export] public float WallJumpAngle { get; set; } = 45f;
    [Export] public float WallStickTime { get; set; } = 0.2f;
    [Export] public float WallDetectionDistance { get; set; } = 1.5f;
    [Export] public float CornerBoostMultiplier { get; set; } = 1.3f;

    [Signal] public delegate void WallJumpedEventHandler(Vector3 jumpDirection);
    [Signal] public delegate void WallDetectedEventHandler(Vector3 wallNormal);
    [Signal] public delegate void WallExitedEventHandler();

    private Vector3 currentWallNormal = Vector3.Zero;
    private float wallStickTimer = 0f;
    private bool isWallSticking = false;

    public override void _PhysicsProcess(double delta)
    {
        UpdateWallStick(delta);
        CheckForWalls();
    }

    public bool TryWallJump()
    {
        if (!CanWallJump()) return false;

        var jumpDirection = CalculateWallJumpDirection();
        var jumpVelocity = jumpDirection * WallJumpForce;

        // Add corner boost if jumping from corner
        var cornerBoost = CalculateCornerBoost();
        jumpVelocity += cornerBoost;

        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent.SetVelocity(jumpVelocity);

        EmitSignal(SignalName.WallJumped, jumpDirection);
        ExitWall();

        return true;
    }

    private bool CanWallJump()
    {
        return currentWallNormal != Vector3.Zero && !GetParent<CharacterBody3D>().IsOnFloor();
    }

    private void CheckForWalls()
    {
        var character = GetParent<CharacterBody3D>();
        var rayQuery = new PhysicsRayQueryParameters3D
        {
            From = character.GlobalPosition,
            To = character.GlobalPosition - character.GlobalTransform.Basis.Z * WallDetectionDistance,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = new Godot.Collections.Array<Rid> { character.GetRid() }
        };

        var result = character.GetWorld3D().DirectSpaceState.IntersectRay(rayQuery);

        if (result.Count > 0)
        {
            var wallNormal = (Vector3)result["normal"];
            var dotProduct = character.GlobalTransform.Basis.Z.Dot(wallNormal);

            // Only detect walls we're facing (dot product > 0.3)
            if (dotProduct > 0.3f && !isWallSticking)
            {
                EnterWall(wallNormal);
            }
        }
        else if (isWallSticking)
        {
            ExitWall();
        }
    }

    private void EnterWall(Vector3 wallNormal)
    {
        currentWallNormal = wallNormal;
        isWallSticking = true;
        wallStickTimer = WallStickTime;

        EmitSignal(SignalName.WallDetected, wallNormal);
    }

    private void ExitWall()
    {
        currentWallNormal = Vector3.Zero;
        isWallSticking = false;
        wallStickTimer = 0f;

        EmitSignal(SignalName.WallExited);
    }

    private void UpdateWallStick(double delta)
    {
        if (!isWallSticking) return;

        wallStickTimer -= (float)delta;
        if (wallStickTimer <= 0)
        {
            ExitWall();
        }
    }

    private Vector3 CalculateWallJumpDirection()
    {
        var character = GetParent<CharacterBody3D>();

        // Calculate jump direction (away from wall + up)
        var awayFromWall = currentWallNormal;
        var upwardDirection = Vector3.Up;

        // Angle the jump based on wall angle
        var jumpDirection = (awayFromWall + upwardDirection).Normalized();

        // Add some horizontal component based on input
        var inputDirection = GetInputDirection();
        if (inputDirection != Vector3.Zero)
        {
            var horizontalBoost = new Vector3(inputDirection[0], 0, inputDirection[2]).Normalized() * 0.3f;
            jumpDirection += horizontalBoost;
            jumpDirection = jumpDirection.Normalized();
        }

        return jumpDirection;
    }

    private Vector3 CalculateCornerBoost()
    {
        // Check for corner by casting additional rays
        var character = GetParent<CharacterBody3D>();

        // Cast rays at 45-degree angles to detect corners
        var cornerRays = new[]
        {
            character.GlobalTransform.Basis.X * 0.5f - character.GlobalTransform.Basis.Z,
            -character.GlobalTransform.Basis.X * 0.5f - character.GlobalTransform.Basis.Z
        };

        var cornerBoost = Vector3.Zero;
        var cornerCount = 0;

        foreach (var rayDirection in cornerRays)
        {
            var cornerQuery = new PhysicsRayQueryParameters3D
            {
                From = character.GlobalPosition,
                To = character.GlobalPosition + rayDirection * WallDetectionDistance * 0.7f,
                CollideWithAreas = false,
                CollideWithBodies = true
            };

            var cornerResult = character.GetWorld3D().DirectSpaceState.IntersectRay(cornerQuery);
            if (cornerResult.Count > 0)
            {
                cornerCount++;
                var normal = (Vector3)cornerResult["normal"];
                cornerBoost += normal.Normalized() * 0.5f;
            }
        }

        if (cornerCount >= 1)
        {
            return cornerBoost.Normalized() * WallJumpForce * CornerBoostMultiplier;
        }

        return Vector3.Zero;
    }

    private Vector3 GetInputDirection()
    {
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        return new Vector3(input[0], 0, input[1]).Rotated(Vector3.Up, GetParent<CharacterBody3D>().GlobalRotation[1]);
    }

    public bool IsWallSticking()
    {
        return isWallSticking;
    }

    public Vector3 GetCurrentWallNormal()
    {
        return currentWallNormal;
    }
}
