using System;
using System.Threading.Tasks;
using Godot;
using Waterjam.Events;

namespace Waterjam.Core.Services.Modular;

/// <summary>
/// Context provided to systems during initialization.
/// Provides access to dependencies, services, and shared resources.
/// </summary>
public interface ISystemContext
{
    /// <summary>
    /// Logger for system-specific logging
    /// </summary>
    ISystemLogger Logger { get; }

    /// <summary>
    /// Event bus for system communication
    /// </summary>
    IEventBus EventBus { get; }

    /// <summary>
    /// Resource loader for system assets
    /// </summary>
    ISystemResourceLoader ResourceLoader { get; }

    /// <summary>
    /// Configuration manager for system settings
    /// </summary>
    IConfigurationManager Configuration { get; }

    /// <summary>
    /// Get a required system dependency
    /// </summary>
    /// <exception cref="SystemNotFoundException">Thrown if system not found</exception>
    T GetRequiredSystem<T>(string systemId) where T : class;

    /// <summary>
    /// Get an optional system dependency
    /// </summary>
    /// <returns>System instance or null if not available</returns>
    T GetOptionalSystem<T>(string systemId) where T : class;

    /// <summary>
    /// Get a required service
    /// </summary>
    /// <exception cref="ServiceNotFoundException">Thrown if service not found</exception>
    T GetRequiredService<T>(string serviceId) where T : class;

    /// <summary>
    /// Get an optional service
    /// </summary>
    /// <returns>Service instance or null if not available</returns>
    T GetOptionalService<T>(string serviceId) where T : class;

    /// <summary>
    /// Check if a system is available
    /// </summary>
    bool IsSystemAvailable(string systemId);

    /// <summary>
    /// Check if a service is available
    /// </summary>
    bool IsServiceAvailable(string serviceId);
}

/// <summary>
/// Logger interface for systems
/// </summary>
public interface ISystemLogger
{
    void Log(string message, LogLevel level = LogLevel.Info);
    void LogError(string message, Exception exception = null);
    void LogWarning(string message);
    void LogDebug(string message);
    void LogPerformance(string operation, float timeMs);
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Event bus for decoupled system communication
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Dispatch an event globally
    /// </summary>
    void Dispatch<T>(T eventArgs) where T : IGameEvent;

    /// <summary>
    /// Subscribe to an event type
    /// </summary>
    void Subscribe<T>(Action<T> handler) where T : IGameEvent;

    /// <summary>
    /// Unsubscribe from an event type
    /// </summary>
    void Unsubscribe<T>(Action<T> handler) where T : IGameEvent;

    /// <summary>
    /// Dispatch an event with priority
    /// </summary>
    void DispatchPriority<T>(T eventArgs, EventPriority priority) where T : IGameEvent;
}

public enum EventPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// Resource loader for system-specific assets
/// </summary>
public interface ISystemResourceLoader
{
    /// <summary>
    /// Load a resource synchronously
    /// </summary>
    T Load<T>(string path) where T : Resource;

    /// <summary>
    /// Load a resource asynchronously
    /// </summary>
    Task<T> LoadAsync<T>(string path) where T : Resource;

    /// <summary>
    /// Check if a resource exists
    /// </summary>
    bool Exists(string path);

    /// <summary>
    /// Preload resources for faster access
    /// </summary>
    Task PreloadAsync(params string[] paths);

    /// <summary>
    /// Unload cached resources to free memory
    /// </summary>
    void UnloadCache();
}

/// <summary>
/// Configuration manager for system settings
/// </summary>
public interface IConfigurationManager
{
    /// <summary>
    /// Get configuration for a system
    /// </summary>
    SystemConfiguration GetSystemConfig(string systemId);

    /// <summary>
    /// Update configuration for a system
    /// </summary>
    Task<bool> UpdateSystemConfig(string systemId, SystemConfiguration config);

    /// <summary>
    /// Get a specific setting value
    /// </summary>
    T GetSetting<T>(string systemId, string key, T defaultValue = default);

    /// <summary>
    /// Set a specific setting value
    /// </summary>
    void SetSetting(string systemId, string key, object value);

    /// <summary>
    /// Load configuration from file
    /// </summary>
    Task<bool> LoadConfigurationAsync(string path);

    /// <summary>
    /// Save configuration to file
    /// </summary>
    Task<bool> SaveConfigurationAsync(string path);

    /// <summary>
    /// Reset to default configuration
    /// </summary>
    void ResetToDefaults(string systemId);
}

/// <summary>
/// Custom exceptions for system operations
/// </summary>
public class SystemNotFoundException : Exception
{
    public string SystemId { get; }

    public SystemNotFoundException(string systemId, string message) : base(message)
    {
        SystemId = systemId;
    }
}

public class ServiceNotFoundException : Exception
{
    public string ServiceId { get; }

    public ServiceNotFoundException(string serviceId, string message) : base(message)
    {
        ServiceId = serviceId;
    }
}

public class SystemDependencyException : Exception
{
    public string SystemId { get; }
    public string[] MissingDependencies { get; }

    public SystemDependencyException(string systemId, string[] missingDeps, string message) : base(message)
    {
        SystemId = systemId;
        MissingDependencies = missingDeps;
    }
}