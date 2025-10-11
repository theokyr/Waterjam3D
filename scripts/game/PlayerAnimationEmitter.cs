using Godot;
using Waterjam.Events;

namespace Waterjam.Game;

public partial class PlayerAnimationEmitter : Node3D
{
    [Export]
    public string ActorId { get; set; } = "player";

    [Export]
    public NodePath ControllerPath { get; set; }

    [Export]
    public float RunSpeedThreshold { get; set; } = 0.2f;

    [Export]
    public bool MirrorByMovementX { get; set; } = true;

    [Export]
    public bool FlipUsingInputFirst { get; set; } = true;

    [Export]
    public float FlipInputEpsilon { get; set; } = 0.1f;

    [Export]
    public float LandOneShotWindow { get; set; } = 0.12f;

    [Export]
    public float AscendThreshold { get; set; } = 1.5f;

    [Export]
    public float DescentThreshold { get; set; } = -1.5f;

    private CharacterBody3D _controller;
    private Camera3D _camera;
    private string _currentState;
    private bool _wasGrounded;
    private float _lastLandTime;
    private bool? _lastFlip;

    public override void _Ready()
    {
        _controller = GetNodeOrNull<CharacterBody3D>(ControllerPath) ?? GetParent() as CharacterBody3D;
        _camera = GetTree().CurrentScene?.GetNodeOrNull<Camera3D>("Player/SpringArm3D/Camera3D")
                  ?? _controller?.GetNodeOrNull<Camera3D>("SpringArm3D/Camera3D");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_controller == null) return;

        var vel = _controller.Velocity;
        var hSpeed = new Vector3(vel[0], 0, vel[2]).Length();
        var grounded = _controller.IsOnFloor();

        string next = _currentState;

        // Basic state selection; can be replaced by richer movement system later
        bool dashFlag = _controller.HasMeta("anim_dash") && (bool)_controller.GetMeta("anim_dash");
        bool slideFlag = _controller.HasMeta("anim_slide") && (bool)_controller.GetMeta("anim_slide");
        float wallJumpTime = _controller.HasMeta("anim_wall_jump_time") ? (float)_controller.GetMeta("anim_wall_jump_time") : -999f;
        float now = (float)Time.GetTicksMsec() * 0.001f;

        // recent wall jump one-shot
        if (!grounded && now - wallJumpTime <= 0.25f)
            next = "wall_jump";
        else if (slideFlag)
            next = "slide";
        else if (dashFlag)
            next = "dash";
        else if (grounded)
        {
            // Land one-shot when touching ground after air
            if (!_wasGrounded && now - _lastLandTime > LandOneShotWindow)
            {
                next = "land";
                _lastLandTime = now;
            }
            else
            {
                next = hSpeed > RunSpeedThreshold ? "run" : "idle";
            }
        }
        else
        {
            if (vel[1] > AscendThreshold) next = "ascend";
            else if (vel[1] < DescentThreshold) next = "descent";
            else next = "in_air";
        }

        // Compute desired flip (left/right) relative to camera
        bool? flip = null;
        if (MirrorByMovementX)
        {
            Vector3 right = _camera != null ? _camera.GlobalTransform.Basis.X : Vector3.Right;
            Vector3 flatRight = new Vector3(right[0], 0, right[2]).Normalized();

            Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
            Vector3 flipDir = Vector3.Zero;
            if (FlipUsingInputFirst && (Mathf.Abs(input[0]) > FlipInputEpsilon || Mathf.Abs(input[1]) > FlipInputEpsilon))
            {
                // Build camera-relative input direction
                Vector3 forward = _camera != null ? -_camera.GlobalTransform.Basis.Z : Vector3.Forward;
                Vector3 flatForward = new Vector3(forward[0], 0, forward[2]).Normalized();
                Vector3 flatRight2 = flatRight; // already normalized
                flipDir = (flatRight2 * input[0] + flatForward * (-input[1])).Normalized();
            }
            else
            {
                flipDir = new Vector3(vel[0], 0, vel[2]);
            }

            if (flipDir.Length() > 0.001f)
                flip = flipDir.Dot(flatRight) < 0.0f; // moving left of camera-right => flipX
        }

        bool stateChanged = next != _currentState && !string.IsNullOrEmpty(next);
        bool flipChanged = flip.HasValue && (!_lastFlip.HasValue || _lastFlip.Value != flip.Value);

        if (stateChanged || flipChanged)
        {
            _currentState = next;
            _lastFlip = flip ?? _lastFlip;
            GameEvent.DispatchGlobal(new AnimationChangedEvent(ActorId, _currentState, true, flip ?? _lastFlip));
        }

        _wasGrounded = grounded;
    }
}


