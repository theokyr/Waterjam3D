using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Waterjam.Core.Services.Modular;

/// <summary>
/// Core interface that all modular game systems must implement.
/// Provides lifecycle management and dependency declaration.
/// </summary>
public interface IGameSystem
{
    /// <summary>
    /// Unique identifier for this system
    /// </summary>
    string SystemId { get; }

    /// <summary>
    /// Human-readable name for UI display
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Detailed description of what this system provides
    /// </summary>
    string Description { get; }

    /// <summary>
    /// System version for compatibility checking
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// Category for organization in UI
    /// </summary>
    SystemCategory Category { get; }

    /// <summary>
    /// Required system dependencies (must be loaded first)
    /// </summary>
    string[] Dependencies { get; }

    /// <summary>
    /// Optional dependencies (loaded if available)
    /// </summary>
    string[] OptionalDependencies { get; }

    /// <summary>
    /// Whether this system is essential and cannot be disabled
    /// </summary>
    bool IsCore { get; }

    /// <summary>
    /// Current state of the system
    /// </summary>
    SystemState State { get; }

    /// <summary>
    /// Initialize the system with context
    /// </summary>
    Task<bool> InitializeAsync(ISystemContext context);

    /// <summary>
    /// Shutdown the system gracefully
    /// </summary>
    Task ShutdownAsync();

    /// <summary>
    /// Check if the system can be safely unloaded
    /// </summary>
    bool CanUnload();

    /// <summary>
    /// Get current performance metrics for this system
    /// </summary>
    SystemMetrics GetMetrics();

    /// <summary>
    /// Validate system configuration
    /// </summary>
    bool ValidateConfiguration(SystemConfiguration config);

    /// <summary>
    /// Apply configuration changes to running system
    /// </summary>
    Task<bool> ApplyConfigurationAsync(SystemConfiguration config);
}

/// <summary>
/// Categories for organizing systems in the UI
/// </summary>
public enum SystemCategory
{
    Core, // Essential systems that cannot be disabled
    Gameplay, // Quest, dialogue, progression systems
    Simulation, // NPC, traffic, world simulation
    Visual, // Graphics, effects, UI enhancements
    Audio, // Sound and music systems
    Network, // Multiplayer and online features
    Debug, // Development and debugging tools
    Modding, // User-created content support
    Experimental // Beta or experimental features
}

/// <summary>
/// Current state of a game system
/// </summary>
public enum SystemState
{
    Uninitialized, // Not yet initialized
    Initializing, // Currently initializing
    Active, // Running normally
    Suspended, // Temporarily suspended
    ShuttingDown, // In the process of shutting down
    Shutdown, // Cleanly shut down
    Failed, // Failed to initialize or crashed
    Updating // Being updated/reconfigured
}

/// <summary>
/// Performance metrics for a system
/// </summary>
public class SystemMetrics
{
    public string SystemId { get; set; }
    public long MemoryUsageBytes { get; set; }
    public float CpuTimeMs { get; set; }
    public int EventsProcessedPerSecond { get; set; }
    public int ActiveEntityCount { get; set; }
    public DateTime LastUpdateTime { get; set; }
    public Dictionary<string, float> CustomMetrics { get; set; } = new();
}

/// <summary>
/// Configuration for a system
/// </summary>
public class SystemConfiguration
{
    public string SystemId { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 0;
    public Dictionary<string, object> Settings { get; set; } = new();
}