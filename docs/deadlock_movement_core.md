# Deadlock Movement System - Core Mechanics Implementation

## Core Movement Components

This document provides detailed implementation patterns for the fundamental movement mechanics that form the foundation of Deadlock-style movement.

## 1. MovementComponent - Core Locomotion

### Class Structure
```csharp
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
    private float coyoteTime = 0.1f;
    private float coyoteTimer = 0f;

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
    }

    public void SetMovementEnabled(bool enabled) => canMove = enabled;
    public Vector3 GetVelocity() => velocity;
    public void SetVelocity(Vector3 newVelocity) => velocity = newVelocity;
}
```

### Ground Movement Implementation
```csharp
private void HandleGroundMovement(double delta)
{
    if (!character.IsOnFloor()) return;

    var inputDirection = GetInputDirection();
    var targetSpeed = Input.IsActionPressed("sprint") ? SprintSpeed : WalkSpeed;

    // Smooth acceleration to target speed
    var currentSpeed = new Vector3(velocity.x, 0, velocity.z).Length();
    var speedDifference = targetSpeed - currentSpeed;

    if (speedDifference > 0)
    {
        // Accelerate towards target speed
        var accelerationAmount = Mathf.Min(speedDifference, Acceleration * delta);
        velocity += inputDirection * accelerationAmount;
    }
    else
    {
        // Apply friction when slowing down or stopping
        velocity = velocity.Lerp(Vector3.Zero, Friction * delta);
    }

    // Orient movement direction
    if (inputDirection != Vector3.Zero)
    {
        var targetDirection = inputDirection.Normalized();
        velocity = velocity.Normalized() * Mathf.Clamp(velocity.Length(), 0, targetSpeed);
        velocity = velocity.Slide(targetDirection).Normalized() * velocity.Length();
    }
}

private Vector3 GetInputDirection()
{
    var input = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
    return new Vector3(input.x, 0, input.y).Rotated(Vector3.Up, character.GlobalRotation.y);
}
```

### Air Movement Implementation
```csharp
private void HandleAirMovement(double delta)
{
    if (character.IsOnFloor()) return;

    var inputDirection = GetInputDirection();

    // Apply air strafing
    velocity.x = Mathf.Lerp(velocity.x, inputDirection.x * WalkSpeed * AirControl, AirAcceleration * delta);
    velocity.z = Mathf.Lerp(velocity.z, inputDirection.z * WalkSpeed * AirControl, AirAcceleration * delta);

    // Clamp maximum air speed
    var horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);
    var maxAirSpeed = WalkSpeed * 1.2f; // Slightly faster than ground speed

    if (horizontalVelocity.Length() > maxAirSpeed)
    {
        horizontalVelocity = horizontalVelocity.Normalized() * maxAirSpeed;
        velocity = new Vector3(horizontalVelocity.x, velocity.y, horizontalVelocity.z);
    }
}

private void ApplyGravity(double delta)
{
    if (!character.IsOnFloor())
    {
        velocity.y -= Gravity * delta;
    }
    else
    {
        velocity.y = 0; // Reset vertical velocity when grounded
    }
}

private void UpdateVelocity()
{
    character.Velocity = velocity;
    character.MoveAndSlide();
    velocity = character.Velocity; // Update with actual velocity after MoveAndSlide
}
```

### Jump Mechanics
```csharp
public void HandleJump()
{
    if (CanJump())
    {
        velocity.y = JumpForce;
        coyoteTimer = 0f; // Reset coyote time on jump
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
        coyoteTimer -= delta;
    }
}
```

## 2. StaminaComponent - Energy Management

### Class Structure
```csharp
public partial class StaminaComponent : Node
{
    [Export] public float MaxStamina { get; set; } = 100f;
    [Export] public float SprintCostPerSecond { get; set; } = 15f;
    [Export] public float RegenerationRate { get; set; } = 8f;
    [Export] public float RegenerationDelay { get; set; } = 2f;

    [Signal] public delegate void StaminaChangedEventHandler(float currentStamina, float maxStamina);
    [Signal] public delegate void StaminaDepletedEventHandler();
    [Signal] public delegate void StaminaRegeneratingEventHandler();

    private float currentStamina;
    private float regenerationTimer = 0f;
    private bool isRegenerating = false;

    public override void _Ready()
    {
        currentStamina = MaxStamina;
        EmitSignal(SignalName.StaminaChanged, currentStamina, MaxStamina);
    }

    public override void _Process(double delta)
    {
        UpdateStaminaRegeneration(delta);
    }

    public bool TryConsumeSprint(float delta)
    {
        var cost = SprintCostPerSecond * delta;
        if (currentStamina >= cost)
        {
            currentStamina -= cost;
            regenerationTimer = RegenerationDelay;
            isRegenerating = false;
            EmitSignal(SignalName.StaminaChanged, currentStamina, MaxStamina);
            return true;
        }
        return false;
    }

    public bool CanSprint()
    {
        return currentStamina > SprintCostPerSecond * 0.1f; // Minimum threshold
    }

    public float GetStaminaPercentage()
    {
        return currentStamina / MaxStamina;
    }
}
```

### Stamina Regeneration
```csharp
private void UpdateStaminaRegeneration(double delta)
{
    if (currentStamina >= MaxStamina)
    {
        isRegenerating = false;
        return;
    }

    if (regenerationTimer > 0)
    {
        regenerationTimer -= delta;
        if (!isRegenerating)
        {
            isRegenerating = true;
            EmitSignal(SignalName.StaminaRegenerating);
        }
        return;
    }

    // Regenerate stamina
    var oldStamina = currentStamina;
    currentStamina = Mathf.Min(MaxStamina, currentStamina + RegenerationRate * delta);

    if (Mathf.Abs(currentStamina - oldStamina) > 0.01f)
    {
        EmitSignal(SignalName.StaminaChanged, currentStamina, MaxStamina);
    }
}
```

## 3. CrouchComponent - Stance Management

### Class Structure
```csharp
public partial class CrouchComponent : Node
{
    [Export] public float CrouchSpeed { get; set; } = 4f;
    [Export] public float CrouchHeight { get; set; } = 1f;
    [Export] public float StandingHeight { get; set; } = 2f;
    [Export] public float CrouchTransitionTime { get; set; } = 0.2f;

    private CollisionShape3D collisionShape;
    private MeshInstance3D mesh;
    private bool isCrouching = false;
    private bool canUncrouch = true;
    private Tween crouchTween;

    public override void _Ready()
    {
        collisionShape = GetParent<CharacterBody3D>().GetNode<CollisionShape3D>("CollisionShape3D");
        mesh = GetParent<CharacterBody3D>().GetNode<MeshInstance3D>("MeshInstance3D");
    }

    public void HandleCrouch()
    {
        if (Input.IsActionJustPressed("crouch"))
        {
            if (isCrouching)
            {
                TryUncrouch();
            }
            else
            {
                StartCrouch();
            }
        }
    }

    private void StartCrouch()
    {
        if (isCrouching) return;

        isCrouching = true;
        AnimateCrouch(CrouchHeight);

        // Check if we can crouch in current position
        if (!CheckCrouchSpace())
        {
            // Cancel crouch if obstructed
            isCrouching = false;
            AnimateCrouch(StandingHeight);
            return;
        }

        EmitSignal(SignalName.CrouchStarted);
    }

    private void TryUncrouch()
    {
        if (!isCrouching || !canUncrouch) return;

        if (CheckUncrouchSpace())
        {
            isCrouching = false;
            AnimateCrouch(StandingHeight);
            EmitSignal(SignalName.CrouchEnded);
        }
    }

    private bool CheckCrouchSpace()
    {
        // Cast ray upward to check for overhead clearance
        var spaceCheck = new PhysicsRayQueryParameters3D
        {
            From = character.GlobalPosition + Vector3.Up * StandingHeight * 0.5f,
            To = character.GlobalPosition + Vector3.Up * CrouchHeight * 0.5f,
            CollideWithAreas = true,
            CollideWithBodies = true
        };

        var result = character.GetWorld3D().DirectSpaceState.IntersectRay(spaceCheck);
        return result.Count == 0; // No collision means space is clear
    }

    private bool CheckUncrouchSpace()
    {
        // Cast ray upward from crouch height to standing height
        var spaceCheck = new PhysicsRayQueryParameters3D
        {
            From = character.GlobalPosition + Vector3.Up * CrouchHeight * 0.5f,
            To = character.GlobalPosition + Vector3.Up * StandingHeight * 0.5f,
            CollideWithAreas = true,
            CollideWithBodies = true
        };

        var result = character.GetWorld3D().DirectSpaceState.IntersectRay(spaceCheck);
        return result.Count == 0;
    }

    private void AnimateCrouch(float targetHeight)
    {
        if (crouchTween != null && crouchTween.IsRunning())
        {
            crouchTween.Kill();
        }

        crouchTween = GetParent<CharacterBody3D>().CreateTween();
        crouchTween.TweenProperty(collisionShape, "scale:y", targetHeight / StandingHeight, CrouchTransitionTime);
        crouchTween.TweenProperty(mesh, "scale:y", targetHeight / StandingHeight, CrouchTransitionTime);
    }
}
```

## 4. Movement State Machine

### State Management
```csharp
public enum MovementState
{
    Idle,
    Walking,
    Sprinting,
    Crouching,
    Jumping,
    Falling,
    Sliding,
    Dashing
}

public partial class MovementStateMachine : Node
{
    private MovementState currentState = MovementState.Idle;
    private Dictionary<MovementState, Action<double>> stateActions = new();

    public override void _Ready()
    {
        InitializeStateActions();
    }

    public override void _PhysicsProcess(double delta)
    {
        var newState = CalculateCurrentState();
        TransitionToState(newState, delta);
    }

    private void InitializeStateActions()
    {
        stateActions[MovementState.Idle] = HandleIdleState;
        stateActions[MovementState.Walking] = HandleWalkingState;
        stateActions[MovementState.Sprinting] = HandleSprintingState;
        stateActions[MovementState.Crouching] = HandleCrouchingState;
        stateActions[MovementState.Jumping] = HandleJumpingState;
        stateActions[MovementState.Falling] = HandleFallingState;
    }

    private MovementState CalculateCurrentState()
    {
        var character = GetParent<CharacterBody3D>();
        var movement = GetNode<MovementComponent>("../MovementComponent");
        var crouch = GetNode<CrouchComponent>("../CrouchComponent");

        // Priority order for state determination
        if (!character.IsOnFloor())
        {
            return velocity.y > 0 ? MovementState.Jumping : MovementState.Falling;
        }

        if (crouch.IsCrouching)
        {
            return MovementState.Crouching;
        }

        var inputDirection = movement.GetInputDirection();
        var speed = velocity.Length();

        if (inputDirection == Vector3.Zero)
        {
            return MovementState.Idle;
        }

        if (Input.IsActionPressed("sprint") && movement.CanSprint())
        {
            return MovementState.Sprinting;
        }

        return MovementState.Walking;
    }

    private void TransitionToState(MovementState newState, double delta)
    {
        if (currentState == newState) return;

        ExitState(currentState);
        currentState = newState;
        EnterState(newState);

        stateActions[newState]?.Invoke(delta);
    }

    private void EnterState(MovementState state)
    {
        switch (state)
        {
            case MovementState.Sprinting:
                // Increase FOV, add speed lines effect
                break;
            case MovementState.Crouching:
                // Lower camera, reduce noise
                break;
        }
    }

    private void ExitState(MovementState state)
    {
        switch (state)
        {
            case MovementState.Sprinting:
                // Reset FOV, remove speed effects
                break;
            case MovementState.Crouching:
                // Reset camera height
                break;
        }
    }
}
```

## 5. Input Buffer System

### Advanced Input Handling
```csharp
public partial class InputBuffer : Node
{
    private Dictionary<string, BufferedInput> bufferedInputs = new();
    private const float DefaultBufferTime = 0.15f;

    private struct BufferedInput
    {
        public string ActionName;
        public double BufferEndTime;
        public bool WasPressed;
    }

    public override void _Process(double delta)
    {
        // Clean up expired buffers
        var expiredKeys = bufferedInputs
            .Where(kvp => Time.GetTime() > kvp.Value.BufferEndTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            bufferedInputs.Remove(key);
        }
    }

    public void BufferInput(string action)
    {
        bufferedInputs[action] = new BufferedInput
        {
            ActionName = action,
            BufferEndTime = Time.GetTime() + DefaultBufferTime,
            WasPressed = true
        };
    }

    public bool IsBuffered(string action)
    {
        if (bufferedInputs.TryGetValue(action, out var bufferedInput))
        {
            if (Time.GetTime() < bufferedInput.BufferEndTime)
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

    public bool ConsumeBufferedInput(string action)
    {
        if (IsBuffered(action))
        {
            return true;
        }
        return false;
    }
}
```

## Integration Example

### Character Controller Scene Structure
```csharp
// CharacterBody3D scene setup
CharacterBody3D (Root Node)
├── CollisionShape3D
├── MeshInstance3D
├── AnimationPlayer
├── Camera3D
├── MovementComponent
├── StaminaComponent
├── CrouchComponent
├── MovementStateMachine
└── InputBuffer
```

### Main Character Script
```csharp
public partial class CharacterController : CharacterBody3D
{
    private MovementComponent movementComponent;
    private StaminaComponent staminaComponent;
    private CrouchComponent crouchComponent;
    private InputBuffer inputBuffer;

    public override void _Ready()
    {
        movementComponent = GetNode<MovementComponent>("MovementComponent");
        staminaComponent = GetNode<StaminaComponent>("StaminaComponent");
        crouchComponent = GetNode<CrouchComponent>("CrouchComponent");
        inputBuffer = GetNode<InputBuffer>("InputBuffer");
    }

    public override void _PhysicsProcess(double delta)
    {
        // Handle input buffering
        if (Input.IsActionJustPressed("jump"))
        {
            inputBuffer.BufferInput("jump");
        }

        // Handle movement inputs
        if (Input.IsActionJustPressed("crouch"))
        {
            crouchComponent.HandleCrouch();
        }

        // Handle jump with buffer
        if (inputBuffer.IsBuffered("jump"))
        {
            movementComponent.HandleJump();
        }

        // Handle sprint stamina consumption
        if (Input.IsActionPressed("sprint") && IsOnFloor())
        {
            if (!staminaComponent.TryConsumeSprint(delta))
            {
                // Force walk speed when stamina depleted
                movementComponent.SetSprintEnabled(false);
            }
            else
            {
                movementComponent.SetSprintEnabled(true);
            }
        }
        else
        {
            movementComponent.SetSprintEnabled(false);
        }
    }
}
```

## Configuration Values

### Recommended Starting Values
```csharp
// MovementComponent
WalkSpeed = 7f;
SprintSpeed = 10f;
Acceleration = 15f;
Friction = 8f;
AirControl = 0.3f;
AirAcceleration = 5f;
Gravity = 20f;
JumpForce = 8f;

// StaminaComponent
MaxStamina = 100f;
SprintCostPerSecond = 15f;
RegenerationRate = 8f;
RegenerationDelay = 2f;

// CrouchComponent
CrouchSpeed = 4f;
CrouchHeight = 1f;
StandingHeight = 2f;
CrouchTransitionTime = 0.2f;
```

## Testing Checklist

- [ ] Basic movement (walk, sprint) feels responsive
- [ ] Jumping provides good air time and control
- [ ] Crouching transitions smoothly and allows access to low spaces
- [ ] Stamina regenerates appropriately and limits sprint duration
- [ ] Air strafing allows directional control while airborne
- [ ] Coyote time allows for forgiving jump timing
- [ ] Input buffering prevents missed inputs during state transitions

## Next Steps

With core movement implemented, proceed to:
1. **Advanced Movement** - Dashing, sliding, bunnyhopping
2. **Environmental Interactions** - Wall jumping, mantling, ziplines
3. **Status Effects** - Movement debuffs and buffs
4. **Polish** - Camera system, animations, visual effects

This core system provides the foundation for all advanced movement techniques while maintaining the responsive, skill-based feel of Deadlock-style gameplay.

