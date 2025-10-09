using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Waterjam.Events;
using Waterjam.Core.Systems.Console;
using Waterjam.Core.Services.Network;
using Newtonsoft.Json;

namespace Waterjam.Core.Services;

/// <summary>
/// Handles replication of entities and components across the network.
/// Server tracks all networked entities and sends snapshots to clients.
/// Clients receive snapshots and update their local entities.
/// </summary>
public partial class NetworkReplicationService : BaseService,
    IGameEventHandler<NetworkServerTickEvent>,
    IGameEventHandler<NetworkClientConnectedEvent>,
    IGameEventHandler<NetworkClientDisconnectedEvent>,
    IGameEventHandler<NetworkConnectedToServerEvent>
{
    private NetworkService _networkService;

    // Server-side state
    private ulong _nextEntityId = 1000; // Start at 1000 to avoid conflicts with local IDs
    private readonly Dictionary<ulong, NetworkEntityState> _entities = new();
    private readonly Dictionary<ulong, HashSet<ulong>> _clientSubscriptions = new(); // client_id -> entity_ids
    private readonly HashSet<ulong> _dirtyEntities = new(); // Entities that changed this tick

    // Client-side state
    private readonly Dictionary<ulong, Node> _clientEntities = new(); // entity_id -> spawned node
    private readonly Queue<SnapshotData> _snapshotBuffer = new();
    private const int SNAPSHOT_BUFFER_SIZE = 5; // Keep last 5 snapshots for interpolation

    // Pass 2: Advanced features
    private readonly SnapshotInterpolator _interpolator = new();
    private readonly BaselineDeltaCompressor _compressor = new();
    private readonly NetworkMetrics _metrics = new();

    // Configuration
    private float _interestRadius = 100.0f; // Replicate entities within 100m
    private int _snapshotInterval = 2; // Send snapshot every N ticks (15 Hz at 30 tick rate)
    private int _ticksSinceSnapshot = 0;

    [Export]
    public bool UseCompression { get; set; } = true;

    [Export]
    public bool UseInterpolation { get; set; } = true;

    public override void _Ready()
    {
        base._Ready();
        _networkService = GetNode<NetworkService>("/root/NetworkService");
        RegisterConsoleCommands();
        ConsoleSystem.Log("[NetworkReplication] Initialized", ConsoleChannel.Network);
    }

    public override void _Process(double delta)
    {
        // Client-side: render interpolated snapshots
        if (_networkService != null && _networkService.IsClient && !_networkService.IsServer && UseInterpolation)
        {
            var interpolated = _interpolator.GetInterpolatedSnapshot();
            if (interpolated != null)
            {
                ApplySnapshotForRendering(interpolated);
            }
        }
    }

    private void ApplySnapshotForRendering(SnapshotData snapshot)
    {
        // Update only visual positions (don't spawn/despawn during interpolation)
        foreach (var entity in snapshot.Entities)
        {
            if (_clientEntities.TryGetValue(entity.EntityId, out var node) && IsInstanceValid(node) && node is Node3D node3D)
            {
                node3D.Position = entity.Position;
                node3D.Rotation = entity.Rotation;
            }
        }
    }

    private void RegisterConsoleCommands()
    {
        var consoleSystem = GetNodeOrNull<ConsoleSystem>("/root/ConsoleSystem");
        if (consoleSystem == null) return;

        consoleSystem.RegisterCommand(new ConsoleCommand(
            "net_spawn_test",
            "Spawn a test entity for replication testing",
            "net_spawn_test [x] [y] [z]",
            async (args) =>
            {
                if (!_networkService.IsServer)
                {
                    ConsoleSystem.LogErr("Only server can spawn entities", ConsoleChannel.Network);
                    return false;
                }

                var x = args.Length > 0 && float.TryParse(args[0], out var px) ? px : 0f;
                var y = args.Length > 1 && float.TryParse(args[1], out var py) ? py : 2f;
                var z = args.Length > 2 && float.TryParse(args[2], out var pz) ? pz : 0f;

                var position = new Vector3(x, y, z);
                SpawnTestEntity(position);

                ConsoleSystem.Log($"Spawned test entity at {position}", ConsoleChannel.Network);
                return true;
            }));

        consoleSystem.RegisterCommand(new ConsoleCommand(
            "net_replication_stats",
            "Show replication statistics",
            "net_replication_stats",
            async (args) =>
            {
                ConsoleSystem.Log($"Entities tracked: {_entities.Count}", ConsoleChannel.Network);
                ConsoleSystem.Log($"Dirty entities: {_dirtyEntities.Count}", ConsoleChannel.Network);
                ConsoleSystem.Log($"Client entities: {_clientEntities.Count}", ConsoleChannel.Network);
                ConsoleSystem.Log($"Interest radius: {_interestRadius}m", ConsoleChannel.Network);
                ConsoleSystem.Log($"Snapshot interval: {_snapshotInterval} ticks", ConsoleChannel.Network);
                ConsoleSystem.Log($"Compression: {(UseCompression ? "ON" : "OFF")}", ConsoleChannel.Network);
                ConsoleSystem.Log($"Interpolation: {(UseInterpolation ? "ON" : "OFF")}", ConsoleChannel.Network);
                return true;
            }));

        consoleSystem.RegisterCommand(new ConsoleCommand(
            "net_bandwidth",
            "Show bandwidth statistics",
            "net_bandwidth",
            async (args) =>
            {
                _metrics.PrintStats();
                return true;
            }));

        consoleSystem.RegisterCommand(new ConsoleCommand(
            "net_compression",
            "Toggle baseline/delta compression",
            "net_compression <on|off>",
            async (args) =>
            {
                if (args.Length == 0)
                {
                    ConsoleSystem.Log($"Compression: {(UseCompression ? "ON" : "OFF")}", ConsoleChannel.Network);
                    return true;
                }

                UseCompression = args[0].ToLower() == "on";
                ConsoleSystem.Log($"Compression: {(UseCompression ? "ON" : "OFF")}", ConsoleChannel.Network);
                return true;
            }));

        consoleSystem.RegisterCommand(new ConsoleCommand(
            "net_interpolation",
            "Toggle client interpolation",
            "net_interpolation <on|off>",
            async (args) =>
            {
                if (args.Length == 0)
                {
                    ConsoleSystem.Log($"Interpolation: {(UseInterpolation ? "ON" : "OFF")}", ConsoleChannel.Network);
                    return true;
                }

                UseInterpolation = args[0].ToLower() == "on";
                ConsoleSystem.Log($"Interpolation: {(UseInterpolation ? "ON" : "OFF")}", ConsoleChannel.Network);
                return true;
            }));
    }

    private void SpawnTestEntity(Vector3 position)
    {
        // Create a simple test entity
        var entity = new Node3D
        {
            Name = "TestEntity",
            Position = position
        };

        // Add visual mesh
        var mesh = new MeshInstance3D();
        var boxMesh = new BoxMesh { Size = new Vector3(1, 1, 1) };
        mesh.Mesh = boxMesh;
        entity.AddChild(mesh);

        // Add to scene
        var root = GetTree().Root.GetNodeOrNull<Node>("Root");
        if (root != null)
        {
            root.AddChild(entity);

            // Register for replication
            RegisterEntity(entity, "test_entity", position);
        }
    }

    #region Server-Side Replication

    /// <summary>
    /// Register an entity for network replication (server-side)
    /// </summary>
    public ulong RegisterEntity(Node entity, string entityType, Vector3 position, long ownerId = -1)
    {
        if (!_networkService.IsServer)
        {
            ConsoleSystem.LogWarn("[NetworkReplication] Only server can register entities", ConsoleChannel.Network);
            return 0;
        }

        var entityId = _nextEntityId++;
        var state = new NetworkEntityState
        {
            EntityId = entityId,
            EntityType = entityType,
            Position = position,
            Rotation = Vector3.Zero,
            OwnerId = ownerId,
            Components = new Dictionary<string, Dictionary<string, object>>()
        };

        _entities[entityId] = state;
        _dirtyEntities.Add(entityId);

        // Store reference to node for easy access
        entity.SetMeta("network_id", entityId);

        ConsoleSystem.Log($"[NetworkReplication] Registered entity {entityId} ({entityType})", ConsoleChannel.Network);

        // Dispatch spawn event
        GameEvent.DispatchGlobal(new NetworkEntitySpawnedEvent(entityId, entityType, position));

        return entityId;
    }

    /// <summary>
    /// Unregister an entity from replication (server-side)
    /// </summary>
    public void UnregisterEntity(ulong entityId)
    {
        if (!_networkService.IsServer) return;

        if (_entities.Remove(entityId))
        {
            _dirtyEntities.Add(entityId); // Mark for despawn in next snapshot
            GameEvent.DispatchGlobal(new NetworkEntityDespawnedEvent(entityId));
            ConsoleSystem.Log($"[NetworkReplication] Unregistered entity {entityId}", ConsoleChannel.Network);
        }
    }

    /// <summary>
    /// Update an entity's position (server-side)
    /// </summary>
    public void UpdateEntityPosition(ulong entityId, Vector3 position, Vector3 rotation)
    {
        if (!_networkService.IsServer) return;

        if (_entities.TryGetValue(entityId, out var state))
        {
            if (state.Position != position || state.Rotation != rotation)
            {
                state.Position = position;
                state.Rotation = rotation;
                _dirtyEntities.Add(entityId);
            }
        }
    }

    /// <summary>
    /// Update entity component data (server-side)
    /// </summary>
    public void UpdateEntityComponent(ulong entityId, string componentName, Dictionary<string, object> data)
    {
        if (!_networkService.IsServer) return;

        if (_entities.TryGetValue(entityId, out var state))
        {
            state.Components[componentName] = data;
            _dirtyEntities.Add(entityId);
        }
    }

    /// <summary>
    /// Server tick - assemble and send snapshots
    /// </summary>
    public void OnGameEvent(NetworkServerTickEvent evt)
    {
        if (!_networkService.IsServer) return;

        _ticksSinceSnapshot++;
        if (_ticksSinceSnapshot < _snapshotInterval) return;

        _ticksSinceSnapshot = 0;

        // Send snapshots to each connected client
        foreach (var (clientId, _) in _clientSubscriptions.ToArray())
        {
            // Skip invalid/disconnected peers
            if (clientId == 0)
            {
                _clientSubscriptions.Remove(clientId);
                continue;
            }
            try
            {
                var mp = GetTree()?.GetMultiplayer();
                var peerConnected = mp != null && mp.GetPeers().Contains((int)clientId);
                if (!peerConnected)
                {
                    _clientSubscriptions.Remove(clientId);
                    continue;
                }
            }
            catch { }

            SendSnapshotToClient(clientId, evt.Tick);
        }

        // Clear dirty flags after sending
        _dirtyEntities.Clear();
    }

    private void SendSnapshotToClient(ulong clientId, ulong tick)
    {
        // Get client's position for interest management (for now, assume origin)
        // TODO: Track actual client player position
        var clientPosition = Vector3.Zero;

        // Determine which entities are relevant to this client
        var relevantEntities = GetRelevantEntities(clientPosition);

        // Build snapshot
        var snapshot = new SnapshotData
        {
            Tick = tick,
            LastProcessedInput = 0, // TODO: Track per-client input
            Entities = new List<EntityUpdate>()
        };

        foreach (var entityId in relevantEntities)
        {
            if (!_entities.TryGetValue(entityId, out var state)) continue;

            var update = new EntityUpdate
            {
                EntityId = entityId,
                EntityType = state.EntityType,
                Position = state.Position,
                Rotation = state.Rotation,
                Components = state.Components
            };

            snapshot.Entities.Add(update);
        }

        byte[] bytes;
        string snapshotType;

        // Use compression if enabled
        if (UseCompression && _compressor.ShouldSendBaseline(clientId, tick))
        {
            // Send baseline
            _compressor.StoreBaseline(clientId, snapshot);
            bytes = SerializeSnapshot(snapshot);
            snapshotType = "baseline";
        }
        else if (UseCompression)
        {
            // Send delta
            var delta = _compressor.ComputeDelta(clientId, snapshot);
            if (delta != null && delta.Entities.Count < snapshot.Entities.Count)
            {
                bytes = SerializeDelta(delta);
                snapshotType = "delta";
            }
            else
            {
                // Delta larger than baseline, send full
                bytes = SerializeSnapshot(snapshot);
                snapshotType = "full";
            }
        }
        else
        {
            // No compression, send full snapshot
            bytes = SerializeSnapshot(snapshot);
            snapshotType = "full";
        }

        // Track metrics
        _metrics.RecordSent(bytes.Length, snapshotType);

        // Send via RPC if we have a network peer
        if (_networkService.IsServer && _networkService.Mode == NetworkMode.Server)
        {
            try
            {
                var mp = GetTree()?.GetMultiplayer();
                if (mp != null)
                {
                    var peers = mp.GetPeers();
                    if (peers.Contains((int)clientId))
                    {
                        RpcId((int)clientId, nameof(ReceiveSnapshotRpc), bytes);
                    }
                }
            }
            catch {}
        }
        else if (_networkService.Mode == NetworkMode.LocalServer)
        {
            // In local server mode, apply directly
            ReceiveSnapshotRpc(bytes);
        }
    }

    private byte[] SerializeSnapshot(SnapshotData snapshot)
    {
        // JSON serialization (Pass 1 / debug mode)
        // TODO: Switch to ProtobufAdapter in production
        var json = JsonConvert.SerializeObject(snapshot);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    private byte[] SerializeDelta(DeltaSnapshotData delta)
    {
        // JSON serialization for delta (Pass 1)
        var json = JsonConvert.SerializeObject(delta);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// RPC receiver for snapshots (client-side)
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ReceiveSnapshotRpc(byte[] data)
    {
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(data);
            var snapshot = JsonConvert.DeserializeObject<SnapshotData>(json);

            if (snapshot != null)
            {
                ReceiveSnapshot(snapshot);
                ConsoleSystem.Log(
                    $"[NetworkReplication] Received snapshot tick {snapshot.Tick}: " +
                    $"{snapshot.Entities.Count} entities",
                    ConsoleChannel.Network
                );
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[NetworkReplication] Failed to deserialize snapshot: {ex.Message}", ConsoleChannel.Network);
        }
    }

    private HashSet<ulong> GetRelevantEntities(Vector3 clientPosition)
    {
        var relevant = new HashSet<ulong>();

        foreach (var (entityId, state) in _entities)
        {
            // Simple distance check
            var distance = clientPosition.DistanceTo(state.Position);
            if (distance <= _interestRadius)
            {
                relevant.Add(entityId);
            }
        }

        return relevant;
    }

    public void OnGameEvent(NetworkClientConnectedEvent evt)
    {
        if (!_networkService.IsServer) return;

        // Initialize client's entity subscription set
        if (evt.PeerId > 0)
        {
            _clientSubscriptions[(ulong)evt.PeerId] = new HashSet<ulong>();
        }

        ConsoleSystem.Log($"[NetworkReplication] Client {evt.PeerId} subscribed to replication", ConsoleChannel.Network);
    }

    #endregion

    #region Client-Side Replication

    public void OnGameEvent(NetworkConnectedToServerEvent evt)
    {
        // Clear any existing client entities
        foreach (var node in _clientEntities.Values)
        {
            if (IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }

        _clientEntities.Clear();
        _snapshotBuffer.Clear();
        _interpolator.Clear();
        _metrics.Reset();

        ConsoleSystem.Log("[NetworkReplication] Client connected, ready to receive snapshots", ConsoleChannel.Network);
    }

    /// <summary>
    /// Client receives and processes snapshot
    /// </summary>
    public void ReceiveSnapshot(SnapshotData snapshot)
    {
        if (_networkService.IsServer) return; // Server doesn't process snapshots

        // Track metrics
        _metrics.RecordReceived(0, "snapshot"); // Size tracked in RPC

        // Buffer snapshot for interpolation
        _snapshotBuffer.Enqueue(snapshot);
        if (_snapshotBuffer.Count > SNAPSHOT_BUFFER_SIZE)
        {
            _snapshotBuffer.Dequeue();
        }

        // Add to interpolator
        if (UseInterpolation)
        {
            _interpolator.AddSnapshot(snapshot);
        }
        else
        {
            // No interpolation, apply immediately
            ApplySnapshot(snapshot);
        }
    }

    private void ApplySnapshot(SnapshotData snapshot)
    {
        var receivedEntityIds = new HashSet<ulong>();

        foreach (var entityUpdate in snapshot.Entities)
        {
            receivedEntityIds.Add(entityUpdate.EntityId);

            if (_clientEntities.TryGetValue(entityUpdate.EntityId, out var existingNode))
            {
                // Update existing entity
                if (IsInstanceValid(existingNode) && existingNode is Node3D node3D)
                {
                    node3D.Position = entityUpdate.Position;
                    node3D.Rotation = entityUpdate.Rotation;

                    // Apply component updates
                    if (entityUpdate.Components != null)
                    {
                        foreach (var (componentName, componentData) in entityUpdate.Components)
                        {
                            ApplyComponentData(node3D, componentName, componentData);
                        }
                    }
                }
            }
            else
            {
                // Spawn new entity
                SpawnClientEntity(entityUpdate);
            }
        }

        // Despawn entities not in snapshot
        var toRemove = new List<ulong>();
        foreach (var (entityId, node) in _clientEntities)
        {
            if (!receivedEntityIds.Contains(entityId))
            {
                toRemove.Add(entityId);
                if (IsInstanceValid(node))
                {
                    node.QueueFree();
                    ConsoleSystem.Log($"[NetworkReplication] Despawned client entity {entityId}", ConsoleChannel.Network);
                }
            }
        }

        foreach (var id in toRemove)
        {
            _clientEntities.Remove(id);
        }
    }

    private void ApplyComponentData(Node3D entity, string componentName, Dictionary<string, object> data)
    {
        try
        {
            switch (componentName)
            {
                case "Transform":
                    if (data.TryGetValue("position", out var posObj) && posObj is Godot.Collections.Dictionary posDict)
                    {
                        var x = Convert.ToSingle(posDict["x"]);
                        var y = Convert.ToSingle(posDict["y"]);
                        var z = Convert.ToSingle(posDict["z"]);
                        entity.Position = new Vector3(x, y, z);
                    }

                    if (data.TryGetValue("rotation", out var rotObj) && rotObj is Godot.Collections.Dictionary rotDict)
                    {
                        var x = Convert.ToSingle(rotDict["x"]);
                        var y = Convert.ToSingle(rotDict["y"]);
                        var z = Convert.ToSingle(rotDict["z"]);
                        entity.Rotation = new Vector3(x, y, z);
                    }

                    break;

                // Add more component types here as needed
                default:
                    // Unknown component type, skip
                    break;
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[NetworkReplication] Failed to apply component {componentName}: {ex.Message}", ConsoleChannel.Network);
        }
    }

    private void SpawnClientEntity(EntityUpdate update)
    {
        // Create a simple placeholder node for now
        // TODO: Load proper prefab based on EntityType
        var node = new Node3D
        {
            Name = $"NetworkEntity_{update.EntityId}",
            Position = update.Position,
            Rotation = update.Rotation
        };

        // Add visual indicator (for debugging)
        var mesh = new MeshInstance3D();
        var sphereMesh = new SphereMesh { Radius = 0.5f };
        mesh.Mesh = sphereMesh;
        node.AddChild(mesh);

        // Add to scene
        var root = GetTree().Root.GetNodeOrNull<Node>("Root");
        if (root != null)
        {
            root.AddChild(node);
            _clientEntities[update.EntityId] = node;

            ConsoleSystem.Log($"[NetworkReplication] Spawned client entity {update.EntityId} ({update.EntityType})", ConsoleChannel.Network);
        }
    }

    #endregion
}

/// <summary>
/// Server-side entity state
/// </summary>
public class NetworkEntityState
{
    public ulong EntityId { get; set; }
    public string EntityType { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public long OwnerId { get; set; } = -1; // Which client owns this entity (-1 = server)
    public Dictionary<string, Dictionary<string, object>> Components { get; set; }
}

/// <summary>
/// Snapshot sent from server to client
/// </summary>
public class SnapshotData
{
    public ulong Tick { get; set; }
    public ulong LastProcessedInput { get; set; }
    public List<EntityUpdate> Entities { get; set; }
}

/// <summary>
/// Entity update within a snapshot
/// </summary>
public class EntityUpdate
{
    public ulong EntityId { get; set; }
    public string EntityType { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public Dictionary<string, Dictionary<string, object>> Components { get; set; }
}