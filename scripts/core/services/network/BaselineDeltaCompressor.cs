using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Waterjam.Core.Services;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Implements baseline + delta snapshot compression.
/// Sends full baseline periodically, deltas in between for bandwidth efficiency.
/// </summary>
public class BaselineDeltaCompressor
{
    private const int BASELINE_INTERVAL_TICKS = 150; // 5 seconds at 30 Hz
    private const float POSITION_EPSILON = 0.01f; // 1cm
    private const float ROTATION_EPSILON = 0.01f; // ~0.57 degrees

    private readonly Dictionary<ulong, SnapshotData> _clientBaselines = new();
    private readonly Dictionary<ulong, int> _ticksSinceBaseline = new();

    /// <summary>
    /// Determine if we should send a baseline or delta for this client
    /// </summary>
    public bool ShouldSendBaseline(ulong clientId, ulong currentTick)
    {
        if (!_ticksSinceBaseline.ContainsKey(clientId))
        {
            _ticksSinceBaseline[clientId] = 0;
            return true; // First snapshot is always baseline
        }

        _ticksSinceBaseline[clientId]++;

        if (_ticksSinceBaseline[clientId] >= BASELINE_INTERVAL_TICKS)
        {
            _ticksSinceBaseline[clientId] = 0;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Store baseline for future delta computation
    /// </summary>
    public void StoreBaseline(ulong clientId, SnapshotData baseline)
    {
        _clientBaselines[clientId] = baseline;
        ConsoleSystem.Log($"[Compression] Stored baseline for client {clientId}: {baseline.Entities.Count} entities", ConsoleChannel.Network);
    }

    /// <summary>
    /// Compute delta from baseline to current snapshot
    /// </summary>
    public DeltaSnapshotData ComputeDelta(ulong clientId, SnapshotData current)
    {
        if (!_clientBaselines.TryGetValue(clientId, out var baseline))
        {
            // No baseline yet, return null to force full snapshot
            return null;
        }

        var delta = new DeltaSnapshotData
        {
            Tick = current.Tick,
            BaselineTick = baseline.Tick,
            Entities = new List<EntityDelta>()
        };

        var baselineEntities = baseline.Entities.ToDictionary(e => e.EntityId);
        var currentEntities = current.Entities.ToDictionary(e => e.EntityId);

        // Check for new and updated entities
        foreach (var currentEntity in current.Entities)
        {
            if (baselineEntities.TryGetValue(currentEntity.EntityId, out var baseEntity))
            {
                // Entity exists in baseline - compute delta
                var entityDelta = ComputeEntityDelta(baseEntity, currentEntity);

                if (entityDelta.HasChanges)
                {
                    delta.Entities.Add(entityDelta);
                }
            }
            else
            {
                // New entity (spawned since baseline)
                delta.Entities.Add(new EntityDelta
                {
                    EntityId = currentEntity.EntityId,
                    IsSpawn = true,
                    EntityType = currentEntity.EntityType,
                    Position = currentEntity.Position,
                    Rotation = currentEntity.Rotation,
                    Components = currentEntity.Components,
                    HasChanges = true
                });
            }
        }

        // Check for despawned entities
        foreach (var baseEntity in baseline.Entities)
        {
            if (!currentEntities.ContainsKey(baseEntity.EntityId))
            {
                delta.Entities.Add(new EntityDelta
                {
                    EntityId = baseEntity.EntityId,
                    IsDespawn = true,
                    HasChanges = true
                });
            }
        }

        return delta;
    }

    private EntityDelta ComputeEntityDelta(EntityUpdate baseline, EntityUpdate current)
    {
        var delta = new EntityDelta
        {
            EntityId = current.EntityId,
            EntityType = current.EntityType
        };

        // Check position change
        if (baseline.Position.DistanceTo(current.Position) > POSITION_EPSILON)
        {
            delta.PositionChanged = true;
            delta.Position = current.Position;
            delta.HasChanges = true;
        }

        // Check rotation change
        if ((baseline.Rotation - current.Rotation).Length() > ROTATION_EPSILON)
        {
            delta.RotationChanged = true;
            delta.Rotation = current.Rotation;
            delta.HasChanges = true;
        }

        // TODO: Component delta detection
        // For now, include all components if any changed
        if (current.Components != null && current.Components.Count > 0)
        {
            delta.Components = current.Components;
            delta.HasChanges = true;
        }

        return delta;
    }

    /// <summary>
    /// Apply delta to baseline to reconstruct full snapshot (client-side)
    /// </summary>
    public SnapshotData ApplyDelta(SnapshotData baseline, DeltaSnapshotData delta)
    {
        var result = new SnapshotData
        {
            Tick = delta.Tick,
            LastProcessedInput = delta.LastProcessedInput,
            Entities = new List<EntityUpdate>()
        };

        var baselineEntities = baseline.Entities.ToDictionary(e => e.EntityId);

        // Start with baseline entities
        foreach (var baseEntity in baseline.Entities)
        {
            result.Entities.Add(new EntityUpdate
            {
                EntityId = baseEntity.EntityId,
                EntityType = baseEntity.EntityType,
                Position = baseEntity.Position,
                Rotation = baseEntity.Rotation,
                Components = baseEntity.Components
            });
        }

        // Apply deltas
        foreach (var entityDelta in delta.Entities)
        {
            if (entityDelta.IsDespawn)
            {
                // Remove from result
                result.Entities.RemoveAll(e => e.EntityId == entityDelta.EntityId);
            }
            else if (entityDelta.IsSpawn)
            {
                // Add new entity
                result.Entities.Add(new EntityUpdate
                {
                    EntityId = entityDelta.EntityId,
                    EntityType = entityDelta.EntityType,
                    Position = entityDelta.Position,
                    Rotation = entityDelta.Rotation,
                    Components = entityDelta.Components
                });
            }
            else
            {
                // Update existing entity
                var existing = result.Entities.FirstOrDefault(e => e.EntityId == entityDelta.EntityId);
                if (existing != null)
                {
                    if (entityDelta.PositionChanged)
                        existing.Position = entityDelta.Position;

                    if (entityDelta.RotationChanged)
                        existing.Rotation = entityDelta.Rotation;

                    if (entityDelta.Components != null)
                        existing.Components = entityDelta.Components;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Clear baselines for a disconnected client
    /// </summary>
    public void ClearClient(ulong clientId)
    {
        _clientBaselines.Remove(clientId);
        _ticksSinceBaseline.Remove(clientId);
    }
}

/// <summary>
/// Delta snapshot data structure
/// </summary>
public class DeltaSnapshotData
{
    public ulong Tick { get; set; }
    public ulong BaselineTick { get; set; }
    public ulong LastProcessedInput { get; set; }
    public List<EntityDelta> Entities { get; set; } = new();
}

/// <summary>
/// Entity delta within a delta snapshot
/// </summary>
public class EntityDelta
{
    public ulong EntityId { get; set; }
    public bool IsSpawn { get; set; }
    public bool IsDespawn { get; set; }
    public string EntityType { get; set; }

    public bool PositionChanged { get; set; }
    public Vector3 Position { get; set; }

    public bool RotationChanged { get; set; }
    public Vector3 Rotation { get; set; }

    public Dictionary<string, Dictionary<string, object>> Components { get; set; }

    public bool HasChanges { get; set; }
}