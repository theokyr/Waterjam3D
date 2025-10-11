using Godot;

public partial class ThirdPersonCamera : Camera3D
{
    [Export] public float Distance { get; set; } = 4.5f;
    [Export] public float Height { get; set; } = 1.8f;
    [Export] public float Damping { get; set; } = 10f; // Used only if UseSmoothing
    [Export] public float MouseSensitivity { get; set; } = 0.12f;
    [Export] public float MinPitchDeg { get; set; } = -60f;
    [Export] public float MaxPitchDeg { get; set; } = 80f;
    [Export] public bool InvertY { get; set; } = false;
    [Export] public bool UseSmoothing { get; set; } = false;
    [Export] public float CollisionPadding { get; set; } = 0.2f;

    private float _yawDeg;
    private float _pitchDeg = 10f;
    private bool _inputCaptured = true;

    public override void _Ready()
    {
        var parent3D = GetParent() as Node3D;
        if (parent3D != null)
        {
            _yawDeg = parent3D.RotationDegrees.Y;
        }
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            _inputCaptured = !_inputCaptured;
            Input.MouseMode = _inputCaptured ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        }

        if (!_inputCaptured) return;

        if (@event is InputEventMouseMotion motion)
        {
            _yawDeg -= motion.Relative.X * MouseSensitivity;
            var ySign = InvertY ? -1f : 1f;
            _pitchDeg += motion.Relative.Y * MouseSensitivity * ySign;
            _pitchDeg = Mathf.Clamp(_pitchDeg, MinPitchDeg, MaxPitchDeg);
        }
    }

    public override void _Process(double delta)
    {
        var target3D = GetParent() as Node3D;
        if (target3D == null)
        {
            return;
        }

        var targetPos = target3D.GlobalPosition + Vector3.Up * Height;

        var yawRad = Mathf.DegToRad(_yawDeg);
        var pitchRad = Mathf.DegToRad(_pitchDeg);

        var yawBasis = new Basis(Vector3.Up, yawRad);
        var forward = (yawBasis * Vector3.Forward).Rotated(Vector3.Right, pitchRad).Normalized();
        var desiredPos = targetPos - forward * Distance;

        // Collision handling: ray from target to desired position
        var parentBody = target3D as CollisionObject3D;
        var exclude = new Godot.Collections.Array<Rid>();
        if (parentBody != null)
        {
            exclude.Add(parentBody.GetRid());
        }
        var ray = new PhysicsRayQueryParameters3D
        {
            From = targetPos,
            To = desiredPos,
            CollideWithBodies = true,
            CollideWithAreas = true,
            Exclude = exclude
        };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (hit.Count > 0)
        {
            var hitPos = (Vector3)hit["position"];
            var hitNormal = (Vector3)hit["normal"];
            desiredPos = hitPos + hitNormal * CollisionPadding;
        }

        if (UseSmoothing)
        {
            GlobalPosition = GlobalPosition.Lerp(desiredPos, Damping * (float)delta);
        }
        else
        {
            GlobalPosition = desiredPos;
        }
        LookAt(targetPos, Vector3.Up);
    }

    public float GetYawDegrees()
    {
        return _yawDeg;
    }
}
