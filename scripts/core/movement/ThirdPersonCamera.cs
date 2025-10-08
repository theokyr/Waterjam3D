using Godot;

public partial class ThirdPersonCamera : Camera3D
{
    [Export] public float Distance { get; set; } = 5f;
    [Export] public float Height { get; set; } = 2f;
    [Export] public float Damping { get; set; } = 5f;
    [Export] public Vector3 TargetPosition { get; set; } = Vector3.Zero;

    private Vector3 velocity = Vector3.Zero;
    private float trauma = 0f;
    private float traumaDecay = 2f;

    public override void _Process(double delta)
    {
        UpdateCameraPosition(delta);
        ApplyTrauma(delta);
    }

    private void UpdateCameraPosition(double delta)
    {
        var forward = -GlobalTransform.Basis.Z.Normalized();
        var targetCameraPosition = TargetPosition - forward * Distance + Vector3.Up * Height;

        // Smooth camera movement
        GlobalPosition = GlobalPosition.Lerp(targetCameraPosition, Damping * (float)delta);

        // Look at target
        LookAt(TargetPosition, Vector3.Up);
    }

    public void AddTrauma(float amount)
    {
        trauma = Mathf.Min(trauma + amount, 1f);
    }

    private void ApplyTrauma(double delta)
    {
        if (trauma > 0)
        {
            // Apply screen shake
            var shake = trauma * trauma;
            var offsetX = (float)GD.RandRange(-shake, shake);
            var offsetY = (float)GD.RandRange(-shake, shake);

            HOffset = offsetX;
            VOffset = offsetY;

            trauma -= traumaDecay * (float)delta;
            if (trauma < 0) trauma = 0;
        }
        else
        {
            HOffset = 0;
            VOffset = 0;
        }
    }
}
