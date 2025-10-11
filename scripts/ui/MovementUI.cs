using Godot;
using System;
using Waterjam.UI.Components;

public partial class MovementUI : Control
{
    private Label speedLabel;
    private CharacterBody3D targetCharacter;
    private StaminaWheel staminaWheel;
    private StaminaComponent cachedStamina;
    private bool staminaSourceBound;

    public override void _Ready()
    {
        speedLabel = GetNode<Label>("SpeedLabel");
        staminaWheel = GetNodeOrNull<StaminaWheel>("CrosshairAnchor/StaminaWheel");
    }

    public override void _Process(double delta)
    {
        if (targetCharacter == null)
        {
            // Try auto-bind common player path in dev scenes
            var scene = GetTree().CurrentScene;
            var auto = scene?.GetNodeOrNull<CharacterBody3D>("Player");
            if (auto != null) SetTargetCharacter(auto);
        }
        UpdateSpeedDisplay();
        UpdateStaminaDisplay();
    }

    public void SetTargetCharacter(CharacterBody3D character)
    {
        targetCharacter = character;
        cachedStamina = null; // reset cache on new target
        staminaSourceBound = false;
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
        if (targetCharacter == null || staminaWheel == null) return;

        if (cachedStamina == null)
        {
            // Support both architectures
            cachedStamina = targetCharacter.GetNodeOrNull<StaminaComponent>("Components/StaminaComponent")
                            ?? targetCharacter.GetNodeOrNull<StaminaComponent>("StaminaComponent");
        }

        if (!staminaSourceBound)
        {
            if (cachedStamina != null)
            {
                staminaWheel.SetSource(cachedStamina);
                staminaSourceBound = true;
            }
            else if (targetCharacter is IStaminaSource)
            {
                staminaWheel.SetSource(targetCharacter);
                staminaSourceBound = true;
            }
        }
    }
}


