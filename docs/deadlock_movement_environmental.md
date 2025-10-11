# Deadlock Movement System - Environmental Interactions Implementation

## Environmental Movement Components

This document details the implementation of environmental interaction mechanics that allow players to traverse complex 3D spaces using level geometry in Deadlock-style movement.

## 1. WallJumpComponent - Wall Interaction System

### Core Wall Jumping Mechanics

#### Class Structure
```csharp
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
            To = character.GlobalPosition - character.GlobalTransform.basis.z * WallDetectionDistance,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Exclude = new Godot.Collections.Array<Rid> { character.GetRid() }
        };

        var result = character.GetWorld3D().DirectSpaceState.IntersectRay(rayQuery);

        if (result.Count > 0)
        {
            var wallNormal = (Vector3)result["normal"];
            var dotProduct = character.GlobalTransform.basis.z.Dot(wallNormal);

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

        wallStickTimer -= delta;
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
            var horizontalBoost = new Vector3(inputDirection.x, 0, inputDirection.z).Normalized() * 0.3f;
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
            character.GlobalTransform.basis.x * 0.5f - character.GlobalTransform.basis.z,
            -character.GlobalTransform.basis.x * 0.5f - character.GlobalTransform.basis.z
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
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        return new Vector3(input.x, 0, input.y).Rotated(Vector3.Up, GetParent<CharacterBody3D>().GlobalRotation.y);
    }
}
```

### Wall Sliding
```csharp
public void UpdateWallSliding(double delta)
{
    if (!isWallSticking || GetParent<CharacterBody3D>().IsOnFloor()) return;

    var movementComponent = GetNode<MovementComponent>("../MovementComponent");
    var currentVelocity = movementComponent.GetVelocity();

    // Apply wall sliding physics
    var slideDirection = new Vector3(currentWallNormal.x, 0, currentWallNormal.z).Normalized();
    var slideVelocity = slideDirection * Mathf.Max(currentVelocity.Length() * 0.3f, 2f);

    // Preserve some vertical momentum
    slideVelocity.y = currentVelocity.y * 0.7f;

    // Allow slight downward control
    if (Input.IsActionPressed("crouch"))
    {
        slideVelocity.y -= 5f * delta;
    }

    movementComponent.SetVelocity(slideVelocity);
}
```

## 2. MantleComponent - Ledge Climbing System

### Mantling Mechanics

#### Class Structure
```csharp
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
            To = checkOrigin + character.GlobalTransform.basis.z * MantleDistance,
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
            var mantlePosition = ledgePosition + Vector3.Up * (MantleHeight * 0.3f) + character.GlobalTransform.basis.z * 0.5f;
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
            mantleCooldownTimer -= delta;
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
}
```

### Advanced Mantling Techniques

#### Mantle Gliding
```csharp
public void TryMantleGlide()
{
    if (isMantling && mantleTween != null && mantleTween.IsRunning())
    {
        // Extend mantle duration for gliding
        mantleTween.Kill();

        mantleTween = GetParent<CharacterBody3D>().CreateTween();
        mantleTween.TweenProperty(GetParent<CharacterBody3D>(), "global_position",
            GetParent<CharacterBody3D>().GlobalPosition + Vector3.Up * 1f, 0.3f);

        // Enable glide controls
        EnableGlideControls();
    }
}
```

#### Superglide
```csharp
public bool TrySuperglide(Vector3 inputDirection)
{
    // Superglide: mantle immediately after landing from height
    if (CanSuperglide())
    {
        var mantleCheck = PerformMantleCheck();
        if (mantleCheck.canMantle)
        {
            // Apply extra speed and distance
            var superMantlePosition = mantleCheck.mantlePosition + inputDirection * 2f;
            StartMantle(superMantlePosition);
            return true;
        }
    }
    return false;
}

private bool CanSuperglide()
{
    var character = GetParent<CharacterBody3D>();
    var movementComponent = GetNode<MovementComponent>("../MovementComponent");

    // Check if we just landed with high downward velocity
    return character.IsOnFloor() &&
           movementComponent.GetVelocity().y < -15f &&
           Time.GetTime() - lastLandTime < 0.1f;
}
```

## 3. ZiplineComponent - Transit Line System

### Zipline Mechanics

#### Class Structure
```csharp
public partial class ZiplineComponent : Node
{
    [Export] public float ZiplineSpeed { get; set; } = 15f;
    [Export] public float ZiplineAcceleration { get; set; } = 20f;
    [Export] public float ZiplineFriction { get; set; } = 0.95f;
    [Export] public float AttachDistance { get; set; } = 2f;

    [Signal] public delegate void ZiplineAttachedEventHandler(Zipline zipline);
    [Signal] public delegate void ZiplineDetachedEventHandler(Vector3 exitVelocity);

    private Zipline currentZipline;
    private float ziplineProgress = 0f;
    private Vector3 ziplineVelocity = Vector3.Zero;

    public override void _PhysicsProcess(double delta)
    {
        if (currentZipline != null)
        {
            UpdateZiplineMovement(delta);
        }
        else
        {
            CheckForZipline();
        }
    }

    public bool TryAttachToZipline(Zipline zipline)
    {
        if (currentZipline != null) return false;

        var closestPoint = zipline.GetClosestPointOnLine(GetParent<CharacterBody3D>().GlobalPosition);
        if (closestPoint.DistanceTo(GetParent<CharacterBody3D>().GlobalPosition) <= AttachDistance)
        {
            AttachToZipline(zipline, closestPoint);
            return true;
        }

        return false;
    }

    private void AttachToZipline(Zipline zipline, Vector3 attachPoint)
    {
        currentZipline = zipline;
        ziplineProgress = zipline.GetProgressFromPoint(attachPoint);
        ziplineVelocity = Vector3.Zero;

        var character = GetParent<CharacterBody3D>();
        character.GlobalPosition = attachPoint;

        // Stop current movement
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent.SetVelocity(Vector3.Zero);

        EmitSignal(SignalName.ZiplineAttached, zipline);
    }

    private void UpdateZiplineMovement(double delta)
    {
        if (currentZipline == null) return;

        var character = GetParent<CharacterBody3D>();

        // Calculate target position on zipline
        ziplineProgress += ziplineVelocity.Length() * delta / currentZipline.Length;

        // Clamp progress to zipline bounds
        ziplineProgress = Mathf.Clamp(ziplineProgress, 0f, 1f);

        var targetPosition = currentZipline.GetPointFromProgress(ziplineProgress);

        // Apply zipline physics
        var directionToTarget = (targetPosition - character.GlobalPosition).Normalized();
        var distance = targetPosition.DistanceTo(character.GlobalPosition);

        // Accelerate toward zipline
        ziplineVelocity += directionToTarget * ZiplineAcceleration * delta;
        ziplineVelocity *= ZiplineFriction; // Apply friction

        // Limit maximum speed
        if (ziplineVelocity.Length() > ZiplineSpeed)
        {
            ziplineVelocity = ziplineVelocity.Normalized() * ZiplineSpeed;
        }

        // Move character
        character.GlobalPosition += ziplineVelocity * delta;

        // Handle input for direction changes
        var inputDirection = GetInputDirection();
        if (inputDirection.z > 0.5f) // Forward
        {
            ziplineVelocity += character.GlobalTransform.basis.z * ZiplineAcceleration * delta;
        }
        else if (inputDirection.z < -0.5f) // Backward
        {
            ziplineVelocity -= character.GlobalTransform.basis.z * ZiplineAcceleration * delta;
        }

        // Check for detach
        if (Input.IsActionJustPressed("jump") || ziplineProgress >= 1f || ziplineProgress <= 0f)
        {
            DetachFromZipline();
        }
    }

    private void DetachFromZipline()
    {
        if (currentZipline == null) return;

        var exitVelocity = ziplineVelocity;

        // Add momentum conservation
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        var preservedVelocity = movementComponent.GetVelocity() * 0.5f;
        exitVelocity += preservedVelocity;

        currentZipline = null;
        ziplineProgress = 0f;

        movementComponent.SetVelocity(exitVelocity);
        EmitSignal(SignalName.ZiplineDetached, exitVelocity);
    }

    private void CheckForZipline()
    {
        // Raycast to find nearby ziplines
        var character = GetParent<CharacterBody3D>();
        var forwardCheck = new PhysicsRayQueryParameters3D
        {
            From = character.GlobalPosition + Vector3.Up,
            To = character.GlobalPosition + Vector3.Up + character.GlobalTransform.basis.z * AttachDistance,
            CollideWithAreas = true,
            CollideWithBodies = false
        };

        var results = character.GetWorld3D().DirectSpaceState.IntersectRay(forwardCheck);
        if (results.Count > 0)
        {
            var collider = (Node3D)results["collider"];
            if (collider is Zipline zipline)
            {
                TryAttachToZipline(zipline);
            }
        }
    }

    private Vector3 GetInputDirection()
    {
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        return new Vector3(input.x, 0, input.y).Rotated(Vector3.Up, GetParent<CharacterBody3D>().GlobalRotation.y);
    }
}
```

### Zipline Momentum Conservation
```csharp
private Vector3 CalculateZiplineExitVelocity()
{
    // Preserve zipline momentum when jumping off
    var baseVelocity = ziplineVelocity;

    // Add player's input momentum
    var inputDirection = GetInputDirection();
    if (inputDirection != Vector3.Zero)
    {
        baseVelocity += inputDirection * 5f;
    }

    // Add slight upward boost for jump
    if (Input.IsActionPressed("jump"))
    {
        baseVelocity.y += 8f;
    }

    return baseVelocity;
}
```

## 4. RopeComponent - Rope Swinging System

### Rope Mechanics

#### Class Structure
```csharp
public partial class RopeComponent : Node
{
    [Export] public float RopeLength { get; set; } = 10f;
    [Export] public float RopeSwingForce { get; set; } = 5f;
    [Export] public float RopeGravity { get; set; } = 15f;

    private Rope currentRope;
    private Vector3 ropeAttachPoint = Vector3.Zero;
    private float ropeAngle = 0f;
    private float ropeAngularVelocity = 0f;

    public override void _PhysicsProcess(double delta)
    {
        if (currentRope != null)
        {
            UpdateRopeSwing(delta);
        }
        else
        {
            CheckForRope();
        }
    }

    public bool TryAttachToRope(Rope rope)
    {
        if (currentRope != null) return false;

        ropeAttachPoint = rope.GlobalPosition;
        currentRope = rope;

        var character = GetParent<CharacterBody3D>();
        var distance = ropeAttachPoint.DistanceTo(character.GlobalPosition);

        if (distance <= RopeLength)
        {
            // Initialize rope swing
            ropeAngle = Mathf.Atan2(character.GlobalPosition.x - ropeAttachPoint.x,
                                   character.GlobalPosition.z - ropeAttachPoint.z);
            ropeAngularVelocity = 0f;

            return true;
        }

        return false;
    }

    private void UpdateRopeSwing(double delta)
    {
        var character = GetParent<CharacterBody3D>();

        // Calculate rope constraint
        var toPlayer = character.GlobalPosition - ropeAttachPoint;
        var distance = toPlayer.Length();

        if (distance > RopeLength)
        {
            // Pull player back to rope length
            var direction = toPlayer.Normalized();
            character.GlobalPosition = ropeAttachPoint + direction * RopeLength;
            ropeAngularVelocity *= 0.8f; // Damping
        }

        // Apply swing physics
        var inputDirection = GetInputDirection();
        var swingForce = inputDirection.x * RopeSwingForce;

        ropeAngularVelocity += swingForce * delta;
        ropeAngularVelocity *= 0.98f; // Air resistance

        // Update position based on angle
        var newX = ropeAttachPoint.x + Mathf.Sin(ropeAngle) * RopeLength;
        var newZ = ropeAttachPoint.z + Mathf.Cos(ropeAngle) * RopeLength;
        var newY = Mathf.Max(character.GlobalPosition.y - RopeGravity * delta, ropeAttachPoint.y);

        character.GlobalPosition = new Vector3(newX, newY, newZ);

        // Handle detach
        if (Input.IsActionJustPressed("jump") || Input.IsActionJustPressed("detach_rope"))
        {
            DetachFromRope();
        }
    }

    private void DetachFromRope()
    {
        if (currentRope == null) return;

        // Calculate exit velocity based on swing momentum
        var exitVelocity = new Vector3(
            Mathf.Cos(ropeAngle) * ropeAngularVelocity * 10f,
            0f,
            -Mathf.Sin(ropeAngle) * ropeAngularVelocity * 10f
        );

        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent.SetVelocity(exitVelocity);

        currentRope = null;
    }

    private void CheckForRope()
    {
        // Similar to zipline detection but for ropes
        var character = GetParent<CharacterBody3D>();
        var checkRays = new[]
        {
            character.GlobalTransform.basis.z,
            character.GlobalTransform.basis.z + character.GlobalTransform.basis.x * 0.5f,
            character.GlobalTransform.basis.z - character.GlobalTransform.basis.x * 0.5f
        };

        foreach (var rayDirection in checkRays)
        {
            var ropeQuery = new PhysicsRayQueryParameters3D
            {
                From = character.GlobalPosition,
                To = character.GlobalPosition + rayDirection * 3f,
                CollideWithAreas = true,
                CollideWithBodies = false
            };

            var results = character.GetWorld3D().DirectSpaceState.IntersectRay(ropeQuery);
            if (results.Count > 0)
            {
                var collider = (Node3D)results["collider"];
                if (collider is Rope rope)
                {
                    TryAttachToRope(rope);
                    break;
                }
            }
        }
    }

    private Vector3 GetInputDirection()
    {
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        return new Vector3(input.x, 0, input.y).Rotated(Vector3.Up, GetParent<CharacterBody3D>().GlobalRotation.y);
    }
}
```

## 5. VentComponent - Vertical Transit System

### Vent Mechanics

#### Class Structure
```csharp
public partial class VentComponent : Node
{
    [Export] public float VentForce { get; set; } = 20f;
    [Export] public float VentExitForce { get; set; } = 10f;
    [Export] public float VentDetectionHeight { get; set; } = 3f;

    private Vent currentVent;
    private bool isInVent = false;

    public override void _PhysicsProcess(double delta)
    {
        if (!isInVent)
        {
            CheckForVent();
        }
        else
        {
            UpdateVentMovement(delta);
        }
    }

    private void CheckForVent()
    {
        var character = GetParent<CharacterBody3D>();

        // Check downward for vents
        var ventQuery = new PhysicsRayQueryParameters3D
        {
            From = character.GlobalPosition + Vector3.Up * 0.5f,
            To = character.GlobalPosition - Vector3.Up * VentDetectionHeight,
            CollideWithAreas = true,
            CollideWithBodies = false
        };

        var results = character.GetWorld3D().DirectSpaceState.IntersectRay(ventQuery);
        if (results.Count > 0)
        {
            var collider = (Node3D)results["collider"];
            if (collider is Vent vent)
            {
                EnterVent(vent);
            }
        }
    }

    private void EnterVent(Vent vent)
    {
        currentVent = vent;
        isInVent = true;

        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent.SetMovementEnabled(false);

        // Position player in vent
        var character = GetParent<CharacterBody3D>();
        character.GlobalPosition = vent.GetEntryPosition();
    }

    private void UpdateVentMovement(double delta)
    {
        if (currentVent == null || !isInVent) return;

        var character = GetParent<CharacterBody3D>();

        // Apply vent force
        var ventDirection = currentVent.GetVentDirection();
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent.SetVelocity(ventDirection * VentForce);

        // Check for exit
        if (currentVent.ShouldExit(character.GlobalPosition) || Input.IsActionJustPressed("jump"))
        {
            ExitVent();
        }
    }

    private void ExitVent()
    {
        if (currentVent == null) return;

        var exitVelocity = currentVent.GetExitDirection() * VentExitForce;

        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent.SetVelocity(exitVelocity);
        movementComponent.SetMovementEnabled(true);

        currentVent = null;
        isInVent = false;
    }
}
```

## 6. Integration and Scene Setup

### Environmental Object Classes
```csharp
public partial class Zipline : Area3D
{
    [Export] public Vector3 StartPoint { get; set; }
    [Export] public Vector3 EndPoint { get; set; }

    public float Length => StartPoint.DistanceTo(EndPoint);

    public Vector3 GetClosestPointOnLine(Vector3 point)
    {
        var lineDirection = (EndPoint - StartPoint).Normalized();
        var pointVector = point - StartPoint;
        var projection = pointVector.Dot(lineDirection);

        projection = Mathf.Clamp(projection, 0f, Length);
        return StartPoint + lineDirection * projection;
    }

    public float GetProgressFromPoint(Vector3 point)
    {
        var closestPoint = GetClosestPointOnLine(point);
        return closestPoint.DistanceTo(StartPoint) / Length;
    }

    public Vector3 GetPointFromProgress(float progress)
    {
        return StartPoint + (EndPoint - StartPoint) * progress;
    }
}

public partial class Rope : Node3D
{
    [Export] public Vector3 AnchorPoint { get; set; }
    [Export] public float Length { get; set; } = 10f;

    public Vector3 GetRopeDirection(Vector3 playerPosition)
    {
        return (playerPosition - AnchorPoint).Normalized();
    }
}
```

## 7. Configuration Values

### Recommended Starting Values
```csharp
// WallJumpComponent
WallJumpForce = 12f;
WallJumpAngle = 45f;
WallStickTime = 0.2f;
WallDetectionDistance = 1.5f;
CornerBoostMultiplier = 1.3f;

// MantleComponent
MantleHeight = 2f;
MantleDistance = 1f;
MantleDuration = 0.5f;
MantleCooldown = 0.3f;

// ZiplineComponent
ZiplineSpeed = 15f;
ZiplineAcceleration = 20f;
ZiplineFriction = 0.95f;
AttachDistance = 2f;

// RopeComponent
RopeLength = 10f;
RopeSwingForce = 5f;
RopeGravity = 15f;

// VentComponent
VentForce = 20f;
VentExitForce = 10f;
VentDetectionHeight = 3f;
```

## 8. Testing Checklist

- [ ] Wall jumping launches player away from walls at correct angle
- [ ] Corner boosts provide extra speed from wall corners
- [ ] Mantling allows traversal of waist-high obstacles
- [ ] Ziplines provide fast horizontal transit with momentum conservation
- [ ] Ropes allow swinging with physics-based momentum
- [ ] Vents provide vertical transportation
- [ ] All environmental interactions preserve player control and intent
- [ ] Visual feedback clearly indicates interactive elements

## 9. Level Design Considerations

### Environmental Layout
- **Wall Jump Surfaces**: Clearly marked with visual indicators
- **Mantle Objects**: Consistent height and depth for predictable traversal
- **Zipline Placement**: Strategic positioning for speed routes and shortcuts
- **Rope Locations**: Swing paths that reward skillful timing
- **Vent Systems**: Vertical connections between level sections

This environmental interaction system transforms static level geometry into dynamic movement opportunities, creating the interconnected, skill-based traversal that defines Deadlock-style gameplay.

