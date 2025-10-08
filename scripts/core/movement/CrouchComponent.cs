using Godot;
using System;

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
    }

    public void TryUncrouch()
    {
        if (!isCrouching || !canUncrouch) return;

        if (CheckUncrouchSpace())
        {
            isCrouching = false;
            AnimateCrouch(StandingHeight);
        }
    }

    private bool CheckCrouchSpace()
    {
        // Cast ray upward to check for overhead clearance
        var character = GetParent<CharacterBody3D>();
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
        var character = GetParent<CharacterBody3D>();
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

    public void ForceCrouch()
    {
        if (!isCrouching)
        {
            isCrouching = true;
            AnimateCrouch(CrouchHeight);
        }
    }

    public bool IsCrouching()
    {
        return isCrouching;
    }
}
