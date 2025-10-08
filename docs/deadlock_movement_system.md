# Deadlock-Style Movement System for Godot

## Overview

This document outlines the implementation of a 3rd person movement system inspired by Deadlock and Source 2 games. The system emphasizes fluid, skill-based movement with advanced techniques like bunnyhopping, dashing, sliding, and environmental interactions.

## Core Architecture

### Movement Controller Structure

```
CharacterBody3D (Root)
├── MovementComponent (handles locomotion)
├── StaminaComponent (manages stamina system)
├── DashComponent (handles dashing mechanics)
├── SlideComponent (handles sliding mechanics)
├── WallJumpComponent (handles wall interactions)
├── MantleComponent (handles mantling/climbing)
└── StatusEffectComponent (handles movement debuffs)
```

### Key Components

#### 1. MovementComponent
- **Purpose**: Core locomotion and physics integration
- **Responsibilities**:
  - Ground movement (walking/sprinting)
  - Jumping mechanics
  - Air strafing
  - Gravity and velocity management
  - Input handling and buffering

#### 2. StaminaComponent
- **Purpose**: Energy management for advanced movement
- **Mechanics**:
  - Sprint consumption
  - Dash cost
  - Regeneration rates
  - Visual feedback (UI integration)

#### 3. DashComponent
- **Purpose**: Burst movement mechanics
- **Features**:
  - Strafe dashing
  - Instant air dash
  - Dash jumping
  - Dash sliding
  - Parry dash

#### 4. SlideComponent
- **Purpose**: Sliding and momentum conservation
- **Mechanics**:
  - Crouch sliding
  - Slide bunnyhopping
  - Momentum transfer
  - Surface adaptation

#### 5. WallJumpComponent
- **Purpose**: Wall interaction mechanics
- **Features**:
  - Wall jumping
  - Wall sliding
  - Corner/edge boosts
  - Fast climbing

#### 6. MantleComponent
- **Purpose**: Environmental traversal
- **Mechanics**:
  - Mantling up ledges
  - Mantle gliding
  - Superglide
  - Glide wall jumps

## Universal Movement Techniques

### Core Movement

#### Walking/Sprinting
```csharp
// Implementation approach
public override void _PhysicsProcess(double delta)
{
    var inputDirection = GetInputDirection();
    var moveSpeed = isSprinting ? sprintSpeed : walkSpeed;

    if (IsOnFloor())
    {
        velocity = inputDirection * moveSpeed;
        // Apply friction when stopping
        if (inputDirection == Vector3.Zero)
        {
            velocity = velocity.Lerp(Vector3.Zero, friction * delta);
        }
    }
    else
    {
        // Air strafing with reduced control
        velocity.x = Mathf.Lerp(velocity.x, inputDirection.x * airControl, airControlRate * delta);
        velocity.z = Mathf.Lerp(velocity.z, inputDirection.z * airControl, airControlRate * delta);
    }

    velocity.y -= gravity * delta;
    Velocity = velocity;
    MoveAndSlide();
}
```

#### Stamina System
```csharp
public class StaminaComponent : Node
{
    [Export] public float maxStamina = 100f;
    [Export] public float sprintCostPerSecond = 10f;
    [Export] public float regenerationRate = 5f;

    private float currentStamina;

    public bool TryConsumeSprint(float delta)
    {
        if (currentStamina >= sprintCostPerSecond * delta)
        {
            currentStamina -= sprintCostPerSecond * delta;
            return true;
        }
        return false;
    }

    public void Regenerate(float delta)
    {
        currentStamina = Mathf.Min(maxStamina, currentStamina + regenerationRate * delta);
    }
}
```

#### Jumping
```csharp
public void HandleJump()
{
    if (IsOnFloor() && canJump)
    {
        velocity.y = jumpForce;
        CoyoteTimeReset(); // Allow for slight input timing forgiveness
    }
}
```

### Advanced Movement

#### Dashing
```csharp
public void PerformDash(Vector3 dashDirection)
{
    if (staminaComponent.CanDash())
    {
        // Strafe dashing - preserve some horizontal momentum
        var dashVelocity = dashDirection * dashSpeed;
        velocity = new Vector3(dashVelocity.x, 0, dashVelocity.z);

        // Apply dash effects (particles, screen effects, etc.)
        EmitDashEffects();
    }
}
```

#### Sliding
```csharp
public void StartSlide()
{
    if (IsOnFloor() && velocity.Length() > minSlideSpeed)
    {
        isSliding = true;
        slideStartVelocity = velocity.Length();
        // Reduce height, increase speed
        Scale = new Vector3(1, slideHeight, 1);
    }
}

public void UpdateSlide(double delta)
{
    if (isSliding)
    {
        // Maintain slide speed with slight deceleration
        var slideDirection = velocity.Normalized();
        var currentSlideSpeed = Mathf.Max(slideSpeed, velocity.Length() * slideMomentum);

        velocity = slideDirection * currentSlideSpeed;

        // Check slide conditions
        if (ShouldEndSlide())
        {
            EndSlide();
        }
    }
}
```

#### Bunnyhopping
```csharp
public void HandleBunnyhop()
{
    if (IsOnFloor() && Input.IsActionJustPressed("jump"))
    {
        // Gain speed from ground friction
        var groundAngle = GetFloorAngle();
        var speedGain = Mathf.Clamp(groundAngle, 0, maxBunnyhopGain);

        velocity += GetInputDirection() * speedGain;

        // Jump with current velocity
        velocity.y = jumpForce;
    }
}
```

#### Air Strafing
```csharp
public void HandleAirMovement(double delta)
{
    var inputDirection = GetInputDirection();

    // Apply air acceleration
    velocity.x = Mathf.Lerp(velocity.x, inputDirection.x * airSpeed, airAcceleration * delta);
    velocity.z = Mathf.Lerp(velocity.z, inputDirection.z * airSpeed, airAcceleration * delta);

    // Clamp maximum air speed
    var horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);
    if (horizontalVelocity.Length() > maxAirSpeed)
    {
        horizontalVelocity = horizontalVelocity.Normalized() * maxAirSpeed;
        velocity = new Vector3(horizontalVelocity.x, velocity.y, horizontalVelocity.z);
    }
}
```

### Environmental Interactions

#### Wall Jumping
```csharp
public void TryWallJump()
{
    var wallNormal = GetWallNormal();
    if (wallNormal != Vector3.Zero && !IsOnFloor())
    {
        // Calculate jump direction (away from wall + up)
        var jumpDirection = (wallNormal + Vector3.Up).Normalized();
        velocity = jumpDirection * wallJumpForce;

        // Add slight horizontal boost for corner boosts
        var cornerBoost = CalculateCornerBoost();
        velocity += cornerBoost;
    }
}
```

#### Mantling
```csharp
public async void TryMantle()
{
    var ledgeCheck = PerformLedgeCheck();
    if (ledgeCheck.canMantle)
    {
        // Animate character up and forward
        var tween = CreateTween();
        tween.TweenProperty(this, "position",
            position + ledgeCheck.mantleVector, mantleDuration);

        // Disable movement during mantle
        canMove = false;
        await ToSignal(tween, "finished");
        canMove = true;
    }
}
```

#### Ziplines
```csharp
public void AttachToZipline(Zipline zipline)
{
    currentZipline = zipline;
    velocity = Vector3.Zero; // Stop current movement
    GlobalPosition = zipline.GetClosestPoint(GlobalPosition);
}

public void UpdateZiplineMovement(double delta)
{
    if (currentZipline != null)
    {
        // Move along zipline path
        var targetPosition = currentZipline.GetNextPosition(position, delta);
        GlobalPosition = targetPosition;

        // Allow momentum conservation when jumping off
        if (Input.IsActionJustPressed("jump"))
        {
            var exitVelocity = currentZipline.GetExitVelocity();
            DetachFromZipline(exitVelocity);
        }
    }
}
```

## Status Effects System

### Movement Debuffs
```csharp
public class StatusEffectComponent : Node
{
    private Dictionary<string, StatusEffect> activeEffects = new();

    public void ApplyEffect(string effectName, float duration, float intensity)
    {
        var effect = new StatusEffect(effectName, duration, intensity);
        activeEffects[effectName] = effect;

        // Apply specific movement modifications
        switch (effectName)
        {
            case "MovementSlow":
                movementComponent.ApplySpeedMultiplier(1f - intensity);
                break;
            case "Stun":
                movementComponent.Stun(duration);
                break;
            case "Displace":
                movementComponent.ApplyDisplacement(effect.displacementVector);
                break;
        }
    }

    public void UpdateEffects(double delta)
    {
        foreach (var effect in activeEffects.Values.ToList())
        {
            effect.duration -= delta;
            if (effect.duration <= 0)
            {
                RemoveEffect(effect.name);
            }
        }
    }
}
```

## Input System

### Advanced Input Handling
```csharp
public class InputBuffer : Node
{
    private Dictionary<string, InputAction> bufferedInputs = new();

    public void BufferInput(string action, float bufferTime = 0.1f)
    {
        bufferedInputs[action] = new InputAction
        {
            actionName = action,
            bufferEndTime = Time.GetTime() + bufferTime
        };
    }

    public bool IsBuffered(string action)
    {
        if (bufferedInputs.TryGetValue(action, out var inputAction))
        {
            if (Time.GetTime() < inputAction.bufferEndTime)
            {
                bufferedInputs.Remove(action);
                return true;
            }
            else
            {
                bufferedInputs.Remove(action);
            }
        }
        return false;
    }
}
```

## Animation State Machine

### Movement States
```csharp
public enum MovementState
{
    Idle,
    Walking,
    Sprinting,
    Jumping,
    Falling,
    Sliding,
    Dashing,
    WallSliding,
    Mantling,
    Ziplining
}

public void UpdateAnimationState()
{
    var newState = CalculateCurrentState();

    if (currentState != newState)
    {
        ExitState(currentState);
        EnterState(newState);
        currentState = newState;
    }

    UpdateStateAnimation(currentState);
}
```

## Performance Considerations

### Optimization Techniques
```csharp
public class MovementOptimizer : Node
{
    private Queue<Vector3> velocityHistory = new();

    public void OptimizeMovement(double delta)
    {
        // Time-slice expensive operations
        if (frameCount % optimizationInterval == 0)
        {
            OptimizeCollisionChecks();
            UpdatePerformanceMetrics();
        }

        // Cache frequently accessed values
        var cachedIsOnFloor = IsOnFloor();

        // Pool reusable objects
        var movementVector = GetPooledVector();
        // ... use vector ...
        ReturnPooledVector(movementVector);
    }
}
```

## Integration Points

### Scene Setup
```csharp
// Character scene structure
CharacterBody3D
├── CollisionShape3D
├── MeshInstance3D (character model)
├── AnimationPlayer
├── Camera3D (3rd person camera)
├── MovementComponent
├── StaminaComponent
├── DashComponent
├── SlideComponent
├── WallJumpComponent
├── MantleComponent
├── StatusEffectComponent
└── InputBuffer
```

### Camera System
```csharp
public class ThirdPersonCamera : Camera3D
{
    [Export] public float distance = 5f;
    [Export] public float height = 2f;

    public override void _Process(double delta)
    {
        var targetPosition = character.GlobalPosition + Vector3.Up * height;
        var cameraPosition = targetPosition - GlobalTransform.basis.z * distance;

        GlobalPosition = cameraPosition;

        LookAt(targetPosition, Vector3.Up);
    }
}
```

## Next Steps

1. **Implementation Order**:
   - Core movement (walking, jumping)
   - Stamina system
   - Advanced movement (dashing, sliding)
   - Environmental interactions
   - Status effects
   - Polish and optimization

2. **Testing Requirements**:
   - Unit tests for each component
   - Integration tests for technique combinations
   - Performance benchmarks
   - Input responsiveness tests

3. **Balance Considerations**:
   - Speed values and acceleration curves
   - Stamina costs and regeneration rates
   - Cooldown timers
   - Interaction ranges and heights

This system provides a foundation for implementing Deadlock-style movement in Godot, focusing on the fluid, skill-based locomotion that defines the genre.
