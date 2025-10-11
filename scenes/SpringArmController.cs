using Godot;
using System;

public partial class SpringArmController : SpringArm3D
{
    [Export]
    public float MouseSensibility { get; set; } = 0.005f;

    // Displayed in degrees in the inspector but stored as radians
    [Export(PropertyHint.Range, "-90,90,0.1,radians_as_degrees")]
    public float MinVerticalAngle { get; set; } = -Mathf.Pi / 2.0f;

    // Displayed in degrees in the inspector but stored as radians
    [Export(PropertyHint.Range, "0,90,0.1,radians_as_degrees")]
    public float MaxVerticalAngle { get; set; } = Mathf.Pi / 4.0f;

    public override void _Ready()
    {
        Input.SetMouseMode(Input.MouseModeEnum.Captured);
    }

    public override void _Input(InputEvent @event)
    {
        // Mouse look when the mouse is captured
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            var currentRotation = Rotation;

            currentRotation.Y -= motion.Relative.X * MouseSensibility;
            currentRotation.Y = Mathf.Wrap(currentRotation.Y, 0.0f, Mathf.Tau);

            currentRotation.X -= motion.Relative.Y * MouseSensibility;
            currentRotation.X = Mathf.Clamp(currentRotation.X, MinVerticalAngle, MaxVerticalAngle);

            Rotation = currentRotation;
        }

        // Zoom the spring arm with mouse wheel actions
        if (@event.IsActionPressed("wheel_up"))
        {
            SpringLength -= 1.0f;
        }
        if (@event.IsActionPressed("wheel_down"))
        {
            SpringLength += 1.0f;
        }

        // Toggle mouse capture
        if (@event.IsActionPressed("toggle_mouse_capture"))
        {
            if (Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                Input.SetMouseMode(Input.MouseModeEnum.Visible);
            }
            else
            {
                Input.SetMouseMode(Input.MouseModeEnum.Captured);
            }
        }
    }
}
