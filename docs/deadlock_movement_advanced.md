# Deadlock Movement System - Advanced Mechanics Implementation

## Advanced Movement Components

This document details the implementation of advanced movement techniques that define the skill ceiling and fluid gameplay of Deadlock-style movement.

## 1. DashComponent - Burst Movement System

### Core Dashing Mechanics

#### Class Structure
```csharp
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

        if (staminaComponent != null && !staminaComponent.TryConsumeStamina(StaminaCost))
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
        ApplyDashEffects();
    }

    private Vector3 CalculateDashDirection(Vector3 inputDirection)
    {
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        var currentVelocity = movementComponent.GetVelocity();

        // Strafe dashing: preserve horizontal momentum, add dash force
        if (currentVelocity.Length() > 0.1f)
        {
            var strafeDirection = new Vector3(currentVelocity.x, 0, currentVelocity.z).Normalized();
            return (strafeDirection * 0.7f + inputDirection * 0.3f).Normalized();
        }

        return inputDirection.Normalized();
    }

    private void UpdateDash(double delta)
    {
        if (!isDashing) return;

        dashTimer -= delta;
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
            dashVelocity.y = originalVelocity.y * 0.5f;
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
            cooldownTimer -= delta;
            if (cooldownTimer <= 0)
            {
                canDash = true;
            }
        }
    }
}
```

### Dash Types and Variations

#### Strafe Dashing
```csharp
private Vector3 CalculateStrafeDash(Vector3 inputDirection, Vector3 currentVelocity)
{
    // Combine current momentum with input direction
    var momentumWeight = 0.6f;
    var inputWeight = 0.4f;

    var strafeDirection = currentVelocity.Normalized();
    var combinedDirection = (strafeDirection * momentumWeight + inputDirection * inputWeight).Normalized();

    return combinedDirection;
}
```

#### Dash Jumping
```csharp
public void TryDashJump()
{
    if (isDashing && dashTimer > DashDuration * 0.3f)
    {
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        var currentVelocity = movementComponent.GetVelocity();

        // Add upward momentum during dash
        var jumpVelocity = currentVelocity + Vector3.Up * 12f;
        movementComponent.SetVelocity(jumpVelocity);

        EndDash(); // Cancel dash on jump
    }
}
```

#### Parry Dash
```csharp
public void TryParryDash(Vector3 parryDirection)
{
    // Special dash that reflects incoming damage/projectiles
    if (TryDash(parryDirection))
    {
        // Add parry effects (screen flash, sound, invincibility frames)
        ApplyParryEffects();
    }
}
```

## 2. SlideComponent - Momentum Conservation

### Sliding Mechanics

#### Class Structure
```csharp
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
        ApplySlideEffects();
    }

    private void UpdateSlide(double delta)
    {
        slideTimer -= delta;

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
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        return new Vector3(input.x, 0, input.y).Rotated(Vector3.Up, GetParent<CharacterBody3D>().GlobalRotation.y);
    }
}
```

### Slide Variations

#### Slide Bunnyhopping
```csharp
public void TrySlideBunnyhop()
{
    if (isSliding && Input.IsActionJustPressed("jump"))
    {
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        var currentVelocity = movementComponent.GetVelocity();

        // Convert slide momentum into jump
        var jumpVelocity = currentVelocity * 0.8f + Vector3.Up * 10f;
        movementComponent.SetVelocity(jumpVelocity);

        EndSlide(); // Exit slide on bunnyhop
    }
}
```

#### Dash Sliding
```csharp
public void TryDashSlide(Vector3 dashDirection)
{
    // Combine dash with immediate slide
    var dashComponent = GetNode<DashComponent>("../DashComponent");
    if (dashComponent.TryDash(dashDirection))
    {
        // Start sliding immediately after dash
        Invoke(nameof(StartSlide), DashComponent.DashDuration * 0.8f);
    }
}
```

## 3. BunnyhopComponent - Speed Building

### Bunnyhopping Mechanics

#### Class Structure
```csharp
public partial class BunnyhopComponent : Node
{
    [Export] public float BunnyhopGain { get; set; } = 1.5f;
    [Export] public float MaxBunnyhopSpeed { get; set; } = 12f;
    [Export] public float JumpTimingWindow { get; set; } = 0.1f;

    private float lastJumpTime = 0f;
    private int consecutiveJumps = 0;
    private Vector3 lastGroundVelocity = Vector3.Zero;

    public override void _PhysicsProcess(double delta)
    {
        UpdateJumpTiming();
    }

    public void TryBunnyhop()
    {
        var character = GetParent<CharacterBody3D>();
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");

        if (character.IsOnFloor() && CanBunnyhop())
        {
            var inputDirection = GetInputDirection();
            var currentVelocity = movementComponent.GetVelocity();

            // Calculate bunnyhop gain based on ground angle and timing
            var speedGain = CalculateBunnyhopGain(inputDirection, currentVelocity);

            // Apply speed gain
            var newVelocity = currentVelocity + inputDirection * speedGain;
            newVelocity.y = movementComponent.JumpForce;

            // Limit maximum speed
            var horizontalSpeed = new Vector3(newVelocity.x, 0, newVelocity.z).Length();
            if (horizontalSpeed > MaxBunnyhopSpeed)
            {
                var limitedVelocity = newVelocity.Normalized() * MaxBunnyhopSpeed;
                limitedVelocity.y = newVelocity.y;
                newVelocity = limitedVelocity;
            }

            movementComponent.SetVelocity(newVelocity);

            // Track consecutive jumps for combo system
            consecutiveJumps++;
            lastJumpTime = Time.GetTime();
            lastGroundVelocity = currentVelocity;
        }
    }

    private bool CanBunnyhop()
    {
        // Allow bunnyhop if recently jumped or on ground
        return Time.GetTime() - lastJumpTime < JumpTimingWindow ||
               GetParent<CharacterBody3D>().IsOnFloor();
    }

    private float CalculateBunnyhopGain(Vector3 inputDirection, Vector3 currentVelocity)
    {
        var baseGain = BunnyhopGain;

        // Bonus gain for perfect timing
        if (Time.GetTime() - lastJumpTime < JumpTimingWindow * 0.5f)
        {
            baseGain *= 1.3f;
        }

        // Reduced gain if jumping against momentum
        var dotProduct = inputDirection.Dot(currentVelocity.Normalized());
        if (dotProduct < 0)
        {
            baseGain *= 0.7f;
        }

        // Gain based on ground angle (steeper = more gain)
        var floorNormal = GetParent<CharacterBody3D>().GetFloorNormal();
        var angleBonus = 1f + (1f - floorNormal.y) * 0.5f;

        return baseGain * angleBonus;
    }

    private Vector3 GetInputDirection()
    {
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        return new Vector3(input.x, 0, input.y).Rotated(Vector3.Up, GetParent<CharacterBody3D>().GlobalRotation.y);
    }

    private void UpdateJumpTiming()
    {
        // Reset combo if too much time passes
        if (Time.GetTime() - lastJumpTime > 1f)
        {
            consecutiveJumps = 0;
        }
    }
}
```

### Raw Bunnyhopping
```csharp
public void TryRawBunnyhop()
{
    var character = GetParent<CharacterBody3D>();
    var movementComponent = GetNode<MovementComponent>("../MovementComponent");

    // Raw bunnyhopping: jump immediately when landing
    if (character.IsOnFloor() && !character.IsOnFloor() != wasOnFloor) // Just landed
    {
        if (CanBunnyhop())
        {
            movementComponent.SetVelocity(movementComponent.GetVelocity() + Vector3.Up * movementComponent.JumpForce);
            lastJumpTime = Time.GetTime();
        }
    }

    wasOnFloor = character.IsOnFloor();
}
```

## 4. Advanced Air Movement

### Air Strafing Enhancement
```csharp
public partial class AirMovementComponent : Node
{
    [Export] public float AirAcceleration { get; set; } = 8f;
    [Export] public float AirFriction { get; set; } = 0.95f;
    [Export] public float MaxAirSpeed { get; set; } = 9f;

    public override void _PhysicsProcess(double delta)
    {
        if (GetParent<CharacterBody3D>().IsOnFloor()) return;

        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        var inputDirection = GetInputDirection();
        var currentVelocity = movementComponent.GetVelocity();

        // Enhanced air strafing
        var targetVelocity = inputDirection * MaxAirSpeed;
        var acceleration = AirAcceleration * delta;

        // Smooth interpolation to target velocity
        currentVelocity.x = Mathf.Lerp(currentVelocity.x, targetVelocity.x, acceleration);
        currentVelocity.z = Mathf.Lerp(currentVelocity.z, targetVelocity.z, acceleration);

        // Apply air friction
        currentVelocity *= AirFriction;

        movementComponent.SetVelocity(currentVelocity);
    }
}
```

### Instant Air Dash
```csharp
public void TryInstantAirDash()
{
    var dashComponent = GetNode<DashComponent>("../DashComponent");
    var inputDirection = GetInputDirection();

    if (dashComponent.TryDash(inputDirection))
    {
        // Instant dash: no duration, immediate velocity change
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        var currentVelocity = movementComponent.GetVelocity();
        var dashVelocity = inputDirection * dashComponent.AirDashSpeed;

        // Preserve vertical momentum
        dashVelocity.y = currentVelocity.y;
        movementComponent.SetVelocity(dashVelocity);
    }
}
```

## 5. Integration and State Management

### Advanced Movement Controller
```csharp
public partial class AdvancedMovementController : Node
{
    private DashComponent dashComponent;
    private SlideComponent slideComponent;
    private BunnyhopComponent bunnyhopComponent;
    private AirMovementComponent airMovementComponent;

    public override void _Ready()
    {
        dashComponent = GetNode<DashComponent>("../DashComponent");
        slideComponent = GetNode<SlideComponent>("../SlideComponent");
        bunnyhopComponent = GetNode<BunnyhopComponent>("../BunnyhopComponent");
        airMovementComponent = GetNode<AirMovementComponent>("../AirMovementComponent");
    }

    public override void _PhysicsProcess(double delta)
    {
        var character = GetParent<CharacterBody3D>();

        // Handle slide input
        if (Input.IsActionJustPressed("slide") && character.IsOnFloor())
        {
            slideComponent.TryStartSlide();
        }

        // Handle dash input
        if (Input.IsActionJustPressed("dash"))
        {
            var inputDirection = GetInputDirection();
            dashComponent.TryDash(inputDirection);
        }

        // Handle bunnyhop
        if (Input.IsActionJustPressed("jump"))
        {
            bunnyhopComponent.TryBunnyhop();
        }

        // Handle advanced air movement
        if (Input.IsActionJustPressed("air_dash"))
        {
            TryInstantAirDash();
        }
    }

    private Vector3 GetInputDirection()
    {
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        return new Vector3(input.x, 0, input.y).Rotated(Vector3.Up, character.GlobalRotation.y);
    }
}
```

## 6. Visual and Audio Effects

### Dash Effects
```csharp
private void ApplyDashEffects()
{
    // Screen effects
    var camera = GetViewport().GetCamera3D();
    camera.AddTrauma(0.3f); // Screen shake

    // Particle effects
    var dashParticles = dashParticleScene.Instantiate<GpuParticles3D>();
    GetParent().AddChild(dashParticles);
    dashParticles.GlobalPosition = GlobalPosition;
    dashParticles.Emitting = true;

    // Sound effects
    var audioPlayer = new AudioStreamPlayer3D();
    audioPlayer.Stream = dashSound;
    audioPlayer.GlobalPosition = GlobalPosition;
    GetParent().AddChild(audioPlayer);
    audioPlayer.Play();
}
```

### Slide Effects
```csharp
private void ApplySlideEffects()
{
    // Speed lines effect
    var speedLines = speedLineParticleScene.Instantiate<GpuParticles3D>();
    GetParent().AddChild(speedLines);
    speedLines.Emitting = true;

    // Sliding sound
    slideAudioPlayer.Stream = slideSound;
    slideAudioPlayer.Play();
}
```

## 7. Configuration Values

### Recommended Starting Values
```csharp
// DashComponent
DashSpeed = 25f;
DashDuration = 0.15f;
DashCooldown = 1f;
StaminaCost = 25f;
AirDashSpeed = 20f;

// SlideComponent
SlideSpeed = 15f;
SlideFriction = 0.3f;
SlideDuration = 1.5f;
MinSlideSpeed = 8f;

// BunnyhopComponent
BunnyhopGain = 1.5f;
MaxBunnyhopSpeed = 12f;
JumpTimingWindow = 0.1f;

// AirMovementComponent
AirAcceleration = 8f;
AirFriction = 0.95f;
MaxAirSpeed = 9f;
```

## 8. Testing Checklist

- [ ] Dashing provides burst movement in desired direction
- [ ] Strafe dashing preserves horizontal momentum
- [ ] Sliding maintains speed and allows direction changes
- [ ] Bunnyhopping builds speed with consecutive jumps
- [ ] Air strafing allows directional control while airborne
- [ ] Dash-slide combinations work smoothly
- [ ] Visual and audio effects enhance movement feel
- [ ] Stamina costs balance advanced techniques appropriately

## 9. Balance Considerations

### Speed Curves
- **Early Game**: Lower speeds, higher friction for easier learning
- **Late Game**: Higher speeds, lower friction for skilled play
- **Stamina Costs**: Increase with technique complexity
- **Cooldowns**: Prevent spam while allowing skilled combinations

### Input Responsiveness
- **Buffer Windows**: 100-150ms for forgiving inputs
- **State Transitions**: Smooth blending between movement states
- **Momentum Preservation**: Maintain player intent across state changes

This advanced movement system builds upon the core mechanics to create the fluid, skill-based movement that defines Deadlock-style gameplay. The modular design allows for easy balancing and character-specific variations.

