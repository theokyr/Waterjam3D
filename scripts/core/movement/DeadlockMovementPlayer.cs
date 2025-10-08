using Godot;
using System;
using Waterjam.Core.Systems.Console;

public partial class DeadlockMovementPlayer : CharacterBody3D
{
    // Core movement components
    private MovementComponent movementComponent;
    private StaminaComponent staminaComponent;
    private InputBuffer inputBuffer;

    // Advanced movement components
    private DashComponent dashComponent;
    private SlideComponent slideComponent;
    private WallJumpComponent wallJumpComponent;
    private MantleComponent mantleComponent;

    // Camera and effects
    private ThirdPersonCamera camera;

    // Configuration
    [Export] public float MouseSensitivity { get; set; } = 0.3f;
    [Export] public bool LockMouse { get; set; } = true;

    public override void _Ready()
    {
        // Get component references
        movementComponent = GetNode<MovementComponent>("MovementComponent");
        staminaComponent = GetNode<StaminaComponent>("StaminaComponent");
        inputBuffer = GetNode<InputBuffer>("InputBuffer");
        dashComponent = GetNode<DashComponent>("DashComponent");
        slideComponent = GetNode<SlideComponent>("SlideComponent");
        wallJumpComponent = GetNode<WallJumpComponent>("WallJumpComponent");
        mantleComponent = GetNode<MantleComponent>("MantleComponent");
        camera = GetNode<ThirdPersonCamera>("Camera3D");

        // Setup input and mouse
        if (LockMouse)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        // Connect signals
        ConnectComponentSignals();

        // Setup UI
        SetupUI();

        ConsoleSystem.Log("DeadlockMovementPlayer initialized with advanced movement system", ConsoleChannel.Player);
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
    }

    public override void _PhysicsProcess(double delta)
    {
        // Handle input buffering
        HandleInputBuffering();

        // Handle movement inputs
        HandleMovementInputs();

        // Update camera
        UpdateCamera();
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
        // Handle jump with buffer
        if (inputBuffer.IsBuffered("jump"))
        {
            if (wallJumpComponent.TryWallJump())
            {
                // Wall jump successful
            }
            else
            {
                movementComponent.HandleJump();
            }
        }

        // Handle dash
        if (Input.IsActionJustPressed("dash"))
        {
            var inputDirection = GetInputDirection();
            dashComponent.TryDash(inputDirection);
        }

        // Handle slide
        if (Input.IsActionJustPressed("slide") && IsOnFloor())
        {
            slideComponent.TryStartSlide();
        }

        // Handle mantle
        if (Input.IsActionJustPressed("mantle"))
        {
            mantleComponent.TryMantle();
        }

        // Handle sprint stamina consumption
        if (Input.IsActionPressed("sprint") && IsOnFloor())
        {
            if (!staminaComponent.TryConsumeSprint((float)GetPhysicsProcessDeltaTime()))
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
        RotateY(-mouseMotion.Relative[0] * MouseSensitivity * 0.01f);

        // Rotate camera vertically (clamped)
        camera.RotateX(-mouseMotion.Relative[1] * MouseSensitivity * 0.01f);
        camera.Rotation = new Vector3(
            Mathf.Clamp(camera.Rotation[0], -Mathf.Pi / 2f, Mathf.Pi / 2f),
            camera.Rotation[1],
            camera.Rotation[2]
        );
    }

    private Vector3 GetInputDirection()
    {
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        return new Vector3(input[0], 0, input[1]).Rotated(Vector3.Up, GlobalRotation[1]);
    }

    private void UpdateCamera()
    {
        camera.TargetPosition = GlobalPosition + Vector3.Up * 2f;
    }

    // Signal handlers
    private void OnStaminaChanged(float current, float max)
    {
        ConsoleSystem.Log($"Stamina: {current:F1}/{max:F1}", ConsoleChannel.Player);
    }

    private void OnStaminaDepleted()
    {
        ConsoleSystem.Log("Stamina depleted!", ConsoleChannel.Player);
    }

    private void OnDashStarted(Vector3 direction)
    {
        ConsoleSystem.Log($"Dash started in direction: {direction}", ConsoleChannel.Player);
    }

    private void OnDashEnded()
    {
        ConsoleSystem.Log("Dash ended", ConsoleChannel.Player);
    }

    private void OnSlideStarted()
    {
        ConsoleSystem.Log("Slide started", ConsoleChannel.Player);
    }

    private void OnSlideEnded()
    {
        ConsoleSystem.Log("Slide ended", ConsoleChannel.Player);
    }

    private void OnWallJumped(Vector3 direction)
    {
        ConsoleSystem.Log($"Wall jump in direction: {direction}", ConsoleChannel.Player);
    }

    private void OnMantleStarted(Vector3 target)
    {
        ConsoleSystem.Log($"Mantle started to: {target}", ConsoleChannel.Player);
    }

    private void OnMantleCompleted()
    {
        ConsoleSystem.Log("Mantle completed", ConsoleChannel.Player);
    }

    private void SetupUI()
    {
        // Find and setup the UI
        var ui = GetParent().GetNode("MovementUI");
        if (ui is Control movementUI)
        {
            var movementUIScript = movementUI.GetNode("MovementUI") as MovementUI;
            if (movementUIScript != null)
            {
                movementUIScript.SetTargetCharacter(this);
            }
        }
    }
}
