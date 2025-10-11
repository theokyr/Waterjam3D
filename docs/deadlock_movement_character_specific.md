# Deadlock Movement System - Character-Specific Mechanics Implementation

## Character-Specific Movement Abilities

This document details the implementation of character-specific movement mechanics that provide unique locomotion options and combat-movement integration in Deadlock-style gameplay.

## 1. HeavyMeleeComponent - Melee Attack Integration

### Heavy Melee Mechanics

#### Class Structure
```csharp
public partial class HeavyMeleeComponent : Node
{
    [Export] public float MeleeRange { get; set; } = 2.5f;
    [Export] public float MeleeDamage { get; set; } = 75f;
    [Export] public float MeleeForce { get; set; } = 15f;
    [Export] public float MeleeCooldown { get; set; } = 1.5f;
    [Export] public float MeleeWindup { get; set; } = 0.3f;
    [Export] public float MeleeRecovery { get; set; } = 0.4f;

    [Signal] public delegate void MeleeStartedEventHandler();
    [Signal] public delegate void MeleeHitEventHandler(Node3D target);
    [Signal] public delegate void MeleeFinishedEventHandler();

    public enum MeleeState { Ready, WindingUp, Attacking, Recovering, Cooldown }

    private MeleeState currentState = MeleeState.Ready;
    private float stateTimer = 0f;
    private Vector3 meleeDirection = Vector3.Zero;
    private bool canMelee = true;

    public override void _PhysicsProcess(double delta)
    {
        UpdateMeleeState(delta);
    }

    public bool TryHeavyMelee(Vector3 direction)
    {
        if (currentState != MeleeState.Ready || !canMelee) return false;

        StartMelee(direction);
        return true;
    }

    private void StartMelee(Vector3 direction)
    {
        currentState = MeleeState.WindingUp;
        stateTimer = MeleeWindup;
        meleeDirection = direction;

        EmitSignal(SignalName.MeleeStarted);
        ApplyMeleeEffects();
    }

    private void UpdateMeleeState(double delta)
    {
        if (currentState == MeleeState.Ready) return;

        stateTimer -= delta;

        switch (currentState)
        {
            case MeleeState.WindingUp:
                if (stateTimer <= 0)
                {
                    ExecuteMeleeAttack();
                }
                break;

            case MeleeState.Attacking:
                if (stateTimer <= 0)
                {
                    currentState = MeleeState.Recovering;
                    stateTimer = MeleeRecovery;
                }
                break;

            case MeleeState.Recovering:
                if (stateTimer <= 0)
                {
                    FinishMelee();
                }
                break;

            case MeleeState.Cooldown:
                if (stateTimer <= 0)
                {
                    currentState = MeleeState.Ready;
                    canMelee = true;
                }
                break;
        }
    }

    private void ExecuteMeleeAttack()
    {
        currentState = MeleeState.Attacking;
        stateTimer = 0.1f; // Quick attack window

        var character = GetParent<CharacterBody3D>();
        var attackOrigin = character.GlobalPosition + Vector3.Up * 1f;

        // Create attack sphere
        var attackQuery = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = MeleeRange },
            Transform = new Transform3D(Basis.Identity, attackOrigin),
            CollideWithAreas = false,
            CollideWithBodies = true
        };

        var results = character.GetWorld3D().DirectSpaceState.IntersectShape(attackQuery);

        foreach (var result in results)
        {
            var collider = (Node3D)result["collider"];
            if (collider != character && collider.HasMethod("TakeDamage"))
            {
                // Apply damage and force
                collider.Call("TakeDamage", MeleeDamage);
                ApplyMeleeForce(collider);

                EmitSignal(SignalName.MeleeHit, collider);
            }
        }
    }

    private void ApplyMeleeForce(Node3D target)
    {
        if (target is CharacterBody3D targetBody)
        {
            var forceDirection = (target.GlobalPosition - GetParent<CharacterBody3D>().GlobalPosition).Normalized();
            forceDirection.y = 0.5f; // Add upward component

            var movementComponent = target.GetNode<MovementComponent>("MovementComponent");
            if (movementComponent != null)
            {
                movementComponent.SetVelocity(forceDirection * MeleeForce);
            }
        }
    }

    private void FinishMelee()
    {
        currentState = MeleeState.Cooldown;
        stateTimer = MeleeCooldown;
        canMelee = false;

        EmitSignal(SignalName.MeleeFinished);
    }

    private void ApplyMeleeEffects()
    {
        // Visual effects (screen shake, particles, etc.)
        var camera = GetViewport().GetCamera3D();
        camera.AddTrauma(0.5f);

        // Animation trigger
        var animationPlayer = GetParent<CharacterBody3D>().GetNode<AnimationPlayer>("AnimationPlayer");
        animationPlayer?.Play("melee_attack");
    }
}
```

### Heavy Melee Cancel
```csharp
public void TryMeleeCancel()
{
    if (currentState == MeleeState.WindingUp)
    {
        // Cancel windup for movement ability
        currentState = MeleeState.Recovering;
        stateTimer = MeleeRecovery * 0.5f; // Reduced recovery

        // Restore some stamina
        var staminaComponent = GetNode<StaminaComponent>("../StaminaComponent");
        staminaComponent?.Regenerate(MeleeWindup * 0.3f);
    }
}
```

### Heavy Melee Cancel Extension
```csharp
public void TryMeleeCancelExtension()
{
    if (currentState == MeleeState.Attacking)
    {
        // Extend attack window for combo potential
        stateTimer += 0.05f;
    }
}
```

## 2. ParryComponent - Defensive Movement

### Parry Mechanics

#### Class Structure
```csharp
public partial class ParryComponent : Node
{
    [Export] public float ParryWindow { get; set; } = 0.25f;
    [Export] public float ParryStaminaCost { get; set; } = 30f;
    [Export] public float ParryCooldown { get; set; } = 2f;
    [Export] public float ParryDashSpeed { get; set; } = 20f;

    [Signal] public delegate void ParryStartedEventHandler();
    [Signal] public delegate void ParrySuccessEventHandler(Node3D attacker);
    [Signal] public delegate void ParryFailedEventHandler();

    public enum ParryState { Ready, Parrying, Cooldown }

    private ParryState currentState = ParryState.Ready;
    private float stateTimer = 0f;
    private bool canParry = true;

    public override void _PhysicsProcess(double delta)
    {
        UpdateParryState(delta);
    }

    public bool TryParry()
    {
        if (currentState != ParryState.Ready || !canParry) return false;

        var staminaComponent = GetNode<StaminaComponent>("../StaminaComponent");
        if (staminaComponent != null && !staminaComponent.TryConsumeStamina(ParryStaminaCost))
        {
            return false;
        }

        StartParry();
        return true;
    }

    private void StartParry()
    {
        currentState = ParryState.Parrying;
        stateTimer = ParryWindow;
        canParry = false;

        EmitSignal(SignalName.ParryStarted);
        ApplyParryEffects();
    }

    private void UpdateParryState(double delta)
    {
        if (currentState == ParryState.Ready) return;

        stateTimer -= delta;

        if (currentState == ParryState.Parrying)
        {
            if (stateTimer <= 0)
            {
                // Parry window ended without success
                FailParry();
            }
        }
        else if (currentState == ParryState.Cooldown)
        {
            if (stateTimer <= 0)
            {
                currentState = ParryState.Ready;
                canParry = true;
            }
        }
    }

    public bool CheckParrySuccess(Node3D attacker)
    {
        if (currentState != ParryState.Parrying) return false;

        // Check if attack came from front
        var character = GetParent<CharacterBody3D>();
        var toAttacker = (attacker.GlobalPosition - character.GlobalPosition).Normalized();
        var facingDirection = -character.GlobalTransform.basis.z;

        if (toAttacker.Dot(facingDirection) > 0.5f) // Attacker is in front
        {
            SucceedParry(attacker);
            return true;
        }

        return false;
    }

    private void SucceedParry(Node3D attacker)
    {
        currentState = ParryState.Cooldown;
        stateTimer = ParryCooldown;

        // Trigger parry dash
        var dashComponent = GetNode<DashComponent>("../DashComponent");
        var parryDirection = (attacker.GlobalPosition - GetParent<CharacterBody3D>().GlobalPosition).Normalized();
        parryDirection.y = 0.2f; // Slight upward angle

        dashComponent?.TryParryDash(parryDirection);

        EmitSignal(SignalName.ParrySuccess, attacker);
        ApplyParrySuccessEffects(attacker);
    }

    private void FailParry()
    {
        currentState = ParryState.Cooldown;
        stateTimer = ParryCooldown;

        EmitSignal(SignalName.ParryFailed);
    }

    private void ApplyParryEffects()
    {
        // Visual feedback (glow, particles, etc.)
        var mesh = GetParent<CharacterBody3D>().GetNode<MeshInstance3D>("MeshInstance3D");
        // Add parry glow effect
    }

    private void ApplyParrySuccessEffects(Node3D attacker)
    {
        // Screen effects, sound, camera shake
        var camera = GetViewport().GetCamera3D();
        camera.AddTrauma(0.8f);

        // Stun attacker briefly
        if (attacker.HasMethod("ApplyStun"))
        {
            attacker.Call("ApplyStun", 0.5f);
        }
    }
}
```

## 3. Character-Specific Movement Abilities

### Example Character: Abrams - Charge Mechanics
```csharp
public partial class AbramsMovementComponent : Node
{
    [Export] public float ChargeSpeed { get; set; } = 18f;
    [Export] public float ChargeDuration { get; set; } = 2f;
    [Export] public float ChargeCooldown { get; set; } = 8f;
    [Export] public float ChargeStaminaCost { get; set; } = 50f;

    private bool isCharging = false;
    private float chargeTimer = 0f;
    private Vector3 chargeDirection = Vector3.Zero;

    public bool TryStartCharge(Vector3 direction)
    {
        if (isCharging) return false;

        var staminaComponent = GetNode<StaminaComponent>("../StaminaComponent");
        if (staminaComponent != null && !staminaComponent.TryConsumeStamina(ChargeStaminaCost))
        {
            return false;
        }

        StartCharge(direction);
        return true;
    }

    private void StartCharge(Vector3 direction)
    {
        isCharging = true;
        chargeTimer = ChargeDuration;
        chargeDirection = direction.Normalized();

        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent.SetVelocity(chargeDirection * ChargeSpeed);

        // Disable other movement during charge
        movementComponent.SetMovementEnabled(false);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (isCharging)
        {
            chargeTimer -= delta;

            if (chargeTimer <= 0)
            {
                EndCharge();
            }
            else
            {
                // Maintain charge velocity
                var movementComponent = GetNode<MovementComponent>("../MovementComponent");
                movementComponent.SetVelocity(chargeDirection * ChargeSpeed);
            }
        }
    }

    private void EndCharge()
    {
        isCharging = false;

        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent.SetMovementEnabled(true);

        // Apply end-of-charge effects
        var camera = GetViewport().GetCamera3D();
        camera.AddTrauma(0.6f);
    }
}
```

### Example Character: Infernus - Flame Dash
```csharp
public partial class InfernusMovementComponent : Node
{
    [Export] public float FlameDashSpeed { get; set; } = 22f;
    [Export] public float FlameDashDuration { get; set; } = 0.8f;
    [Export] public float FlameTrailDamage { get; set; } = 20f;

    private bool isFlameDashing = false;
    private float flameDashTimer = 0f;
    private Vector3 flameDashDirection = Vector3.Zero;

    public bool TryFlameDash(Vector3 direction)
    {
        if (isFlameDashing) return false;

        StartFlameDash(direction);
        return true;
    }

    private void StartFlameDash(Vector3 direction)
    {
        isFlameDashing = true;
        flameDashTimer = FlameDashDuration;
        flameDashDirection = direction.Normalized();

        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent.SetVelocity(flameDashDirection * FlameDashSpeed);

        // Create flame trail
        StartFlameTrail();
    }

    private void StartFlameTrail()
    {
        var flameTrailTimer = new Timer();
        flameTrailTimer.WaitTime = 0.1f;
        flameTrailTimer.OneShot = false;
        flameTrailTimer.Timeout += CreateFlameTrailSegment;
        AddChild(flameTrailTimer);
        flameTrailTimer.Start();
    }

    private void CreateFlameTrailSegment()
    {
        if (!isFlameDashing) return;

        var character = GetParent<CharacterBody3D>();
        var flameSegment = flameTrailScene.Instantiate<GpuParticles3D>();
        GetParent().AddChild(flameSegment);
        flameSegment.GlobalPosition = character.GlobalPosition;

        // Check for damage in flame area
        var damageQuery = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = 2f },
            Transform = new Transform3D(Basis.Identity, character.GlobalPosition),
            CollideWithAreas = false,
            CollideWithBodies = true
        };

        var results = character.GetWorld3D().DirectSpaceState.IntersectShape(damageQuery);
        foreach (var result in results)
        {
            var collider = (Node3D)result["collider"];
            if (collider != character && collider.HasMethod("TakeDamage"))
            {
                collider.Call("TakeDamage", FlameTrailDamage);
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (isFlameDashing)
        {
            flameDashTimer -= delta;

            if (flameDashTimer <= 0)
            {
                EndFlameDash();
            }
            else
            {
                var movementComponent = GetNode<MovementComponent>("../MovementComponent");
                movementComponent.SetVelocity(flameDashDirection * FlameDashSpeed);
            }
        }
    }

    private void EndFlameDash()
    {
        isFlameDashing = false;

        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent.SetVelocity(Vector3.Zero);
    }
}
```

## 4. Status Effect Integration

### Movement Status Effects
```csharp
public partial class StatusEffectComponent : Node
{
    private Dictionary<string, StatusEffect> activeEffects = new();

    public void ApplyMovementEffect(string effectName, float duration, float intensity)
    {
        var effect = new StatusEffect
        {
            name = effectName,
            duration = duration,
            intensity = intensity
        };

        activeEffects[effectName] = effect;

        switch (effectName)
        {
            case "MovementSlow":
                ApplyMovementSlow(intensity);
                break;
            case "Stun":
                ApplyStun(duration);
                break;
            case "Displace":
                ApplyDisplacement(effect);
                break;
        }
    }

    private void ApplyMovementSlow(float intensity)
    {
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent?.ApplySpeedMultiplier(1f - intensity);
    }

    private void ApplyStun(float duration)
    {
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");
        movementComponent?.Stun(duration);
    }

    private void ApplyDisplacement(StatusEffect effect)
    {
        var character = GetParent<CharacterBody3D>();
        var movementComponent = GetNode<MovementComponent>("../MovementComponent");

        // Apply displacement force
        var displacementForce = effect.displacementVector * effect.intensity;
        movementComponent?.SetVelocity(displacementForce);
    }

    public override void _Process(double delta)
    {
        // Update and remove expired effects
        var expiredEffects = activeEffects
            .Where(kvp => kvp.Value.duration <= 0)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var effectName in expiredEffects)
        {
            RemoveEffect(effectName);
        }

        // Update effect durations
        foreach (var effect in activeEffects.Values)
        {
            effect.duration -= delta;
        }
    }

    private void RemoveEffect(string effectName)
    {
        if (activeEffects.TryGetValue(effectName, out var effect))
        {
            // Remove effect modifications
            switch (effectName)
            {
                case "MovementSlow":
                    var movementComponent = GetNode<MovementComponent>("../MovementComponent");
                    movementComponent?.RemoveSpeedMultiplier();
                    break;
                case "Stun":
                    var movementComponent2 = GetNode<MovementComponent>("../MovementComponent");
                    movementComponent2?.RemoveStun();
                    break;
            }

            activeEffects.Remove(effectName);
        }
    }

    private struct StatusEffect
    {
        public string name;
        public float duration;
        public float intensity;
        public Vector3 displacementVector;
    }
}
```

## 5. Ability Integration System

### Ability Movement Controller
```csharp
public partial class AbilityMovementController : Node
{
    private HeavyMeleeComponent heavyMeleeComponent;
    private ParryComponent parryComponent;
    private Dictionary<string, CharacterAbility> abilities = new();

    public override void _Ready()
    {
        heavyMeleeComponent = GetNode<HeavyMeleeComponent>("../HeavyMeleeComponent");
        parryComponent = GetNode<ParryComponent>("../ParryComponent");

        // Register character-specific abilities
        RegisterAbilities();
    }

    public override void _PhysicsProcess(double delta)
    {
        // Handle ability inputs
        if (Input.IsActionJustPressed("heavy_melee"))
        {
            var inputDirection = GetInputDirection();
            heavyMeleeComponent?.TryHeavyMelee(inputDirection);
        }

        if (Input.IsActionJustPressed("parry"))
        {
            parryComponent?.TryParry();
        }

        // Handle character-specific abilities
        foreach (var ability in abilities.Values)
        {
            if (Input.IsActionJustPressed(ability.inputAction) && ability.CanUse())
            {
                ability.Execute();
            }
        }
    }

    private void RegisterAbilities()
    {
        // Register character-specific abilities based on character type
        var characterName = GetParent<CharacterBody3D>().Name;

        switch (characterName)
        {
            case "Abrams":
                abilities["charge"] = new AbramsChargeAbility(this);
                break;
            case "Infernus":
                abilities["flame_dash"] = new InfernusFlameDashAbility(this);
                break;
        }
    }

    private Vector3 GetInputDirection()
    {
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        return new Vector3(input.x, 0, input.y).Rotated(Vector3.Up, GetParent<CharacterBody3D>().GlobalRotation.y);
    }
}
```

## 6. Configuration Values

### Recommended Starting Values
```csharp
// HeavyMeleeComponent
MeleeRange = 2.5f;
MeleeDamage = 75f;
MeleeForce = 15f;
MeleeCooldown = 1.5f;
MeleeWindup = 0.3f;
MeleeRecovery = 0.4f;

// ParryComponent
ParryWindow = 0.25f;
ParryStaminaCost = 30f;
ParryCooldown = 2f;
ParryDashSpeed = 20f;

// Character-specific values
// Abrams
ChargeSpeed = 18f;
ChargeDuration = 2f;
ChargeCooldown = 8f;
ChargeStaminaCost = 50f;

// Infernus
FlameDashSpeed = 22f;
FlameDashDuration = 0.8f;
FlameTrailDamage = 20f;
```

## 7. Testing Checklist

- [ ] Heavy melee attacks connect with targets in range
- [ ] Melee cancels allow for movement during windup
- [ ] Parry successfully reflects attacks from the front
- [ ] Parry dash provides defensive mobility
- [ ] Character-specific abilities integrate smoothly with core movement
- [ ] Status effects properly modify movement capabilities
- [ ] Visual and audio feedback enhance ability feel
- [ ] Ability costs balance power with resource management

## 8. Balance Considerations

### Ability Design Principles
- **Risk vs Reward**: Powerful abilities should have clear counters
- **Resource Management**: Stamina costs prevent ability spam
- **Movement Integration**: Abilities should enhance, not replace, core movement
- **Visual Clarity**: Clear telegraphing for reaction-based gameplay
- **Combo Potential**: Abilities that chain with movement techniques

This character-specific system creates unique movement identities for different characters while maintaining the core Deadlock movement philosophy of skill-based, fluid locomotion.


