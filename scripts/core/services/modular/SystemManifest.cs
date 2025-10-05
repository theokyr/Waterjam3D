using System;
using System.Collections.Generic;
using System.Linq;

namespace Waterjam.Core.Services.Modular;

/// <summary>
/// Manifest describing a game system and its requirements.
/// Loaded from JSON files to define available systems.
/// </summary>
public class SystemManifest
{
    /// <summary>
    /// Unique identifier for the system
    /// </summary>
    public string SystemId { get; set; }

    /// <summary>
    /// Display name for UI
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    /// Detailed description
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// System version
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// System category for organization
    /// </summary>
    public SystemCategory Category { get; set; } = SystemCategory.Gameplay;

    /// <summary>
    /// Path to the system scene file (if scene-based)
    /// </summary>
    public string ScenePath { get; set; }

    /// <summary>
    /// Path to the system script file (if script-based)
    /// </summary>
    public string ScriptPath { get; set; }

    /// <summary>
    /// Required dependencies that must be loaded first
    /// </summary>
    public string[] Dependencies { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional dependencies that will be used if available
    /// </summary>
    public string[] OptionalDependencies { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Whether this is a core system that cannot be disabled
    /// </summary>
    public bool IsCore { get; set; } = false;

    /// <summary>
    /// Load priority (lower numbers load first)
    /// </summary>
    public int LoadPriority { get; set; } = 100;

    /// <summary>
    /// Default configuration for the system
    /// </summary>
    public Dictionary<string, object> DefaultConfiguration { get; set; } = new();

    /// <summary>
    /// Resource paths that this system requires
    /// </summary>
    public string[] ResourcePaths { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Minimum required version of dependencies
    /// </summary>
    public Dictionary<string, string> MinDependencyVersions { get; set; } = new();

    /// <summary>
    /// Tags for categorization and filtering
    /// </summary>
    public string[] Tags { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Author information
    /// </summary>
    public SystemAuthor Author { get; set; }

    /// <summary>
    /// Whether this system supports hot reload
    /// </summary>
    public bool SupportsHotReload { get; set; } = false;

    /// <summary>
    /// Optional: Name of the assembly (DLL without extension) providing this system (for code mods)
    /// </summary>
    public string AssemblyName { get; set; }

    /// <summary>
    /// Optional: Fully-qualified type name implementing the system (must derive Node and implement IGameSystem)
    /// </summary>
    public string TypeName { get; set; }

    /// <summary>
    /// Runtime-only: The originating directory of this manifest (e.g., user://mods/ModId/)
    /// Used to resolve relative resource and assembly paths for mods.
    /// </summary>
    public string OriginDirectory { get; set; }

    /// <summary>
    /// Performance impact rating (1-5, where 1 is minimal)
    /// </summary>
    public int PerformanceImpact { get; set; } = 2;

    /// <summary>
    /// Memory usage estimate in MB
    /// </summary>
    public float EstimatedMemoryMB { get; set; } = 10.0f;

    // Networking fields (v2)

    /// <summary>
    /// Where this mod/system runs: Client, Server, or Both
    /// </summary>
    public ModSide Side { get; set; } = ModSide.Both;

    /// <summary>
    /// Permissions this mod requires (e.g., "read_world_chunks", "spawn_entities", "ui_overlay")
    /// </summary>
    public string[] Permissions { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Network-specific configuration
    /// </summary>
    public NetworkManifestData Net { get; set; }

    /// <summary>
    /// SHA-256 checksum of the mod content for integrity verification
    /// </summary>
    public string Checksum { get; set; }
}

/// <summary>
/// Where a mod/system runs
/// </summary>
public enum ModSide
{
    Client = 0, // Client-only (UI, cosmetics, soundpacks)
    Server = 1, // Server-only (gameplay rules, server-driven features)
    Both = 2 // Both sides (complex behaviors requiring client code)
}

/// <summary>
/// Network-specific manifest data
/// </summary>
public class NetworkManifestData
{
    /// <summary>
    /// RPC methods this mod exposes (with direction)
    /// </summary>
    public RpcDeclaration[] Rpcs { get; set; } = Array.Empty<RpcDeclaration>();

    /// <summary>
    /// Component types this mod replicates
    /// </summary>
    public ComponentDeclaration[] Components { get; set; } = Array.Empty<ComponentDeclaration>();

    /// <summary>
    /// Bandwidth estimate in KB/s per player
    /// </summary>
    public float EstimatedBandwidthKBps { get; set; } = 1.0f;
}

/// <summary>
/// RPC method declaration
/// </summary>
public class RpcDeclaration
{
    public string Name { get; set; }
    public RpcDirection Direction { get; set; }
    public bool Reliable { get; set; } = true;
}

/// <summary>
/// Direction of RPC calls
/// </summary>
public enum RpcDirection
{
    ClientToServer,
    ServerToClient,
    Bidirectional
}

/// <summary>
/// Component replication declaration
/// </summary>
public class ComponentDeclaration
{
    public string Name { get; set; }
    public ReplicationMode Mode { get; set; }
    public Dictionary<string, string> Fields { get; set; } = new(); // field name -> type
}

/// <summary>
/// How component data is replicated
/// </summary>
public enum ReplicationMode
{
    Full, // Full state every snapshot
    Delta, // Only changed fields
    OnDemand // Only when explicitly requested
}

/// <summary>
/// Author information for a system
/// </summary>
public class SystemAuthor
{
    public string Name { get; set; }
    public string Email { get; set; }
    public string Website { get; set; }
}

/// <summary>
/// Resolves dependencies between systems
/// </summary>
public class DependencyResolver
{
    /// <summary>
    /// Resolve load order for a system and its dependencies
    /// </summary>
    public List<string> ResolveDependencies(string systemId, Dictionary<string, SystemManifest> manifests)
    {
        var resolved = new List<string>();
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        ResolveDependenciesRecursive(systemId, manifests, resolved, visited, recursionStack);

        return resolved;
    }

    private void ResolveDependenciesRecursive(
        string systemId,
        Dictionary<string, SystemManifest> manifests,
        List<string> resolved,
        HashSet<string> visited,
        HashSet<string> recursionStack)
    {
        if (recursionStack.Contains(systemId))
        {
            throw new InvalidOperationException($"Circular dependency detected: {systemId}");
        }

        if (visited.Contains(systemId))
        {
            return;
        }

        visited.Add(systemId);
        recursionStack.Add(systemId);

        if (manifests.TryGetValue(systemId, out var manifest))
        {
            // Process dependencies first
            foreach (var dep in manifest.Dependencies)
            {
                if (manifests.ContainsKey(dep))
                {
                    ResolveDependenciesRecursive(dep, manifests, resolved, visited, recursionStack);
                }
                else
                {
                    throw new SystemDependencyException(systemId, new[] { dep },
                        $"System {systemId} depends on {dep}, which is not available");
                }
            }

            // Process optional dependencies if available
            foreach (var optDep in manifest.OptionalDependencies)
            {
                if (manifests.ContainsKey(optDep))
                {
                    ResolveDependenciesRecursive(optDep, manifests, resolved, visited, recursionStack);
                }
            }
        }

        recursionStack.Remove(systemId);
        resolved.Add(systemId);
    }

    /// <summary>
    /// Check if loading a system would create circular dependencies
    /// </summary>
    public bool HasCircularDependency(string systemId, Dictionary<string, SystemManifest> manifests)
    {
        try
        {
            ResolveDependencies(systemId, manifests);
            return false;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Circular dependency"))
        {
            return true;
        }
    }

    /// <summary>
    /// Get all systems that would be affected by unloading a system
    /// </summary>
    public List<string> GetDependentSystems(string systemId, Dictionary<string, SystemManifest> manifests)
    {
        var dependents = new List<string>();

        foreach (var kvp in manifests)
        {
            if (kvp.Value.Dependencies.Contains(systemId))
            {
                dependents.Add(kvp.Key);
                // Recursively get systems that depend on this dependent
                dependents.AddRange(GetDependentSystems(kvp.Key, manifests));
            }
        }

        return dependents.Distinct().ToList();
    }
}

/// <summary>
/// Monitors performance metrics for systems
/// </summary>
public class SystemPerformanceMonitor
{
    private readonly Dictionary<string, SystemMetrics> _metrics = new();
    private readonly Dictionary<string, Queue<float>> _cpuHistory = new();
    private readonly Dictionary<string, Queue<long>> _memoryHistory = new();
    private const int HISTORY_SIZE = 60; // Keep 60 samples

    /// <summary>
    /// Update metrics for a system
    /// </summary>
    public void UpdateMetrics(string systemId, SystemMetrics metrics)
    {
        _metrics[systemId] = metrics;

        // Update history
        if (!_cpuHistory.ContainsKey(systemId))
        {
            _cpuHistory[systemId] = new Queue<float>(HISTORY_SIZE);
            _memoryHistory[systemId] = new Queue<long>(HISTORY_SIZE);
        }

        var cpuQueue = _cpuHistory[systemId];
        var memQueue = _memoryHistory[systemId];

        if (cpuQueue.Count >= HISTORY_SIZE) cpuQueue.Dequeue();
        if (memQueue.Count >= HISTORY_SIZE) memQueue.Dequeue();

        cpuQueue.Enqueue(metrics.CpuTimeMs);
        memQueue.Enqueue(metrics.MemoryUsageBytes);
    }

    /// <summary>
    /// Get current metrics for a system
    /// </summary>
    public SystemMetrics GetMetrics(string systemId)
    {
        return _metrics.GetValueOrDefault(systemId, new SystemMetrics { SystemId = systemId });
    }

    /// <summary>
    /// Get average CPU usage over history
    /// </summary>
    public float GetAverageCpuMs(string systemId)
    {
        if (!_cpuHistory.TryGetValue(systemId, out var history) || history.Count == 0)
            return 0;

        return history.Average();
    }

    /// <summary>
    /// Get average memory usage over history
    /// </summary>
    public long GetAverageMemoryBytes(string systemId)
    {
        if (!_memoryHistory.TryGetValue(systemId, out var history) || history.Count == 0)
            return 0;

        return (long)history.Average();
    }

    /// <summary>
    /// Get total resource usage across all systems
    /// </summary>
    public (float totalCpuMs, long totalMemoryBytes) GetTotalUsage()
    {
        float totalCpu = 0;
        long totalMemory = 0;

        foreach (var metrics in _metrics.Values)
        {
            totalCpu += metrics.CpuTimeMs;
            totalMemory += metrics.MemoryUsageBytes;
        }

        return (totalCpu, totalMemory);
    }
}