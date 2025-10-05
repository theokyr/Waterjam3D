using System;
using System.Collections.Generic;
using System.Linq;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Tracks network performance metrics for monitoring and optimization.
/// </summary>
public class NetworkMetrics
{
    private const int SAMPLE_WINDOW_SIZE = 60; // 1 second at 60 FPS

    private ulong _totalBytesSent;
    private ulong _totalBytesReceived;
    private ulong _totalPacketsSent;
    private ulong _totalPacketsReceived;

    private readonly Queue<BandwidthSample> _samples = new();
    private readonly Dictionary<string, ulong> _messageTypeBytes = new();

    /// <summary>
    /// Record sent data
    /// </summary>
    public void RecordSent(int bytes, string messageType = "unknown")
    {
        _totalBytesSent += (ulong)bytes;
        _totalPacketsSent++;

        RecordSample(bytes, 0, messageType);

        if (!_messageTypeBytes.ContainsKey(messageType))
        {
            _messageTypeBytes[messageType] = 0;
        }

        _messageTypeBytes[messageType] += (ulong)bytes;
    }

    /// <summary>
    /// Record received data
    /// </summary>
    public void RecordReceived(int bytes, string messageType = "unknown")
    {
        _totalBytesReceived += (ulong)bytes;
        _totalPacketsReceived++;

        RecordSample(0, bytes, messageType);
    }

    private void RecordSample(int sent, int received, string messageType)
    {
        _samples.Enqueue(new BandwidthSample
        {
            Timestamp = Godot.Time.GetTicksMsec(),
            BytesSent = sent,
            BytesReceived = received,
            MessageType = messageType
        });

        while (_samples.Count > SAMPLE_WINDOW_SIZE)
        {
            _samples.Dequeue();
        }
    }

    /// <summary>
    /// Get current bandwidth usage in Kbps
    /// </summary>
    public (float kbpsUp, float kbpsDown) GetCurrentBandwidth()
    {
        if (_samples.Count < 2) return (0, 0);

        var totalSent = _samples.Sum(s => s.BytesSent);
        var totalReceived = _samples.Sum(s => s.BytesReceived);
        var duration = (_samples.Last().Timestamp - _samples.First().Timestamp) / 1000.0; // seconds

        if (duration <= 0) return (0, 0);

        var kbpsUp = (totalSent * 8) / duration / 1000;
        var kbpsDown = (totalReceived * 8) / duration / 1000;

        return ((float)kbpsUp, (float)kbpsDown);
    }

    /// <summary>
    /// Get bandwidth breakdown by message type
    /// </summary>
    public Dictionary<string, float> GetBandwidthByType()
    {
        var result = new Dictionary<string, float>();

        foreach (var (type, bytes) in _messageTypeBytes)
        {
            var mbytes = bytes / 1024.0f / 1024.0f;
            result[type] = mbytes;
        }

        return result;
    }

    /// <summary>
    /// Get summary statistics
    /// </summary>
    public NetworkStats GetStats()
    {
        var (up, down) = GetCurrentBandwidth();

        return new NetworkStats
        {
            TotalBytesSent = _totalBytesSent,
            TotalBytesReceived = _totalBytesReceived,
            TotalPacketsSent = _totalPacketsSent,
            TotalPacketsReceived = _totalPacketsReceived,
            CurrentKbpsUp = up,
            CurrentKbpsDown = down
        };
    }

    /// <summary>
    /// Print detailed statistics to console
    /// </summary>
    public void PrintStats()
    {
        var stats = GetStats();

        ConsoleSystem.Log("=== Network Metrics ===", ConsoleChannel.Network);
        ConsoleSystem.Log($"Total Sent: {stats.TotalBytesSent / 1024} KB ({stats.TotalPacketsSent} packets)", ConsoleChannel.Network);
        ConsoleSystem.Log($"Total Received: {stats.TotalBytesReceived / 1024} KB ({stats.TotalPacketsReceived} packets)", ConsoleChannel.Network);
        ConsoleSystem.Log($"Current Upload: {stats.CurrentKbpsUp:F1} Kbps", ConsoleChannel.Network);
        ConsoleSystem.Log($"Current Download: {stats.CurrentKbpsDown:F1} Kbps", ConsoleChannel.Network);

        var byType = GetBandwidthByType();
        if (byType.Count > 0)
        {
            ConsoleSystem.Log("Bandwidth by type:", ConsoleChannel.Network);
            foreach (var (type, mb) in byType.OrderByDescending(kvp => kvp.Value))
            {
                ConsoleSystem.Log($"  {type}: {mb:F2} MB", ConsoleChannel.Network);
            }
        }
    }

    /// <summary>
    /// Reset all metrics
    /// </summary>
    public void Reset()
    {
        _totalBytesSent = 0;
        _totalBytesReceived = 0;
        _totalPacketsSent = 0;
        _totalPacketsReceived = 0;
        _samples.Clear();
        _messageTypeBytes.Clear();
    }
}

public struct BandwidthSample
{
    public ulong Timestamp; // milliseconds
    public int BytesSent;
    public int BytesReceived;
    public string MessageType;
}

public struct NetworkStats
{
    public ulong TotalBytesSent;
    public ulong TotalBytesReceived;
    public ulong TotalPacketsSent;
    public ulong TotalPacketsReceived;
    public float CurrentKbpsUp;
    public float CurrentKbpsDown;
}