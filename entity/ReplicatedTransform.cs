using Godot;
using System.Collections.Generic;

namespace WorldGame.Domain;

/// <summary>
/// Component that replicates transform (position, rotation, scale) across the network.
/// Attach to any Node3D that needs its transform replicated.
/// </summary>
[GlobalClass]
public partial class ReplicatedTransform : Node
{
    [Export] public float UpdateRate { get; set; } = 20.0f; // Updates per second
    [Export] public float PositionThreshold { get; set; } = 0.01f; // Min distance to trigger update
    [Export] public float RotationThreshold { get; set; } = 0.01f; // Min rotation change (radians)
    
    private Node3D _target;
    private NetworkedEntity _networkEntity;
    private Vector3 _lastPosition;
    private Vector3 _lastRotation;
    private float _updateAccumulator;
    
    public override void _Ready()
    {
        _target = GetParent<Node3D>();
        _networkEntity = GetParent<NetworkedEntity>();
        
        if (_target != null)
        {
            _lastPosition = _target.GlobalPosition;
            _lastRotation = _target.GlobalRotation;
        }
    }
    
    public override void _Process(double delta)
    {
        if (_target == null || _networkEntity == null || !_networkEntity.HasAuthority)
            return;
        
        _updateAccumulator += (float)delta;
        
        if (_updateAccumulator >= 1.0f / UpdateRate)
        {
            _updateAccumulator = 0;
            CheckAndUpdate();
        }
    }
    
    private void CheckAndUpdate()
    {
        var posChanged = _target.GlobalPosition.DistanceTo(_lastPosition) > PositionThreshold;
        var rotChanged = (_target.GlobalRotation - _lastRotation).Length() > RotationThreshold;
        
        if (posChanged || rotChanged)
        {
            _lastPosition = _target.GlobalPosition;
            _lastRotation = _target.GlobalRotation;
            
            var data = new Dictionary<string, object>
            {
                ["position"] = _target.GlobalPosition,
                ["rotation"] = _target.GlobalRotation,
                ["scale"] = _target.Scale
            };
            
            _networkEntity.UpdateComponent("Transform", data);
        }
    }
}

