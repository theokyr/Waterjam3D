using Godot;
using WorldGame.Game.Services;

namespace WorldGame.Domain;

/// <summary>
/// Attribute to mark a node as networked.
/// Entities with this attribute will be automatically registered for replication.
/// </summary>
[GlobalClass]
public partial class NetworkedEntity : Node3D
{
    /// <summary>
    /// Entity type identifier (used to spawn correct prefab on clients)
    /// </summary>
    [Export] public string EntityType { get; set; } = "generic";
    
    /// <summary>
    /// Whether this entity is replicated across the network
    /// </summary>
    [Export] public bool IsReplicated { get; set; } = true;
    
    /// <summary>
    /// Network ID assigned by server (0 = not assigned yet)
    /// </summary>
    public ulong NetworkId { get; private set; }
    
    /// <summary>
    /// Whether this entity has authority (server or owning client)
    /// </summary>
    public bool HasAuthority => NetworkId == 0 || GetNetworkService()?.IsServer == true;
    
    private NetworkReplicationService _replicationService;
    
    public override void _Ready()
    {
        if (!IsReplicated) return;
        
        _replicationService = GetNodeOrNull<NetworkReplicationService>("/root/NetworkReplicationService");
        
        // Only server registers entities for replication
        var networkService = GetNetworkService();
        if (networkService != null && networkService.IsServer)
        {
            NetworkId = _replicationService?.RegisterEntity(this, EntityType, GlobalPosition) ?? 0;
        }
    }
    
    public override void _Process(double delta)
    {
        if (!IsReplicated || !HasAuthority) return;
        
        // Update position on server if it changed
        if (_replicationService != null && NetworkId > 0)
        {
            _replicationService.UpdateEntityPosition(NetworkId, GlobalPosition, GlobalRotation);
        }
    }
    
    public override void _ExitTree()
    {
        if (!IsReplicated || NetworkId == 0) return;
        
        var networkService = GetNetworkService();
        if (networkService != null && networkService.IsServer)
        {
            _replicationService?.UnregisterEntity(NetworkId);
        }
    }
    
    /// <summary>
    /// Update a component on this entity
    /// </summary>
    public void UpdateComponent(string componentName, System.Collections.Generic.Dictionary<string, object> data)
    {
        if (!HasAuthority || NetworkId == 0) return;
        
        _replicationService?.UpdateEntityComponent(NetworkId, componentName, data);
    }
    
    private NetworkService GetNetworkService()
    {
        return GetNodeOrNull<NetworkService>("/root/NetworkService");
    }
}

