using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Waterjam.Core.Services;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Handles client-side snapshot interpolation for smooth rendering.
/// Maintains a buffer of snapshots and interpolates between them.
/// </summary>
public class SnapshotInterpolator
{
    private const int BUFFER_MS = 100; // 100ms interpolation buffer
    private const int MAX_BUFFER_SIZE = 10;

    private readonly Queue<TimestampedSnapshot> _buffer = new();
    private ulong _serverTickRate = 30; // Hz

    /// <summary>
    /// Add a received snapshot to the buffer
    /// </summary>
    public void AddSnapshot(SnapshotData snapshot)
    {
        var timestamp = Time.GetTicksMsec();
        _buffer.Enqueue(new TimestampedSnapshot
        {
            Snapshot = snapshot,
            ReceivedAt = timestamp
        });

        // Keep buffer size reasonable
        while (_buffer.Count > MAX_BUFFER_SIZE)
        {
            _buffer.Dequeue();
        }
    }

    /// <summary>
    /// Get interpolated snapshot for rendering
    /// </summary>
    public SnapshotData GetInterpolatedSnapshot()
    {
        var now = Time.GetTicksMsec();
        var renderTime = now - BUFFER_MS; // Render 100ms in the past

        if (_buffer.Count < 2)
        {
            // Not enough data for interpolation
            var last = _buffer.LastOrDefault();
            return last.Snapshot;
        }

        // Find the two snapshots to interpolate between
        TimestampedSnapshot? from = null;
        TimestampedSnapshot? to = null;

        var snapshots = _buffer.ToArray();
        for (int i = 0; i < snapshots.Length - 1; i++)
        {
            if (snapshots[i].ReceivedAt <= renderTime && snapshots[i + 1].ReceivedAt >= renderTime)
            {
                from = snapshots[i];
                to = snapshots[i + 1];
                break;
            }
        }

        // Fallback: use latest two snapshots
        if (!from.HasValue || !to.HasValue)
        {
            var count = snapshots.Length;
            if (count >= 2)
            {
                from = snapshots[count - 2];
                to = snapshots[count - 1];
            }
            else
            {
                return snapshots.Last().Snapshot;
            }
        }

        // Calculate interpolation factor
        float t = 0.5f;
        if (to.Value.ReceivedAt != from.Value.ReceivedAt)
        {
            t = (float)(renderTime - from.Value.ReceivedAt) / (to.Value.ReceivedAt - from.Value.ReceivedAt);
            t = Mathf.Clamp(t, 0f, 1f);
        }

        // Interpolate entity positions
        return InterpolateSnapshots(from.Value.Snapshot, to.Value.Snapshot, t);
    }

    private SnapshotData InterpolateSnapshots(SnapshotData from, SnapshotData to, float t)
    {
        var result = new SnapshotData
        {
            Tick = to.Tick,
            LastProcessedInput = to.LastProcessedInput,
            Entities = new List<EntityUpdate>()
        };

        var fromEntities = from.Entities.ToDictionary(e => e.EntityId);

        foreach (var toEntity in to.Entities)
        {
            if (fromEntities.TryGetValue(toEntity.EntityId, out var fromEntity))
            {
                // Interpolate between from and to
                result.Entities.Add(new EntityUpdate
                {
                    EntityId = toEntity.EntityId,
                    EntityType = toEntity.EntityType,
                    Position = fromEntity.Position.Lerp(toEntity.Position, t),
                    Rotation = LerpRotation(fromEntity.Rotation, toEntity.Rotation, t),
                    Components = toEntity.Components // Components updated immediately
                });
            }
            else
            {
                // New entity that wasn't in previous snapshot
                // Spawn immediately, no interpolation
                result.Entities.Add(toEntity);
            }
        }

        return result;
    }

    private Vector3 LerpRotation(Vector3 from, Vector3 to, float t)
    {
        // Handle rotation wrapping (e.g., 359° to 1°)
        var result = new Vector3();

        result.X = LerpAngle(from.X, to.X, t);
        result.Y = LerpAngle(from.Y, to.Y, t);
        result.Z = LerpAngle(from.Z, to.Z, t);

        return result;
    }

    private float LerpAngle(float from, float to, float t)
    {
        float delta = Mathf.Wrap(to - from, -Mathf.Pi, Mathf.Pi);
        return from + delta * t;
    }

    /// <summary>
    /// Clear the buffer (e.g., on disconnect)
    /// </summary>
    public void Clear()
    {
        _buffer.Clear();
    }
}

public struct TimestampedSnapshot
{
    public SnapshotData Snapshot;
    public ulong ReceivedAt; // milliseconds
}