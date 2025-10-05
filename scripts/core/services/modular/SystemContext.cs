using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Waterjam.Events;
using Waterjam.Core.Systems.Console;
using Newtonsoft.Json;

namespace Waterjam.Core.Services.Modular;

/// <summary>
/// Implementation of ISystemContext that provides services to game systems
/// </summary>
public class SystemContext : ISystemContext
{
    private readonly SystemRegistry _registry;
    private readonly Node _serviceRoot;
    private readonly SystemLogger _logger;
    private readonly SystemEventBus _eventBus;
    private readonly SystemResourceLoader _resourceLoader;
    private readonly SystemConfigurationManager _configManager;

    public ISystemLogger Logger => _logger;
    public IEventBus EventBus => _eventBus;
    public ISystemResourceLoader ResourceLoader => _resourceLoader;
    public IConfigurationManager Configuration => _configManager;

    public SystemContext(SystemRegistry registry)
    {
        _registry = registry;
        var tree = Engine.GetMainLoop() as SceneTree;
        _serviceRoot = tree?.Root;

        _logger = new SystemLogger();
        _eventBus = new SystemEventBus();
        _resourceLoader = new SystemResourceLoader();
        _configManager = new SystemConfigurationManager();
    }

    public T GetRequiredSystem<T>(string systemId) where T : class
    {
        var system = _registry.GetSystem(systemId);

        if (system == null)
        {
            throw new SystemNotFoundException(systemId, $"Required system '{systemId}' not found");
        }

        if (system is not T typedSystem)
        {
            throw new InvalidCastException($"System '{systemId}' cannot be cast to type {typeof(T).Name}");
        }

        return typedSystem;
    }

    public T GetOptionalSystem<T>(string systemId) where T : class
    {
        var system = _registry.GetSystem(systemId);
        return system as T;
    }

    public T GetRequiredService<T>(string serviceId) where T : class
    {
        var service = GetServiceNode(serviceId);

        if (service == null)
        {
            throw new ServiceNotFoundException(serviceId, $"Required service '{serviceId}' not found");
        }

        if (service is not T typedService)
        {
            throw new InvalidCastException($"Service '{serviceId}' cannot be cast to type {typeof(T).Name}");
        }

        return typedService;
    }

    public T GetOptionalService<T>(string serviceId) where T : class
    {
        var service = GetServiceNode(serviceId);
        return service as T;
    }

    public bool IsSystemAvailable(string systemId)
    {
        return _registry.IsSystemLoaded(systemId);
    }

    public bool IsServiceAvailable(string serviceId)
    {
        return GetServiceNode(serviceId) != null;
    }

    private Node GetServiceNode(string serviceId)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        var root = tree?.Root;
        if (root == null) return null;
        return root.GetNodeOrNull($"/root/{serviceId}");
    }
}

/// <summary>
/// Logger implementation for systems
/// </summary>
public class SystemLogger : ISystemLogger
{
    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        var channel = level switch
        {
            LogLevel.Debug => ConsoleChannel.Debug,
            LogLevel.Warning => ConsoleChannel.Warning,
            LogLevel.Error => ConsoleChannel.Error,
            LogLevel.Critical => ConsoleChannel.Error,
            _ => ConsoleChannel.System
        };

        ConsoleSystem.Log($"[ModularSystem] {message}", channel);
    }

    public void LogError(string message, Exception exception = null)
    {
        var fullMessage = exception != null
            ? $"[ModularSystem] {message}: {exception.Message}\n{exception.StackTrace}"
            : $"[ModularSystem] {message}";

        ConsoleSystem.LogErr(fullMessage, ConsoleChannel.Error);
    }

    public void LogWarning(string message)
    {
        ConsoleSystem.LogWarn($"[ModularSystem] {message}", ConsoleChannel.Warning);
    }

    public void LogDebug(string message)
    {
        if (OS.IsDebugBuild())
        {
            ConsoleSystem.Log($"[ModularSystem] [DEBUG] {message}", ConsoleChannel.Debug);
        }
    }

    public void LogPerformance(string operation, float timeMs)
    {
        if (timeMs > 16.0f)
            ConsoleSystem.LogWarn($"[ModularSystem] [PERF] {operation} took {timeMs:F2}ms", ConsoleChannel.System);
    }
}

/// <summary>
/// Event bus implementation for systems
/// </summary>
public class SystemEventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly object _lock = new();

    public void Dispatch<T>(T eventArgs) where T : IGameEvent
    {
        // Use the existing GameEvent system for compatibility
        GameEvent.DispatchGlobal(eventArgs);

        // Also dispatch to local handlers
        lock (_lock)
        {
            if (_handlers.TryGetValue(typeof(T), out var handlers))
            {
                foreach (var handler in handlers.ToArray())
                {
                    try
                    {
                        ((Action<T>)handler)(eventArgs);
                    }
                    catch (Exception ex)
                    {
                        ConsoleSystem.LogErr($"Error in event handler: {ex.Message}", ConsoleChannel.System);
                    }
                }
            }
        }
    }

    public void Subscribe<T>(Action<T> handler) where T : IGameEvent
    {
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(T), out var handlers))
            {
                handlers = new List<Delegate>();
                _handlers[typeof(T)] = handlers;
            }

            handlers.Add(handler);
        }
    }

    public void Unsubscribe<T>(Action<T> handler) where T : IGameEvent
    {
        lock (_lock)
        {
            if (_handlers.TryGetValue(typeof(T), out var handlers))
            {
                handlers.Remove(handler);
            }
        }
    }

    public void DispatchPriority<T>(T eventArgs, EventPriority priority) where T : IGameEvent
    {
        // For now, just dispatch normally
        // Priority handling could be added later
        Dispatch(eventArgs);
    }
}

/// <summary>
/// Resource loader implementation for systems
/// </summary>
public class SystemResourceLoader : ISystemResourceLoader
{
    private readonly Dictionary<string, Resource> _cache = new();

    public T Load<T>(string path) where T : Resource
    {
        if (_cache.TryGetValue(path, out var cached) && cached is T typedCached)
        {
            return typedCached;
        }

        var resource = GD.Load<T>(path);
        if (resource != null)
        {
            _cache[path] = resource;
        }

        return resource;
    }

    public Task<T> LoadAsync<T>(string path) where T : Resource
    {
        // Godot doesn't have true async loading yet, so we simulate it
        return Task.FromResult(Load<T>(path));
    }

    public bool Exists(string path)
    {
        return ResourceLoader.Exists(path);
    }

    public Task PreloadAsync(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (!_cache.ContainsKey(path))
            {
                var resource = GD.Load(path);
                if (resource != null)
                {
                    _cache[path] = resource;
                }
            }
        }

        return Task.CompletedTask;
    }

    public void UnloadCache()
    {
        _cache.Clear();
    }
}

/// <summary>
/// Configuration manager implementation for systems
/// </summary>
public class SystemConfigurationManager : IConfigurationManager
{
    private readonly Dictionary<string, SystemConfiguration> _configurations = new();
    private readonly string CONFIG_PATH = "user://system_configs/";

    public SystemConfiguration GetSystemConfig(string systemId)
    {
        if (_configurations.TryGetValue(systemId, out var config))
        {
            return config;
        }

        // Try to load from file
        var configPath = $"{CONFIG_PATH}{systemId}.json";
        if (FileAccess.FileExists(configPath))
        {
            try
            {
                var json = FileAccess.GetFileAsString(configPath);
                config = JsonConvert.DeserializeObject<SystemConfiguration>(json);
                _configurations[systemId] = config;
                return config;
            }
            catch (Exception ex)
            {
                ConsoleSystem.LogWarn($"Failed to load config for {systemId}: {ex.Message}", ConsoleChannel.System);
            }
        }

        // Return default config
        return new SystemConfiguration
        {
            SystemId = systemId,
            Enabled = true,
            Priority = 0,
            Settings = new Dictionary<string, object>()
        };
    }

    public Task<bool> UpdateSystemConfig(string systemId, SystemConfiguration config)
    {
        _configurations[systemId] = config;

        // Save to file
        try
        {
            // Ensure directory exists
            DirAccess.MakeDirRecursiveAbsolute(CONFIG_PATH);

            var configPath = $"{CONFIG_PATH}{systemId}.json";
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);

            using var file = FileAccess.Open(configPath, FileAccess.ModeFlags.Write);
            file.StoreString(json);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to save config for {systemId}: {ex.Message}", ConsoleChannel.System);
            return Task.FromResult(false);
        }
    }

    public T GetSetting<T>(string systemId, string key, T defaultValue = default)
    {
        var config = GetSystemConfig(systemId);

        if (config?.Settings != null && config.Settings.TryGetValue(key, out var value))
        {
            try
            {
                if (value is T typedValue)
                {
                    return typedValue;
                }

                // Try to convert
                var json = JsonConvert.SerializeObject(value);
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch
            {
                return defaultValue;
            }
        }

        return defaultValue;
    }

    public void SetSetting(string systemId, string key, object value)
    {
        var config = GetSystemConfig(systemId);
        if (config == null)
        {
            config = new SystemConfiguration
            {
                SystemId = systemId,
                Enabled = true,
                Settings = new Dictionary<string, object>()
            };
            _configurations[systemId] = config;
        }

        config.Settings[key] = value;
    }

    public Task<bool> LoadConfigurationAsync(string path)
    {
        try
        {
            if (!FileAccess.FileExists(path))
            {
                return Task.FromResult(false);
            }

            var json = FileAccess.GetFileAsString(path);
            var configs = JsonConvert.DeserializeObject<Dictionary<string, SystemConfiguration>>(json);

            foreach (var kvp in configs)
            {
                _configurations[kvp.Key] = kvp.Value;
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to load configuration: {ex.Message}", ConsoleChannel.System);
            return Task.FromResult(false);
        }
    }

    public Task<bool> SaveConfigurationAsync(string path)
    {
        try
        {
            var json = JsonConvert.SerializeObject(_configurations, Formatting.Indented);

            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            file.StoreString(json);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to save configuration: {ex.Message}", ConsoleChannel.System);
            return Task.FromResult(false);
        }
    }

    public void ResetToDefaults(string systemId)
    {
        _configurations.Remove(systemId);

        // Delete config file if it exists
        var configPath = $"{CONFIG_PATH}{systemId}.json";
        if (FileAccess.FileExists(configPath))
        {
            DirAccess.RemoveAbsolute(configPath);
        }
    }
}