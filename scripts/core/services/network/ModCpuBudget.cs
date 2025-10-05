using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Tracks and enforces CPU time budgets for mods.
/// Prevents single mod from monopolizing server resources.
/// </summary>
public class ModCpuBudget
{
    private readonly Dictionary<string, ModBudgetTracker> _budgets = new();
    private const float MAX_MS_PER_TICK = 5.0f; // 5ms per mod per tick
    private const float WARNING_THRESHOLD = 3.0f; // Warn at 3ms
    private const int MAX_VIOLATIONS = 10; // Disable after 10 violations

    /// <summary>
    /// Start tracking a mod's CPU time
    /// </summary>
    public void StartTracking(string modId)
    {
        if (!_budgets.ContainsKey(modId))
        {
            _budgets[modId] = new ModBudgetTracker(modId);
        }

        _budgets[modId].StartFrame();
    }

    /// <summary>
    /// End tracking and check budget
    /// </summary>
    public void EndTracking(string modId)
    {
        if (!_budgets.TryGetValue(modId, out var tracker))
            return;

        tracker.EndFrame();

        // Check if over budget
        if (tracker.LastFrameMs > MAX_MS_PER_TICK)
        {
            tracker.ViolationCount++;

            ConsoleSystem.LogWarn(
                $"[ModBudget] {modId} exceeded budget: {tracker.LastFrameMs:F2}ms (limit: {MAX_MS_PER_TICK}ms)",
                ConsoleChannel.Network
            );

            if (tracker.ViolationCount >= MAX_VIOLATIONS)
            {
                ConsoleSystem.LogErr(
                    $"[ModBudget] {modId} disabled after {MAX_VIOLATIONS} budget violations",
                    ConsoleChannel.Network
                );

                // Dispatch event to disable mod
                // GameEvent.DispatchGlobal(new ModDisabledEvent(modId, "CPU budget violations"));
            }
        }
        else if (tracker.AverageMs > WARNING_THRESHOLD)
        {
            // Warning, but not violation
            if (tracker.WarningCount++ % 100 == 0) // Log every 100 warnings
            {
                ConsoleSystem.LogWarn(
                    $"[ModBudget] {modId} averaging {tracker.AverageMs:F2}ms (warning threshold: {WARNING_THRESHOLD}ms)",
                    ConsoleChannel.Network
                );
            }
        }
    }

    /// <summary>
    /// Get tracker for a mod
    /// </summary>
    public ModBudgetTracker GetTracker(string modId)
    {
        return _budgets.GetValueOrDefault(modId);
    }

    /// <summary>
    /// Check if mod is disabled due to violations
    /// </summary>
    public bool IsModDisabled(string modId)
    {
        var tracker = _budgets.GetValueOrDefault(modId);
        return tracker?.ViolationCount >= MAX_VIOLATIONS;
    }

    /// <summary>
    /// Reset budget for a mod (e.g., after reload)
    /// </summary>
    public void ResetBudget(string modId)
    {
        if (_budgets.TryGetValue(modId, out var tracker))
        {
            tracker.ViolationCount = 0;
            tracker.WarningCount = 0;
        }
    }

    /// <summary>
    /// Get statistics for all mods
    /// </summary>
    public ModBudgetStats GetStats()
    {
        return new ModBudgetStats
        {
            TotalMods = _budgets.Count,
            AverageCpuMs = _budgets.Values.Average(t => t.AverageMs),
            MaxCpuMs = _budgets.Values.Max(t => t.AverageMs),
            TotalViolations = _budgets.Values.Sum(t => t.ViolationCount),
            DisabledMods = _budgets.Values.Count(t => t.ViolationCount >= MAX_VIOLATIONS)
        };
    }

    /// <summary>
    /// Print detailed budget report
    /// </summary>
    public void PrintReport()
    {
        ConsoleSystem.Log("=== Mod CPU Budget Report ===", ConsoleChannel.Network);

        foreach (var (modId, tracker) in _budgets.OrderByDescending(kvp => kvp.Value.AverageMs))
        {
            var status = tracker.ViolationCount >= MAX_VIOLATIONS ? "DISABLED" :
                tracker.AverageMs > WARNING_THRESHOLD ? "WARNING" : "OK";

            ConsoleSystem.Log(
                $"{modId}: {tracker.AverageMs:F2}ms avg, {tracker.PeakMs:F2}ms peak [{status}]",
                ConsoleChannel.Network
            );
        }

        var stats = GetStats();
        ConsoleSystem.Log($"Total: {stats.AverageCpuMs:F2}ms avg, {stats.TotalViolations} violations", ConsoleChannel.Network);
    }
}

/// <summary>
/// Tracks CPU budget for a single mod
/// </summary>
public class ModBudgetTracker
{
    public string ModId { get; }
    public float AverageMs { get; private set; }
    public float LastFrameMs { get; private set; }
    public float PeakMs { get; private set; }
    public int ViolationCount { get; set; }
    public int WarningCount { get; set; }

    private ulong _frameStart;
    private readonly Queue<float> _samples = new();
    private const int SAMPLE_SIZE = 60; // 2 seconds at 30 Hz

    public ModBudgetTracker(string modId)
    {
        ModId = modId;
    }

    public void StartFrame()
    {
        _frameStart = Time.GetTicksUsec();
    }

    public void EndFrame()
    {
        var elapsed = (Time.GetTicksUsec() - _frameStart) / 1000.0f; // milliseconds
        LastFrameMs = elapsed;

        if (elapsed > PeakMs)
        {
            PeakMs = elapsed;
        }

        _samples.Enqueue(elapsed);

        while (_samples.Count > SAMPLE_SIZE)
        {
            _samples.Dequeue();
        }

        AverageMs = _samples.Average();
    }
}

/// <summary>
/// CPU budget statistics
/// </summary>
public struct ModBudgetStats
{
    public int TotalMods;
    public float AverageCpuMs;
    public float MaxCpuMs;
    public int TotalViolations;
    public int DisabledMods;
}