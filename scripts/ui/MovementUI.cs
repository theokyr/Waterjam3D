using Godot;
using System;

public partial class MovementUI : Control
{
    private Label speedLabel;
    private ProgressBar staminaBar;
    private CharacterBody3D targetCharacter;

    public override void _Ready()
    {
        speedLabel = GetNode<Label>("SpeedLabel");
        staminaBar = GetNode<ProgressBar>("StaminaBar");
    }

    public override void _Process(double delta)
    {
        if (targetCharacter != null)
        {
            UpdateSpeedDisplay();
            UpdateStaminaDisplay();
        }
    }

    public void SetTargetCharacter(CharacterBody3D character)
    {
        targetCharacter = character;
    }

    private void UpdateSpeedDisplay()
    {
        if (targetCharacter == null || speedLabel == null) return;

        var velocity = targetCharacter.Velocity;
        var horizontalSpeed = new Vector3(velocity[0], 0, velocity[2]).Length();
        var speedMS = horizontalSpeed; // Already in m/s since we're using Godot units

        speedLabel.Text = $"Speed: {speedMS:F2} m/s";
    }

    private void UpdateStaminaDisplay()
    {
        if (targetCharacter == null || staminaBar == null) return;

        var staminaComponent = targetCharacter.GetNode<StaminaComponent>("StaminaComponent");
        if (staminaComponent != null)
        {
            staminaBar.Value = staminaComponent.GetStaminaPercentage() * 100f;
        }
    }
}
