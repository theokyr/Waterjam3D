using Godot;

namespace Waterjam.Game;

/// <summary>
/// Parameters for player movement and physics, authored as a Resource
/// to mimic Unity's ScriptableObject workflow.
/// </summary>
[GlobalClass]
public partial class PlayerParams : Resource
{
    [Export]
    public float MoveSpeed { get; set; } = 6.0f;

    [Export]
    public float SprintMultiplier { get; set; } = 1.5f;

    [Export]
    public float JumpVelocity { get; set; } = 4.5f;

    [Export]
    public float Gravity { get; set; } = 9.81f;

    [Export]
    public float MouseSensitivity { get; set; } = 0.1f;

    [Export]
    public bool InvertY { get; set; } = false;

    [Export]
    public float MinPitchDeg { get; set; } = -60f;

    [Export]
    public float MaxPitchDeg { get; set; } = 80f;
}


