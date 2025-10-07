using Godot;
using Waterjam.Core.Systems.Console;
using Waterjam.Core.Services;
using Waterjam.Domain;
using Waterjam.Events;

namespace Waterjam.Game;

/// <summary>
/// Simple third-person player entity built as a box, with classic centered camera.
/// Inherits CharacterEntity to participate in domain events and damage.
/// Supports network synchronization for multiplayer.
/// </summary>
public partial class PlayerEntity : CharacterEntity
{
    [Export]
    public PlayerParams Params { get; set; }

    private Vector3 velocity;
    private float yawDegrees;
    private float pitchDegrees;

    // Network synchronization
    private bool _isNetworked;
    private long _networkAuthority;
    private Vector3 _lastSyncedPosition;
    private Vector3 _lastSyncedVelocity;
    private float _syncTimer;
    private const float SYNC_INTERVAL = 0.1f; // Sync every 100ms

    public override void _Ready()
    {
        base._Ready();
        Health = MaxHealth = 100f;
        if (CharacterBody == null)
        {
            ConsoleSystem.LogErr("PlayerEntity requires a CharacterBody3D child.", ConsoleChannel.Error);
        }

        // Check if this is a networked player
        _networkAuthority = GetMultiplayerAuthority();
        _isNetworked = _networkAuthority != GetTree().GetMultiplayer().GetUniqueId();

        // Only capture mouse for local player
        if (!_isNetworked)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        // Initialize yaw/pitch from current nodes to avoid sudden offsets
        if (CharacterBody != null)
            yawDegrees = CharacterBody.RotationDegrees.Y;
        var springArmInit = CharacterBody?.GetNodeOrNull<Node3D>("SpringArm3D");
        if (springArmInit != null)
            pitchDegrees = springArmInit.RotationDegrees.X;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (CharacterBody == null) return;

        // Handle network synchronization
        _syncTimer += (float)delta;

        if (_isNetworked)
        {
            // This is a networked player - interpolate towards synced position
            InterpolateNetworkedMovement(delta);
        }
        else
        {
            // This is the local player - handle input and sync to network
            var inputDir = GetMovementInput();
            var moveSpeed = Params?.MoveSpeed ?? 6.0f;
            var sprintMul = Params?.SprintMultiplier ?? 1.5f;
            var wishSpeed = moveSpeed * (Input.IsActionPressed("sprint") ? sprintMul : 1f);
            var basisForward = -CharacterBody.GlobalTransform.Basis.Z; // Character forward (-Z)
            var basisRight = CharacterBody.GlobalTransform.Basis.X;

            var move = (basisForward * inputDir.Z + basisRight * inputDir.X).Normalized();
            var horizontalVel = move * wishSpeed;

            var gravity = Params?.Gravity ?? ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
            if (!CharacterBody.IsOnFloor())
                velocity.Y -= gravity * (float)delta;

            var jumpVel = Params?.JumpVelocity ?? 4.5f;
            if (CharacterBody.IsOnFloor() && Input.IsActionJustPressed("jump"))
                velocity.Y = jumpVel;

            velocity.X = horizontalVel.X;
            velocity.Z = horizontalVel.Z;

            CharacterBody.Velocity = velocity;
            CharacterBody.MoveAndSlide();
            velocity = CharacterBody.Velocity;

            // Sync movement to network if it's time
            if (_syncTimer >= SYNC_INTERVAL)
            {
                SyncMovementToNetwork();
                _syncTimer = 0f;
            }
        }
    }

    private void InterpolateNetworkedMovement(double delta)
    {
        // For networked players, we would interpolate towards the synced position
        // This is a simplified version - in a real implementation, you'd use proper interpolation

        // For now, just ensure the character body follows the synced position
        if (CharacterBody != null)
        {
            // This would be replaced with proper interpolation logic
            // CharacterBody.GlobalPosition = Vector3.Lerp(CharacterBody.GlobalPosition, _lastSyncedPosition, (float)delta * 10f);
        }
    }

    private void SyncMovementToNetwork()
    {
        if (CharacterBody == null) return;

        var currentPos = CharacterBody.GlobalPosition;
        var currentVel = CharacterBody.Velocity;

        // Only sync if there's been significant movement
        if (currentPos.DistanceTo(_lastSyncedPosition) > 0.1f || currentVel.DistanceTo(_lastSyncedVelocity) > 0.1f)
        {
            // Send movement update to server/clients
            var networkService = GetNodeOrNull<NetworkService>("/root/NetworkService");
            if (networkService != null && networkService.IsClient)
            {
                // This would send input to server for server-authoritative movement
                // networkService.SendPlayerMovement(currentPos, currentVel);
            }

            _lastSyncedPosition = currentPos;
            _lastSyncedVelocity = currentVel;
        }
    }

    /// <summary>
    /// Sets the network authority for this player entity.
    /// </summary>
    public void SetNetworkAuthority(long authorityPeerId)
    {
        _networkAuthority = authorityPeerId;
        _isNetworked = authorityPeerId != GetTree().GetMultiplayer().GetUniqueId();

        if (_isNetworked)
        {
            ConsoleSystem.Log($"Player entity set as networked with authority from peer {authorityPeerId}", ConsoleChannel.Network);
        }
        else
        {
            ConsoleSystem.Log("Player entity set as local player", ConsoleChannel.Network);
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Only handle input for the local player
        if (_isNetworked) return;

        if (@event is InputEventMouseMotion motion)
        {
            var sens = Params?.MouseSensitivity ?? 0.1f;
            var invertY = Params?.InvertY ?? false;
            yawDegrees -= motion.Relative.X * sens;
            pitchDegrees += (invertY ? motion.Relative.Y : -motion.Relative.Y) * sens;

            var minPitch = Params?.MinPitchDeg ?? -60f;
            var maxPitch = Params?.MaxPitchDeg ?? 80f;
            pitchDegrees = Mathf.Clamp(pitchDegrees, minPitch, maxPitch);

            if (CharacterBody != null)
                CharacterBody.RotationDegrees = new Vector3(0f, yawDegrees, 0f);

            var springArm = CharacterBody?.GetNodeOrNull<Node3D>("SpringArm3D");
            if (springArm != null)
            {
                var springRot = springArm.RotationDegrees;
                springRot.X = pitchDegrees;
                springArm.RotationDegrees = springRot;
            }
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (@event.IsActionPressed("use"))
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    private static Vector3 GetMovementInput()
    {
        float x = 0f;
        float z = 0f;
        if (Input.IsActionPressed("move_left")) x -= 1f;
        if (Input.IsActionPressed("move_right")) x += 1f;
        if (Input.IsActionPressed("move_forward")) z += 1f;
        if (Input.IsActionPressed("move_back")) z -= 1f;
        var v = new Vector3(x, 0, z);
        return v.Length() > 1f ? v.Normalized() : v;
    }
}


