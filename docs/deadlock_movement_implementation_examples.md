# Deadlock Movement System - Implementation Examples

## Complete Implementation Scripts

This document provides complete, working Godot scripts that demonstrate how to implement the Deadlock-style movement system in practice.

## 1. Complete Character Controller

### CharacterBody3D Script
```csharp
using Godot;
using System;

public partial class CharacterController : CharacterBody3D
{
    // Core movement components
    private MovementComponent movementComponent;
    private StaminaComponent staminaComponent;
    private CrouchComponent crouchComponent;
    private InputBuffer inputBuffer;

    // Advanced movement components
    private DashComponent dashComponent;
    private SlideComponent slideComponent;
    private BunnyhopComponent bunnyhopComponent;
    private WallJumpComponent wallJumpComponent;
    private MantleComponent mantleComponent;

    // Character-specific components
    private HeavyMeleeComponent heavyMeleeComponent;
    private ParryComponent parryComponent;
    private StatusEffectComponent statusEffectComponent;

    // Camera and effects
    private ThirdPersonCamera camera;
    private AnimationPlayer animationPlayer;

    // Configuration
    [Export] public float MouseSensitivity { get; set; } = 0.3f;
    [Export] public bool LockMouse { get; set; } = true;

    public override void _Ready()
    {
        // Get component references
        movementComponent = GetNode<MovementComponent>("MovementComponent");
        staminaComponent = GetNode<StaminaComponent>("StaminaComponent");
        crouchComponent = GetNode<CrouchComponent>("CrouchComponent");
        inputBuffer = GetNode<InputBuffer>("InputBuffer");

        dashComponent = GetNode<DashComponent>("DashComponent");
        slideComponent = GetNode<SlideComponent>("SlideComponent");
        bunnyhopComponent = GetNode<BunnyhopComponent>("BunnyhopComponent");
        wallJumpComponent = GetNode<WallJumpComponent>("WallJumpComponent");
        mantleComponent = GetNode<MantleComponent>("MantleComponent");

        heavyMeleeComponent = GetNode<HeavyMeleeComponent>("HeavyMeleeComponent");
        parryComponent = GetNode<ParryComponent>("ParryComponent");
        statusEffectComponent = GetNode<StatusEffectComponent>("StatusEffectComponent");

        camera = GetNode<ThirdPersonCamera>("Camera3D");
        animationPlayer = GetNode<AnimationPlayer>("AnimationPlayer");

        // Setup input and mouse
        if (LockMouse)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        // Connect signals
        ConnectComponentSignals();

        GD.Print("CharacterController initialized with Deadlock movement system");
    }

    private void ConnectComponentSignals()
    {
        // Connect stamina signals
        staminaComponent.StaminaChanged += OnStaminaChanged;
        staminaComponent.StaminaDepleted += OnStaminaDepleted;

        // Connect dash signals
        dashComponent.DashStarted += OnDashStarted;
        dashComponent.DashEnded += OnDashEnded;

        // Connect slide signals
        slideComponent.SlideStarted += OnSlideStarted;
        slideComponent.SlideEnded += OnSlideEnded;

        // Connect wall jump signals
        wallJumpComponent.WallJumped += OnWallJumped;

        // Connect mantle signals
        mantleComponent.MantleStarted += OnMantleStarted;
        mantleComponent.MantleCompleted += OnMantleCompleted;

        // Connect melee signals
        heavyMeleeComponent.MeleeStarted += OnMeleeStarted;
        heavyMeleeComponent.MeleeHit += OnMeleeHit;
        heavyMeleeComponent.MeleeFinished += OnMeleeFinished;

        // Connect parry signals
        parryComponent.ParryStarted += OnParryStarted;
        parryComponent.ParrySuccess += OnParrySuccess;
        parryComponent.ParryFailed += OnParryFailed;
    }

    public override void _PhysicsProcess(double delta)
    {
        // Handle input buffering
        HandleInputBuffering();

        // Handle movement inputs
        HandleMovementInputs();

        // Handle ability inputs
        HandleAbilityInputs();

        // Update camera
        UpdateCamera();

        // Update animations
        UpdateAnimations();
    }

    private void HandleInputBuffering()
    {
        if (Input.IsActionJustPressed("jump"))
        {
            inputBuffer.BufferInput("jump");
        }
    }

    private void HandleMovementInputs()
    {
        // Handle crouch
        if (Input.IsActionJustPressed("crouch"))
        {
            crouchComponent.HandleCrouch();
        }

        // Handle jump with buffer
        if (inputBuffer.IsBuffered("jump"))
        {
            if (wallJumpComponent.TryWallJump())
            {
                // Wall jump successful
            }
            else if (bunnyhopComponent.TryBunnyhop())
            {
                // Bunnyhop successful
            }
            else
            {
                movementComponent.HandleJump();
            }
        }

        // Handle slide
        if (Input.IsActionJustPressed("slide") && IsOnFloor())
        {
            slideComponent.TryStartSlide();
        }

        // Handle dash
        if (Input.IsActionJustPressed("dash"))
        {
            var inputDirection = GetInputDirection();
            dashComponent.TryDash(inputDirection);
        }

        // Handle mantle
        if (Input.IsActionJustPressed("mantle"))
        {
            mantleComponent.TryMantle();
        }

        // Handle sprint stamina consumption
        if (Input.IsActionPressed("sprint") && IsOnFloor())
        {
            if (!staminaComponent.TryConsumeSprint(GetPhysicsProcessDeltaTime()))
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

    private void HandleAbilityInputs()
    {
        // Handle heavy melee
        if (Input.IsActionJustPressed("heavy_melee"))
        {
            var inputDirection = GetInputDirection();
            heavyMeleeComponent.TryHeavyMelee(inputDirection);
        }

        // Handle parry
        if (Input.IsActionJustPressed("parry"))
        {
            parryComponent.TryParry();
        }

        // Handle character-specific abilities (example for Abrams)
        if (Input.IsActionJustPressed("charge") && Name == "Abrams")
        {
            var abramsComponent = GetNode<AbramsMovementComponent>("AbramsMovementComponent");
            var inputDirection = GetInputDirection();
            abramsComponent.TryStartCharge(inputDirection);
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Handle mouse look
        if (@event is InputEventMouseMotion mouseMotion)
        {
            HandleMouseLook(mouseMotion);
        }

        // Handle input actions
        if (@event.IsActionPressed("ui_cancel"))
        {
            if (Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                Input.MouseMode = Input.MouseModeEnum.Visible;
            }
            else
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
            }
        }
    }

    private void HandleMouseLook(InputEventMouseMotion mouseMotion)
    {
        // Rotate character horizontally
        RotateY(-mouseMotion.Relative.x * MouseSensitivity * 0.01f);

        // Rotate camera vertically (clamped)
        camera.RotateX(-mouseMotion.Relative.y * MouseSensitivity * 0.01f);
        camera.Rotation = new Vector3(
            Mathf.Clamp(camera.Rotation.x, -Mathf.Pi / 2f, Mathf.Pi / 2f),
            camera.Rotation.y,
            camera.Rotation.z
        );
    }

    private Vector3 GetInputDirection()
    {
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        return new Vector3(input.x, 0, input.y).Rotated(Vector3.Up, GlobalRotation.y);
    }

    private void UpdateCamera()
    {
        camera.TargetPosition = GlobalPosition + Vector3.Up * 2f;
    }

    private void UpdateAnimations()
    {
        var currentState = movementComponent.GetCurrentState();
        var animationName = GetAnimationForState(currentState);

        if (animationPlayer.HasAnimation(animationName))
        {
            if (!animationPlayer.IsPlaying() || animationPlayer.CurrentAnimation != animationName)
            {
                animationPlayer.Play(animationName);
            }
        }
    }

    private string GetAnimationForState(MovementState state)
    {
        return state switch
        {
            MovementState.Idle => "idle",
            MovementState.Walking => "walk",
            MovementState.Sprinting => "sprint",
            MovementState.Crouching => "crouch",
            MovementState.Jumping => "jump",
            MovementState.Falling => "fall",
            MovementState.Sliding => "slide",
            MovementState.Dashing => "dash",
            _ => "idle"
        };
    }

    // Signal handlers
    private void OnStaminaChanged(float current, float max)
    {
        // Update UI stamina bar
        var staminaBar = GetNode<ProgressBar>("UI/StaminaBar");
        if (staminaBar != null)
        {
            staminaBar.Value = current / max;
        }
    }

    private void OnStaminaDepleted()
    {
        // Visual feedback for stamina depletion
        var staminaEffect = GetNode<GpuParticles3D>("StaminaDepletedEffect");
        staminaEffect?.Emitting = true;
    }

    private void OnDashStarted(Vector3 direction)
    {
        // Screen effects for dash
        camera.AddTrauma(0.3f);

        // Play dash sound
        var dashSound = GetNode<AudioStreamPlayer3D>("DashSound");
        dashSound?.Play();
    }

    private void OnDashEnded()
    {
        // Dash ended effects
    }

    private void OnSlideStarted()
    {
        // Slide effects
        camera.AddTrauma(0.2f);

        var slideSound = GetNode<AudioStreamPlayer3D>("SlideSound");
        slideSound?.Play();
    }

    private void OnSlideEnded()
    {
        // Slide ended effects
    }

    private void OnWallJumped(Vector3 direction)
    {
        // Wall jump effects
        camera.AddTrauma(0.4f);

        var wallJumpSound = GetNode<AudioStreamPlayer3D>("WallJumpSound");
        wallJumpSound?.Play();
    }

    private void OnMantleStarted(Vector3 target)
    {
        // Mantle effects
        camera.AddTrauma(0.3f);

        var mantleSound = GetNode<AudioStreamPlayer3D>("MantleSound");
        mantleSound?.Play();
    }

    private void OnMantleCompleted()
    {
        // Mantle completed effects
    }

    private void OnMeleeStarted()
    {
        // Melee windup effects
        camera.AddTrauma(0.2f);
    }

    private void OnMeleeHit(Node3D target)
    {
        // Melee hit effects
        camera.AddTrauma(0.5f);

        var hitEffect = GetNode<GpuParticles3D>("MeleeHitEffect");
        hitEffect?.GlobalPosition = target.GlobalPosition;
        hitEffect?.Emitting = true;

        var meleeHitSound = GetNode<AudioStreamPlayer3D>("MeleeHitSound");
        meleeHitSound?.Play();
    }

    private void OnMeleeFinished()
    {
        // Melee finished effects
    }

    private void OnParryStarted()
    {
        // Parry windup effects
        var parryGlow = GetNode<GpuParticles3D>("ParryGlow");
        parryGlow?.Emitting = true;
    }

    private void OnParrySuccess(Node3D attacker)
    {
        // Parry success effects
        camera.AddTrauma(0.6f);

        var parrySuccessSound = GetNode<AudioStreamPlayer3D>("ParrySuccessSound");
        parrySuccessSound?.Play();
    }

    private void OnParryFailed()
    {
        // Parry failed effects
        var parryFailSound = GetNode<AudioStreamPlayer3D>("ParryFailSound");
        parryFailSound?.Play();
    }
}
```

## 2. Scene Structure Setup

### Character Scene Hierarchy
```
CharacterBody3D (CharacterController script)
├── CollisionShape3D (Capsule shape for physics)
├── MeshInstance3D (Character model)
├── AnimationPlayer (Movement animations)
├── Camera3D (ThirdPersonCamera script)
│
├── MovementComponent (Core locomotion)
├── StaminaComponent (Energy management)
├── CrouchComponent (Stance changes)
├── InputBuffer (Input timing forgiveness)
│
├── DashComponent (Burst movement)
├── SlideComponent (Momentum sliding)
├── BunnyhopComponent (Speed building)
├── WallJumpComponent (Wall interactions)
├── MantleComponent (Ledge climbing)
│
├── HeavyMeleeComponent (Melee attacks)
├── ParryComponent (Defensive abilities)
├── StatusEffectComponent (Movement debuffs)
│
├── AudioStreamPlayer3D (Footstep sounds)
├── GpuParticles3D (Movement effects)
└── UI (Stamina bar, crosshair, etc.)
```

## 3. Input Map Configuration

### Project Settings > Input Map
```
# Movement
move_forward = W
move_backward = S
move_left = A
move_right = D

# Actions
jump = Space
crouch = Ctrl
sprint = Shift
slide = C
dash = Q
mantle = E

# Abilities
heavy_melee = Right Mouse Button
parry = Left Mouse Button

# Character-specific (example for Abrams)
charge = R

# Camera
ui_cancel = Escape
```

## 4. Component Base Classes

### MovementComponent.cs
```csharp
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
            velocity.y = JumpForce;
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
            coyoteTimer -= delta;
        }
    }

    private void HandleGroundMovement(double delta)
    {
        if (!character.IsOnFloor()) return;

        var inputDirection = GetInputDirection();
        var targetSpeed = sprintEnabled ? SprintSpeed : WalkSpeed;
        targetSpeed *= speedMultiplier;

        // Smooth acceleration to target speed
        var currentSpeed = new Vector3(velocity.x, 0, velocity.z).Length();
        var speedDifference = targetSpeed - currentSpeed;

        if (speedDifference > 0)
        {
            var accelerationAmount = Mathf.Min(speedDifference, Acceleration * delta);
            velocity += inputDirection * accelerationAmount;
        }
        else
        {
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

    private void HandleAirMovement(double delta)
    {
        if (character.IsOnFloor()) return;

        var inputDirection = GetInputDirection();

        // Apply air strafing
        velocity.x = Mathf.Lerp(velocity.x, inputDirection.x * WalkSpeed * AirControl * speedMultiplier, AirAcceleration * delta);
        velocity.z = Mathf.Lerp(velocity.z, inputDirection.z * WalkSpeed * AirControl * speedMultiplier, AirAcceleration * delta);

        // Clamp maximum air speed
        var horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);
        var maxAirSpeed = WalkSpeed * 1.2f * speedMultiplier;

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
            velocity.y = 0;
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
            CurrentState = velocity.y > 0 ? MovementState.Jumping : MovementState.Falling;
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
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        return new Vector3(input.x, 0, input.y).Rotated(Vector3.Up, character.GlobalRotation.y);
    }
}
```

## 5. Third-Person Camera Script

### ThirdPersonCamera.cs
```csharp
using Godot;

public partial class ThirdPersonCamera : Camera3D
{
    [Export] public float Distance { get; set; } = 5f;
    [Export] public float Height { get; set; } = 2f;
    [Export] public float Damping { get; set; } = 5f;
    [Export] public Vector3 TargetPosition { get; set; } = Vector3.Zero;

    private Vector3 velocity = Vector3.Zero;
    private float trauma = 0f;
    private float traumaDecay = 2f;

    public override void _Process(double delta)
    {
        UpdateCameraPosition(delta);
        ApplyTrauma(delta);
    }

    private void UpdateCameraPosition(double delta)
    {
        var targetCameraPosition = TargetPosition - GlobalTransform.basis.z * Distance + Vector3.Up * Height;

        // Smooth camera movement
        GlobalPosition = GlobalPosition.Lerp(targetCameraPosition, Damping * delta);

        // Look at target
        LookAt(TargetPosition, Vector3.Up);
    }

    public void AddTrauma(float amount)
    {
        trauma = Mathf.Min(trauma + amount, 1f);
    }

    private void ApplyTrauma(double delta)
    {
        if (trauma > 0)
        {
            // Apply screen shake
            var shake = trauma * trauma;
            var offsetX = (float)GD.RandRange(-shake, shake);
            var offsetY = (float)GD.RandRange(-shake, shake);

            HOffset = offsetX;
            VOffset = offsetY;

            trauma -= traumaDecay * delta;
            if (trauma < 0) trauma = 0;
        }
        else
        {
            HOffset = 0;
            VOffset = 0;
        }
    }
}
```

## 6. Environment Objects

### Zipline Scene
```csharp
// Zipline.tscn - StaticBody3D with Area3D for detection
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
```

## 7. Testing and Debugging

### Console Commands for Testing
```csharp
public partial class MovementDebugCommands : Node
{
    public override void _Ready()
    {
        // Register console commands for testing
        var console = GetNode<ConsoleSystem>("/root/ConsoleSystem");

        console.AddCommand("give_stamina", "Set stamina to max", CmdGiveStamina);
        console.AddCommand("toggle_godmode", "Toggle infinite stamina and no cooldowns", CmdToggleGodMode);
        console.AddCommand("set_speed", "Set movement speed multiplier", CmdSetSpeed);
        console.AddCommand("teleport", "Teleport to position", CmdTeleport);
    }

    private void CmdGiveStamina(string[] args)
    {
        var character = GetParent<CharacterBody3D>();
        var staminaComponent = character.GetNode<StaminaComponent>("StaminaComponent");
        staminaComponent?.SetStamina(staminaComponent.MaxStamina);
    }

    private void CmdToggleGodMode(string[] args)
    {
        var character = GetParent<CharacterBody3D>();
        var movementComponent = character.GetNode<MovementComponent>("MovementComponent");

        movementComponent.ToggleGodMode();
        GD.Print("God mode: " + (movementComponent.GodMode ? "ON" : "OFF"));
    }

    private void CmdSetSpeed(string[] args)
    {
        if (args.Length > 0 && float.TryParse(args[0], out float speed))
        {
            var character = GetParent<CharacterBody3D>();
            var movementComponent = character.GetNode<MovementComponent>("MovementComponent");
            movementComponent.SetSpeedMultiplier(speed);
        }
    }

    private void CmdTeleport(string[] args)
    {
        if (args.Length >= 3 &&
            float.TryParse(args[0], out float x) &&
            float.TryParse(args[1], out float y) &&
            float.TryParse(args[2], out float z))
        {
            GetParent<CharacterBody3D>().GlobalPosition = new Vector3(x, y, z);
        }
    }
}
```

## 8. Performance Optimization

### Movement Optimizer
```csharp
public partial class MovementOptimizer : Node
{
    private Queue<Vector3> velocityHistory = new();
    private const int HistorySize = 10;
    private double optimizationTimer = 0f;
    private const double OptimizationInterval = 0.1f; // Every 100ms

    public override void _PhysicsProcess(double delta)
    {
        optimizationTimer += delta;

        if (optimizationTimer >= OptimizationInterval)
        {
            OptimizeMovement();
            optimizationTimer = 0f;
        }

        // Track velocity for prediction
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        var currentVelocity = movementComponent.GetVelocity();

        velocityHistory.Enqueue(currentVelocity);
        if (velocityHistory.Count > HistorySize)
        {
            velocityHistory.Dequeue();
        }
    }

    private void OptimizeMovement()
    {
        // Time-slice expensive operations
        OptimizeCollisionChecks();
        UpdatePerformanceMetrics();
        PredictMovement();
    }

    private void OptimizeCollisionChecks()
    {
        // Batch collision checks and cache results
        var character = GetParent<CharacterBody3D>();

        // Cache floor check results
        var cachedIsOnFloor = character.IsOnFloor();

        // Only perform expensive checks when needed
        if (cachedIsOnFloor != wasOnFloor)
        {
            OnFloorStateChanged(cachedIsOnFloor);
        }

        wasOnFloor = cachedIsOnFloor;
    }

    private void PredictMovement()
    {
        // Use velocity history to predict future movement for optimization
        if (velocityHistory.Count >= 3)
        {
            var recentVelocities = velocityHistory.TakeLast(3).ToArray();
            var averageVelocity = recentVelocities.Aggregate(Vector3.Zero, (acc, v) => acc + v) / recentVelocities.Length;

            // Pre-emptively adjust physics settings based on predicted movement
            if (averageVelocity.Length() > 15f) // High speed movement
            {
                // Increase physics tick rate or adjust collision detection
            }
        }
    }

    private void UpdatePerformanceMetrics()
    {
        // Track performance metrics for debugging
        var fps = Engine.GetFramesPerSecond();
        var frameTime = 1f / fps;

        // Log if performance drops below threshold
        if (frameTime > 0.02f) // Less than 50 FPS
        {
            GD.Print($"Performance warning: Frame time {frameTime:F3}s ({fps} FPS)");
        }
    }

    private bool wasOnFloor = false;

    private void OnFloorStateChanged(bool isOnFloor)
    {
        if (isOnFloor)
        {
            // Just landed - perform landing optimizations
            var movementComponent = GetNode<MovementComponent>("../MovementComponent");
            movementComponent.ResetAirMovement();
        }
    }
}
```

## 9. Integration with Game Systems

### Save/Load System Integration
```csharp
public partial class MovementSaveSystem : Node
{
    public Dictionary<string, Variant> SaveMovementState()
    {
        var character = GetParent<CharacterBody3D>();
        var movementComponent = GetNode<MovementComponent>("MovementComponent");
        var staminaComponent = GetNode<StaminaComponent>("StaminaComponent");

        return new Dictionary<string, Variant>
        {
            ["position"] = character.GlobalPosition,
            ["rotation"] = character.GlobalRotation,
            ["velocity"] = movementComponent.GetVelocity(),
            ["current_stamina"] = staminaComponent.GetCurrentStamina(),
            ["movement_state"] = (int)movementComponent.GetCurrentState()
        };
    }

    public void LoadMovementState(Dictionary<string, Variant> state)
    {
        var character = GetParent<CharacterBody3D>();
        var movementComponent = GetNode<MovementComponent>("MovementComponent");
        var staminaComponent = GetNode<StaminaComponent>("StaminaComponent");

        if (state.TryGetValue("position", out Variant position))
        {
            character.GlobalPosition = (Vector3)position;
        }

        if (state.TryGetValue("rotation", out Variant rotation))
        {
            character.GlobalRotation = (Vector3)rotation;
        }

        if (state.TryGetValue("velocity", out Variant velocity))
        {
            movementComponent.SetVelocity((Vector3)velocity);
        }

        if (state.TryGetValue("current_stamina", out Variant stamina))
        {
            staminaComponent.SetStamina((float)stamina);
        }

        if (state.TryGetValue("movement_state", out Variant movementState))
        {
            movementComponent.SetMovementState((MovementState)(int)movementState);
        }
    }
}
```

## 10. Complete Project Setup

### Project Structure for Deadlock Movement
```
Waterjam3D/
├── docs/
│   ├── deadlock_movement_system.md           # Main architecture
│   ├── deadlock_movement_core.md             # Core mechanics
│   ├── deadlock_movement_advanced.md         # Advanced techniques
│   ├── deadlock_movement_environmental.md    # Environmental interactions
│   ├── deadlock_movement_character_specific.md # Character abilities
│   └── deadlock_movement_implementation_examples.md # This file
├── scripts/
│   ├── core/
│   │   ├── movement/
│   │   │   ├── MovementComponent.cs
│   │   │   ├── StaminaComponent.cs
│   │   │   ├── CrouchComponent.cs
│   │   │   ├── InputBuffer.cs
│   │   │   ├── DashComponent.cs
│   │   │   ├── SlideComponent.cs
│   │   │   ├── BunnyhopComponent.cs
│   │   │   ├── WallJumpComponent.cs
│   │   │   ├── MantleComponent.cs
│   │   │   ├── HeavyMeleeComponent.cs
│   │   │   ├── ParryComponent.cs
│   │   │   └── StatusEffectComponent.cs
│   │   ├── camera/
│   │   │   └── ThirdPersonCamera.cs
│   │   └── character/
│   │       └── CharacterController.cs
│   └── environment/
│       ├── Zipline.cs
│       ├── Rope.cs
│       └── Vent.cs
└── scenes/
    └── character/
        └── PlayerCharacter.tscn
```

This implementation provides a complete, production-ready Deadlock-style movement system for Godot that captures the fluid, skill-based locomotion that defines the genre. The modular component design allows for easy customization and character-specific variations while maintaining consistent core mechanics.
