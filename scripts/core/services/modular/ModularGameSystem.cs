using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Waterjam.Events;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Core.Services.Modular;

/// <summary>
/// Base class for all modular game systems.
/// Provides common functionality and lifecycle management.
/// </summary>
public abstract partial class ModularGameSystem : Node, IGameSystem
{
    // Abstract properties that must be implemented
    public abstract string SystemId { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public abstract SystemCategory Category { get; }

    // Virtual properties with defaults
    public virtual Version Version => new Version(1, 0, 0);
    public virtual string[] Dependencies => Array.Empty<string>();
    public virtual string[] OptionalDependencies => Array.Empty<string>();
    public virtual bool IsCore => false;

    // State management
    public SystemState State { get; protected set; } = SystemState.Uninitialized;

    // Context and services
    protected ISystemContext Context { get; private set; }
    protected ISystemLogger Logger { get; private set; }
    protected IEventBus EventBus { get; private set; }

    // Performance tracking
    private readonly SystemMetrics _metrics = new();
    private DateTime _lastUpdateTime = DateTime.Now;
    private readonly Dictionary<string, float> _operationTimings = new();

    // Configuration
    protected SystemConfiguration Configuration { get; private set; }

    /// <summary>
    /// Initialize the system with context
    /// </summary>
    public virtual async Task<bool> InitializeAsync(ISystemContext context)
    {
        Context = context;
        Logger = context.Logger;
        EventBus = context.EventBus;

        State = SystemState.Initializing;

        try
        {
            Logger.Log($"Initializing {DisplayName}...");

            // Load configuration
            Configuration = context.Configuration.GetSystemConfig(SystemId);
            if (Configuration == null)
            {
                Configuration = CreateDefaultConfiguration();
            }

            // Validate configuration
            if (!ValidateConfiguration(Configuration))
            {
                throw new Exception("Invalid configuration");
            }

            // Register event handlers
            RegisterEventHandlers();

            // Perform system-specific initialization
            var success = await OnInitializeAsync();

            if (success)
            {
                State = SystemState.Active;
                Logger.Log($"{DisplayName} initialized successfully");

                // Start performance monitoring
                StartPerformanceMonitoring();
            }
            else
            {
                State = SystemState.Failed;
                Logger.LogError($"{DisplayName} initialization failed");
            }

            return success;
        }
        catch (Exception ex)
        {
            State = SystemState.Failed;
            Logger.LogError($"Failed to initialize {DisplayName}", ex);
            return false;
        }
    }

    /// <summary>
    /// Shutdown the system gracefully
    /// </summary>
    public virtual async Task ShutdownAsync()
    {
        State = SystemState.ShuttingDown;
        Logger.Log($"Shutting down {DisplayName}...");

        try
        {
            // Stop performance monitoring
            StopPerformanceMonitoring();

            // Unregister any console commands registered by this system
            UnregisterConsoleCommands();

            // Unregister event handlers
            UnregisterEventHandlers();

            // Perform system-specific shutdown
            await OnShutdownAsync();

            State = SystemState.Shutdown;
            Logger.Log($"{DisplayName} shut down successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during {DisplayName} shutdown", ex);
            State = SystemState.Failed;
        }
    }

    /// <summary>
    /// Check if the system can be safely unloaded
    /// </summary>
    public virtual bool CanUnload()
    {
        // Base implementation - can be overridden
        return State == SystemState.Active && !HasActiveOperations();
    }

    /// <summary>
    /// Get current performance metrics
    /// </summary>
    public virtual SystemMetrics GetMetrics()
    {
        _metrics.SystemId = SystemId;
        _metrics.LastUpdateTime = _lastUpdateTime;
        return _metrics;
    }

    /// <summary>
    /// Validate system configuration
    /// </summary>
    public virtual bool ValidateConfiguration(SystemConfiguration config)
    {
        // Base validation - can be extended
        return config != null && config.SystemId == SystemId;
    }

    /// <summary>
    /// Apply configuration changes to running system
    /// </summary>
    public virtual async Task<bool> ApplyConfigurationAsync(SystemConfiguration config)
    {
        if (!ValidateConfiguration(config))
        {
            Logger.LogError("Invalid configuration provided");
            return false;
        }

        State = SystemState.Updating;

        try
        {
            Logger.Log($"Applying configuration to {DisplayName}");

            var oldConfig = Configuration;
            Configuration = config;

            var success = await OnConfigurationChangedAsync(oldConfig, config);

            if (success)
            {
                State = SystemState.Active;
                Logger.Log("Configuration applied successfully");
            }
            else
            {
                // Rollback
                Configuration = oldConfig;
                State = SystemState.Active;
                Logger.LogWarning("Configuration change failed, rolled back");
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.LogError("Error applying configuration", ex);
            State = SystemState.Active;
            return false;
        }
    }

    // Abstract methods that must be implemented by derived classes

    /// <summary>
    /// Perform system-specific initialization
    /// </summary>
    protected abstract Task<bool> OnInitializeAsync();

    /// <summary>
    /// Perform system-specific shutdown
    /// </summary>
    protected abstract Task OnShutdownAsync();

    /// <summary>
    /// Check if system has active operations
    /// </summary>
    protected abstract bool HasActiveOperations();

    // Virtual methods that can be overridden

    /// <summary>
    /// Register event handlers for this system
    /// </summary>
    protected virtual void RegisterEventHandlers()
    {
        // Override to register specific event handlers
    }

    /// <summary>
    /// Unregister event handlers for this system
    /// </summary>
    protected virtual void UnregisterEventHandlers()
    {
        // Override to unregister specific event handlers
    }

    // Console command registration helpers for modular systems
    private readonly List<string> _registeredConsoleCommandNames = new();

    protected void RegisterConsoleCommand(ConsoleCommand command)
    {
        ConsoleSystem.Instance?.RegisterCommand(command);
        _registeredConsoleCommandNames.Add(command.Name);
    }

    protected void UnregisterConsoleCommands()
    {
        if (_registeredConsoleCommandNames.Count == 0) return;
        foreach (var name in _registeredConsoleCommandNames)
            try
            {
                ConsoleSystem.Instance?.UnregisterCommand(name);
            }
            catch
            {
                /* best-effort */
            }

        _registeredConsoleCommandNames.Clear();
    }

    /// <summary>
    /// Handle configuration changes
    /// </summary>
    protected virtual Task<bool> OnConfigurationChangedAsync(SystemConfiguration oldConfig, SystemConfiguration newConfig)
    {
        // Override to handle configuration changes
        return Task.FromResult(true);
    }

    /// <summary>
    /// Create default configuration for this system
    /// </summary>
    protected virtual SystemConfiguration CreateDefaultConfiguration()
    {
        return new SystemConfiguration
        {
            SystemId = SystemId,
            Enabled = true,
            Priority = 0,
            Settings = new Dictionary<string, object>()
        };
    }

    // Helper methods for derived classes

    /// <summary>
    /// Measure the time taken for an operation
    /// </summary>
    protected async Task<T> MeasureOperationAsync<T>(string operationName, Func<Task<T>> operation)
    {
        var startTime = DateTime.Now;

        try
        {
            return await operation();
        }
        finally
        {
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            _operationTimings[operationName] = (float)elapsed;
            Logger.LogPerformance(operationName, (float)elapsed);
        }
    }

    /// <summary>
    /// Measure the time taken for an operation
    /// </summary>
    protected T MeasureOperation<T>(string operationName, Func<T> operation)
    {
        var startTime = DateTime.Now;

        try
        {
            return operation();
        }
        finally
        {
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            _operationTimings[operationName] = (float)elapsed;
            Logger.LogPerformance(operationName, (float)elapsed);
        }
    }

    /// <summary>
    /// Update performance metrics
    /// </summary>
    protected void UpdateMetrics(Action<SystemMetrics> updateAction)
    {
        updateAction(_metrics);
        _lastUpdateTime = DateTime.Now;
    }

    /// <summary>
    /// Log a message with system context
    /// </summary>
    protected void Log(string message, LogLevel level = LogLevel.Info)
    {
        Logger.Log($"[{SystemId}] {message}", level);
    }

    /// <summary>
    /// Log an error with system context
    /// </summary>
    protected void LogError(string message, Exception ex = null)
    {
        Logger.LogError($"[{SystemId}] {message}", ex);
    }

    /// <summary>
    /// Log a warning with system context
    /// </summary>
    protected void LogWarning(string message)
    {
        Logger.LogWarning($"[{SystemId}] {message}");
    }

    /// <summary>
    /// Get a required dependency system
    /// </summary>
    protected T GetRequiredSystem<T>(string systemId) where T : class
    {
        return Context.GetRequiredSystem<T>(systemId);
    }

    /// <summary>
    /// Get an optional dependency system
    /// </summary>
    protected T GetOptionalSystem<T>(string systemId) where T : class
    {
        return Context.GetOptionalSystem<T>(systemId);
    }

    /// <summary>
    /// Get a required service
    /// </summary>
    protected T GetRequiredService<T>(string serviceId) where T : class
    {
        return Context.GetRequiredService<T>(serviceId);
    }

    /// <summary>
    /// Get an optional service
    /// </summary>
    protected T GetOptionalService<T>(string serviceId) where T : class
    {
        return Context.GetOptionalService<T>(serviceId);
    }

    // Performance monitoring

    private Timer _performanceTimer;

    private void StartPerformanceMonitoring()
    {
        if (_performanceTimer != null) return;

        _performanceTimer = new Timer
        {
            WaitTime = 1.0, // Update metrics every second
            OneShot = false
        };

        // Add timer deferred to avoid parent-busy errors during system initialization
        CallDeferred(Node.MethodName.AddChild, _performanceTimer);
        _performanceTimer.Timeout += OnPerformanceTimerTimeout;
        // Start on idle to ensure it is inside the tree
        GetTree().CreateTimer(0.01).Timeout += () =>
        {
            if (IsInstanceValid(_performanceTimer)) _performanceTimer.Start();
        };
    }

    private void StopPerformanceMonitoring()
    {
        if (_performanceTimer == null) return;

        _performanceTimer.Stop();
        _performanceTimer.QueueFree();
        _performanceTimer = null;
    }

    private void OnPerformanceTimerTimeout()
    {
        // Update CPU time metric
        if (_operationTimings.Count > 0)
            _metrics.CpuTimeMs = _operationTimings.Values.Average();

        // Memory usage would need platform-specific implementation
        // For now, we'll use a placeholder
        _metrics.MemoryUsageBytes = (long)OS.GetStaticMemoryUsage();

        // Clear operation timings for next interval
        _operationTimings.Clear();
    }
}