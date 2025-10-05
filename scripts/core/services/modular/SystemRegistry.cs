using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Newtonsoft.Json;
using Waterjam.Events;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Core.Services.Modular;

/// <summary>
/// Central registry for managing modular game systems.
/// Handles loading, unloading, dependency resolution, and lifecycle management.
/// </summary>
public partial class SystemRegistry : BaseService
{
    private static SystemRegistry _instance;
    public static SystemRegistry Instance => _instance;

    // System storage
    private readonly Dictionary<string, IGameSystem> _loadedSystems = new();
    private readonly Dictionary<string, SystemManifest> _availableManifests = new();
    private readonly Dictionary<string, Node> _systemNodes = new();
    private readonly Dictionary<string, ModAssemblyLoader> _assemblyLoaders = new();

    // Services and components
    private readonly DependencyResolver _dependencyResolver = new();
    private readonly SystemContext _systemContext;
    private readonly SystemPerformanceMonitor _performanceMonitor = new();

    // Configuration
    private SystemRegistryConfig _config;
    private readonly string MANIFESTS_PATH = "res://systems/manifests/";
    private readonly string SYSTEMS_PATH = "res://systems/";
    private readonly string MODS_ROOT = "user://mods/";

    // State tracking
    private bool _isInitialized;
    private readonly HashSet<string> _loadingSystemIds = new();
    private readonly List<System.IO.FileSystemWatcher> _modWatchers = new();

    public SystemRegistry()
    {
        _systemContext = new SystemContext(this);
    }

    public override void _Ready()
    {
        base._Ready();
        _instance = this;
        // Defer initialization to avoid 'parent is busy' during root setup
        CallDeferred(nameof(BeginInitialize));
    }

    /// <summary>
    /// Get the current registry configuration
    /// </summary>
    public SystemRegistryConfig GetConfig()
    {
        return _config;
    }

    /// <summary>
    /// Save the current registry configuration to disk
    /// </summary>
    public async Task<bool> SaveConfigAsync(SystemRegistryConfig config)
    {
        try
        {
            _config = config;
            var configPath = "user://system_registry_config.json";
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);

            using var file = FileAccess.Open(configPath, FileAccess.ModeFlags.Write);
            if (file == null)
            {
                ConsoleSystem.LogErr($"[SystemRegistry] Failed to open config file for writing: {FileAccess.GetOpenError()}", ConsoleChannel.System);
                return false;
            }

            file.StoreString(json);
            ConsoleSystem.Log("[SystemRegistry] Configuration saved successfully", ConsoleChannel.System);
            return true;
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SystemRegistry] Failed to save config: {ex.Message}", ConsoleChannel.System);
            return false;
        }
    }

    private async void BeginInitialize()
    {
        await InitializeAsync();
    }

    /// <summary>
    /// Initialize the registry and discover available systems
    /// </summary>
    private async Task InitializeAsync()
    {
        ConsoleSystem.Log("[SystemRegistry] Initializing modular system registry", ConsoleChannel.System);

        try
        {
            // Load configuration
            _config = await LoadRegistryConfigAsync();

            // Discover available systems (core/game)
            await DiscoverSystemsAsync();

            // Discover mod-provided systems
            await DiscoverModSystemsAsync();

            // Load core systems
            await LoadCoreSystemsAsync();

            // Auto-load additional systems per configuration (post core)
            await AutoLoadConfiguredSystemsAsync();

            // Register console commands
            RegisterConsoleCommands();

            // Enable mod hot reload watchers if configured
            if (_config.EnableHotReload)
            {
                TrySetupModWatchers();
            }

            _isInitialized = true;
            ConsoleSystem.Log($"[SystemRegistry] Initialized with {_availableManifests.Count} available systems", ConsoleChannel.System);

            // After core systems load, emit a GameInitializedEvent to ensure subscribers are live
            GameEvent.DispatchGlobal(new GameInitializedEvent(true));
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SystemRegistry] Failed to initialize: {ex.Message}", ConsoleChannel.System);
        }
    }

    /// <summary>
    /// Load non-core systems specified in configuration AutoLoadSystems
    /// </summary>
    private async Task AutoLoadConfiguredSystemsAsync()
    {
        try
        {
            // Establish sensible defaults if config has no explicit list
            var list = (_config?.AutoLoadSystems != null && _config.AutoLoadSystems.Count > 0)
                ? _config.AutoLoadSystems
                : new System.Collections.Generic.List<string>
                {
                    // Common gameplay systems expected by tests and UI
                };

            foreach (var id in list.Distinct())
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (_loadedSystems.ContainsKey(id)) continue;

                // Compatibility: skip autoload if an equivalent node already exists under /root/GameSystems
                var systemsRoot = EnsureSystemsRoot();
                if (systemsRoot != null)
                {
                    bool ExistsByName(string name) => systemsRoot.GetNodeOrNull<Node>(name) != null;
                    if ((id == "dialogue_system" && ExistsByName("DialogueSystem"))
                        || (id == "quest_system" && ExistsByName("QuestSystem"))
                        || (id == "script_engine" && (ExistsByName("ScriptEngine") || ExistsByName("ScriptEngineSystem"))))
                    {
                        ConsoleSystem.Log($"[SystemRegistry] Skipping autoload for {id}: existing compatibility node present", ConsoleChannel.System);
                        continue;
                    }
                }

                if (!_availableManifests.ContainsKey(id))
                {
                    ConsoleSystem.LogWarn($"[SystemRegistry] AutoLoad skipped: manifest not found for {id}", ConsoleChannel.System);
                    continue;
                }

                ConsoleSystem.Log($"[SystemRegistry] Loading configured system: {id}", ConsoleChannel.System);
                var ok = await LoadSystemAsync(id);
                if (!ok)
                {
                    ConsoleSystem.LogWarn($"[SystemRegistry] Failed to auto-load system {id}", ConsoleChannel.System);
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogWarn($"[SystemRegistry] Auto-load phase failed: {ex.Message}", ConsoleChannel.System);
        }
    }

    /// <summary>
    /// Discover all available system manifests
    /// </summary>
    private async Task DiscoverSystemsAsync()
    {
        var dir = DirAccess.Open(MANIFESTS_PATH);
        if (dir == null)
        {
            ConsoleSystem.LogWarn("[SystemRegistry] Manifests directory not found, creating default", ConsoleChannel.System);
            await CreateDefaultManifestsAsync();
            return;
        }

        var files = dir.GetFiles();
        foreach (var file in files)
        {
            if (!file.EndsWith(".json")) continue;

            try
            {
                var manifestPath = $"{MANIFESTS_PATH}{file}";
                var manifestJson = FileAccess.GetFileAsString(manifestPath);
                var manifest = JsonConvert.DeserializeObject<SystemManifest>(manifestJson);

                if (ValidateManifest(manifest))
                {
                    _availableManifests[manifest.SystemId] = manifest;
                    ConsoleSystem.Log($"[SystemRegistry] Discovered system: {manifest.SystemId} v{manifest.Version}", ConsoleChannel.System);
                }
            }
            catch (Exception ex)
            {
                ConsoleSystem.LogWarn($"[SystemRegistry] Failed to load manifest {file}: {ex.Message}", ConsoleChannel.System);
            }
        }
    }

    /// <summary>
    /// Discover manifests provided by user mods under user://mods/
    /// Supports both per-mod "systems/" and "manifests/" directories.
    /// </summary>
    private Task DiscoverModSystemsAsync()
    {
        try
        {
            // Ensure mods root exists
            DirAccess.MakeDirRecursiveAbsolute(MODS_ROOT);

            var modsDir = DirAccess.Open(MODS_ROOT);
            if (modsDir == null)
            {
                ConsoleSystem.Log("[SystemRegistry] No mods directory found (user://mods)", ConsoleChannel.System);
                return Task.CompletedTask;
            }

            var modFolders = modsDir.GetDirectories();
            int discovered = 0;
            foreach (var modFolder in modFolders)
            {
                var modBase = $"{MODS_ROOT}{modFolder.TrimEnd('/')}/";
                // Attempt to mount any .pck packs under this mod
                TryMountModPacks(modBase);
                // Prefer explicit systems/ first, then manifests/
                var candidates = new[] { $"{modBase}systems/", $"{modBase}manifests/" };
                foreach (var candidate in candidates)
                {
                    var cdir = DirAccess.Open(candidate);
                    if (cdir == null) continue;
                    foreach (var file in cdir.GetFiles())
                    {
                        if (!file.EndsWith(".json")) continue;
                        var mfPath = $"{candidate}{file}";
                        try
                        {
                            var json = FileAccess.GetFileAsString(mfPath);
                            var manifest = JsonConvert.DeserializeObject<SystemManifest>(json);
                            if (!ValidateManifest(manifest)) continue;

                            // Track origin for resolving relative resource/assembly paths
                            manifest.OriginDirectory = modBase;

                            // Allow mods to override base manifests of same systemId
                            var existed = _availableManifests.ContainsKey(manifest.SystemId);
                            _availableManifests[manifest.SystemId] = manifest;
                            ConsoleSystem.Log($"[SystemRegistry] {(existed ? "Overrode" : "Discovered")} mod system: {manifest.SystemId} from {modFolder}", ConsoleChannel.System);
                            discovered++;
                        }
                        catch (Exception ex)
                        {
                            ConsoleSystem.LogWarn($"[SystemRegistry] Failed to load mod manifest at {mfPath}: {ex.Message}", ConsoleChannel.System);
                        }
                    }
                }
            }

            if (discovered > 0)
                ConsoleSystem.Log($"[SystemRegistry] Discovered {discovered} mod system manifest(s)", ConsoleChannel.System);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SystemRegistry] Mod discovery failed: {ex.Message}", ConsoleChannel.System);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Load a system by ID
    /// </summary>
    public async Task<bool> LoadSystemAsync(string systemId, bool force = false)
    {
        // Check if already loaded
        if (_loadedSystems.ContainsKey(systemId))
        {
            if (!force)
            {
                ConsoleSystem.LogWarn($"[SystemRegistry] System {systemId} is already loaded", ConsoleChannel.System);
                return true;
            }

            // Force reload - unload first
            await UnloadSystemAsync(systemId, true);
        }

        // Check if currently loading
        if (_loadingSystemIds.Contains(systemId))
        {
            ConsoleSystem.LogWarn($"[SystemRegistry] System {systemId} is already being loaded", ConsoleChannel.System);
            return false;
        }

        // Get manifest
        if (!_availableManifests.TryGetValue(systemId, out var manifest))
        {
            ConsoleSystem.LogErr($"[SystemRegistry] System {systemId} not found in available systems", ConsoleChannel.System);
            return false;
        }

        _loadingSystemIds.Add(systemId);

        try
        {
            // Resolve and load dependencies first
            var loadOrder = _dependencyResolver.ResolveDependencies(systemId, _availableManifests);

            foreach (var depId in loadOrder)
            {
                if (depId == systemId) continue; // Skip self
                if (_loadedSystems.ContainsKey(depId)) continue; // Already loaded

                ConsoleSystem.Log($"[SystemRegistry] Loading dependency {depId} for {systemId}", ConsoleChannel.System);
                var depLoaded = await LoadSystemAsync(depId, false);
                if (!depLoaded && manifest.Dependencies.Contains(depId))
                {
                    throw new SystemDependencyException(systemId, new[] { depId },
                        $"Failed to load required dependency {depId}");
                }
            }

            // Create system instance
            var system = await CreateSystemInstanceAsync(manifest);
            if (system == null)
            {
                throw new Exception($"Failed to create instance of {systemId}");
            }

            // Initialize system
            ConsoleSystem.Log($"[SystemRegistry] Initializing system {systemId}", ConsoleChannel.System);
            var initialized = await system.InitializeAsync(_systemContext);

            if (!initialized)
            {
                throw new Exception($"System {systemId} failed to initialize");
            }

            // Register system
            _loadedSystems[systemId] = system;

            // Fire loaded event
            ConsoleSystem.Log($"[SystemRegistry] System {systemId} loaded successfully", ConsoleChannel.System);
            FireSystemLoadedEvent(systemId);

            return true;
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SystemRegistry] Failed to load system {systemId}: {ex.Message}", ConsoleChannel.System);
            return false;
        }
        finally
        {
            _loadingSystemIds.Remove(systemId);
        }
    }

    /// <summary>
    /// Unload a system
    /// </summary>
    public async Task<bool> UnloadSystemAsync(string systemId, bool force = false)
    {
        if (!_loadedSystems.TryGetValue(systemId, out var system))
        {
            ConsoleSystem.LogWarn($"[SystemRegistry] System {systemId} is not loaded", ConsoleChannel.System);
            return true;
        }

        // Check if system can be unloaded
        if (!force && !system.CanUnload())
        {
            ConsoleSystem.LogWarn($"[SystemRegistry] System {systemId} cannot be unloaded (has active operations)", ConsoleChannel.System);
            return false;
        }

        // Check for dependent systems
        if (!force)
        {
            var dependents = GetDependentSystems(systemId);
            if (dependents.Any())
            {
                ConsoleSystem.LogWarn($"[SystemRegistry] Cannot unload {systemId}, required by: {string.Join(", ", dependents)}", ConsoleChannel.System);
                return false;
            }
        }

        try
        {
            ConsoleSystem.Log($"[SystemRegistry] Shutting down system {systemId}", ConsoleChannel.System);

            // Shutdown the system
            await system.ShutdownAsync();

            // Remove from registry
            _loadedSystems.Remove(systemId);

            // Remove node if exists
            if (_systemNodes.TryGetValue(systemId, out var node))
            {
                node.QueueFree();
                _systemNodes.Remove(systemId);
            }

            // Cleanup assembly loader if any
            if (_assemblyLoaders.TryGetValue(systemId, out var loader))
            {
                loader.Dispose();
                _assemblyLoaders.Remove(systemId);
            }

            // Fire unloaded event
            ConsoleSystem.Log($"[SystemRegistry] System {systemId} unloaded successfully", ConsoleChannel.System);
            FireSystemUnloadedEvent(systemId);

            return true;
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SystemRegistry] Error unloading system {systemId}: {ex.Message}", ConsoleChannel.System);
            return false;
        }
    }

    /// <summary>
    /// Get a loaded system by ID
    /// </summary>
    public IGameSystem GetSystem(string systemId)
    {
        return _loadedSystems.GetValueOrDefault(systemId);
    }

    /// <summary>
    /// Get a loaded system by type
    /// </summary>
    public T GetSystem<T>() where T : class, IGameSystem
    {
        return _loadedSystems.Values.OfType<T>().FirstOrDefault();
    }

    /// <summary>
    /// Check if a system is loaded
    /// </summary>
    public bool IsSystemLoaded(string systemId)
    {
        return _loadedSystems.ContainsKey(systemId);
    }

    /// <summary>
    /// Get all available system manifests (for UI display)
    /// </summary>
    public List<SystemManifest> GetAvailableManifests()
    {
        return _availableManifests.Values.ToList();
    }

    /// <summary>
    /// Get a specific manifest by system ID
    /// </summary>
    public SystemManifest GetManifest(string systemId)
    {
        return _availableManifests.GetValueOrDefault(systemId);
    }

    /// <summary>
    /// Get all available system manifests
    /// </summary>
    public IEnumerable<SystemManifest> GetAvailableSystemManifests()
    {
        return _availableManifests.Values;
    }

    /// <summary>
    /// Get all loaded systems
    /// </summary>
    public IEnumerable<IGameSystem> GetLoadedSystems()
    {
        return _loadedSystems.Values;
    }

    /// <summary>
    /// Get systems that depend on the specified system
    /// </summary>
    private IEnumerable<string> GetDependentSystems(string systemId)
    {
        return _loadedSystems
            .Where(kvp => kvp.Value.Dependencies.Contains(systemId))
            .Select(kvp => kvp.Key);
    }

    /// <summary>
    /// Create a system instance from manifest
    /// </summary>
    private Task<IGameSystem> CreateSystemInstanceAsync(SystemManifest manifest)
    {
        try
        {
            // Ensure compatibility systems root exists
            var systemsRoot = EnsureSystemsRoot();
            // Load the system scene or script
            if (!string.IsNullOrEmpty(manifest.ScenePath))
            {
                var scene = GD.Load<PackedScene>(manifest.ScenePath);
                if (scene == null)
                {
                    throw new Exception($"Failed to load scene: {manifest.ScenePath}");
                }

                var node = scene.Instantiate();
                if (node is IGameSystem system)
                {
                    // Parent under /root/GameSystems for backwards compatibility
                    systemsRoot.AddChild(node);
                    // Stable naming for tests and tools
                    if (string.IsNullOrEmpty(node.Name)) node.Name = node.GetType().Name;
                    _systemNodes[manifest.SystemId] = node;
                    return Task.FromResult(system);
                }

                throw new Exception($"Scene root does not implement IGameSystem: {manifest.ScenePath}");
            }
            else if (!string.IsNullOrEmpty(manifest.ScriptPath))
            {
                // Prefer instantiating managed C# types directly when ScriptPath points to a .cs file.
                // This avoids SetScript, which does not produce a typed instance for C# scripts.
                Node instance = null;
                try
                {
                    var className = System.IO.Path.GetFileNameWithoutExtension(manifest.ScriptPath);
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    var type = assembly.GetTypes()
                        .FirstOrDefault(t => typeof(Node).IsAssignableFrom(t)
                                             && typeof(IGameSystem).IsAssignableFrom(t)
                                             && string.Equals(t.Name, className, System.StringComparison.Ordinal));
                    if (type != null)
                    {
                        instance = System.Activator.CreateInstance(type) as Node;
                    }
                }
                catch
                {
                    // fall through to resource-based load below
                }

                if (instance == null)
                {
                    // Managed C# scripts must be instantiated via reflection; SetScript is not supported.
                    throw new Exception($"Failed to instantiate managed script type for {manifest.ScriptPath}. Ensure class name matches filename and implements IGameSystem.");
                }

                if (instance is IGameSystem managedSystem)
                {
                    systemsRoot.AddChild(instance);
                    if (string.IsNullOrEmpty(instance.Name)) instance.Name = instance.GetType().Name;
                    _systemNodes[manifest.SystemId] = instance;
                    return Task.FromResult(managedSystem);
                }

                // If we reached here, we could not create a valid IGameSystem instance
                instance?.QueueFree();
                throw new Exception($"Script does not implement IGameSystem: {manifest.ScriptPath}");
            }
            else if (!string.IsNullOrEmpty(manifest.AssemblyName) && !string.IsNullOrEmpty(manifest.TypeName))
            {
                if (!_config.AllowCodeMods)
                {
                    throw new Exception("Code mods are disabled by configuration (AllowCodeMods=false)");
                }

                // Assembly-based system (mod code)
                // Resolve assembly path: prefer mod bin/ under OriginDirectory
                var baseDir = manifest.OriginDirectory ?? string.Empty;
                var asmPath = System.IO.Path.Combine(baseDir, "bin", manifest.AssemblyName + ".dll");

                // Convert Godot path to absolute filesystem path if needed
                var absoluteAsmPath = asmPath;
                if (asmPath.StartsWith("user://") || asmPath.StartsWith("res://"))
                {
                    absoluteAsmPath = ProjectSettings.GlobalizePath(asmPath);
                }

                if (!System.IO.File.Exists(absoluteAsmPath))
                {
                    throw new Exception($"Assembly not found at {absoluteAsmPath} (Godot path: {asmPath})");
                }

                // Load via ModAssemblyLoader
                if (_assemblyLoaders.TryGetValue(manifest.SystemId, out var existing))
                {
                    existing.Dispose();
                    _assemblyLoaders.Remove(manifest.SystemId);
                }

                var loader = new ModAssemblyLoader(absoluteAsmPath);
                _assemblyLoaders[manifest.SystemId] = loader;

                var instance = loader.CreateInstance(manifest.TypeName) as Node;
                if (instance == null)
                {
                    throw new Exception($"Type {manifest.TypeName} did not create a Node instance");
                }

                if (instance is not IGameSystem sys)
                {
                    instance.QueueFree();
                    throw new Exception($"Type {manifest.TypeName} does not implement IGameSystem");
                }

                systemsRoot.AddChild(instance);
                if (string.IsNullOrEmpty(instance.Name)) instance.Name = instance.GetType().Name;
                _systemNodes[manifest.SystemId] = instance;
                return Task.FromResult(sys);
            }

            throw new Exception("Manifest must specify either ScenePath or ScriptPath");
        }
        catch (Exception ex)
        {
            // Log full exception with type and stack to aid diagnosing mod load failures
            ConsoleSystem.LogErr($"[SystemRegistry] Failed to create system instance for {manifest.SystemId}: {ex.GetType().Name}: {ex.Message}\n{ex}", ConsoleChannel.System);
            return Task.FromResult<IGameSystem>(null);
        }
    }

    private Node EnsureSystemsRoot()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        var root = tree?.Root;
        var systems = root?.GetNodeOrNull<Node>("GameSystems");
        if (systems == null && root != null)
        {
            // Try to instantiate legacy GameSystems scene for compatibility if present
            try
            {
                PackedScene prefab = null;
                try
                {
                    if (FileAccess.FileExists("res://scenes/prefabs/GameSystems.tscn"))
                        prefab = GD.Load<PackedScene>("res://scenes/prefabs/GameSystems.tscn");
                }
                catch { }

                if (prefab != null)
                {
                    var inst = prefab.Instantiate<Node>();
                    if (inst.Name != "GameSystems") inst.Name = "GameSystems";
                    root.CallDeferred(Node.MethodName.AddChild, inst);
                    systems = inst;
                }
            }
            catch
            {
                // Fallback to an empty container if prefab is unavailable
            }

            systems ??= new Node { Name = "GameSystems" };
            if (systems.GetParent() == null) root.CallDeferred(Node.MethodName.AddChild, systems);
        }

        return systems ?? this; // Fallback to registry if root is unavailable
    }

    /// <summary>
    /// Load core systems that cannot be disabled
    /// </summary>
    private async Task LoadCoreSystemsAsync()
    {
        var coreSystems = _availableManifests.Values
            .Where(m => m.IsCore)
            .OrderBy(m => m.LoadPriority);

        foreach (var manifest in coreSystems)
        {
            ConsoleSystem.Log($"[SystemRegistry] Loading core system: {manifest.SystemId}", ConsoleChannel.System);
            var loaded = await LoadSystemAsync(manifest.SystemId);

            if (!loaded)
            {
                ConsoleSystem.LogErr($"[SystemRegistry] Failed to load core system {manifest.SystemId} - game may not function correctly", ConsoleChannel.System);
            }
        }
    }

    /// <summary>
    /// Validate a system manifest
    /// </summary>
    private bool ValidateManifest(SystemManifest manifest)
    {
        if (manifest == null) return false;
        if (string.IsNullOrEmpty(manifest.SystemId)) return false;
        if (string.IsNullOrEmpty(manifest.DisplayName)) return false;
        // Allow one of: ScenePath, ScriptPath, or (AssemblyName + TypeName)
        var hasScene = !string.IsNullOrEmpty(manifest.ScenePath);
        var hasScript = !string.IsNullOrEmpty(manifest.ScriptPath);
        var hasAssembly = !string.IsNullOrEmpty(manifest.AssemblyName) && !string.IsNullOrEmpty(manifest.TypeName);
        if (!(hasScene || hasScript || hasAssembly)) return false;

        return true;
    }

    /// <summary>
    /// Load registry configuration
    /// </summary>
    private Task<SystemRegistryConfig> LoadRegistryConfigAsync()
    {
        var configPath = "user://system_registry_config.json";

        if (FileAccess.FileExists(configPath))
        {
            try
            {
                var json = FileAccess.GetFileAsString(configPath);
                return Task.FromResult(JsonConvert.DeserializeObject<SystemRegistryConfig>(json));
            }
            catch (Exception ex)
            {
                ConsoleSystem.LogWarn($"[SystemRegistry] Failed to load config: {ex.Message}", ConsoleChannel.System);
            }
        }

        // Return default config
        return Task.FromResult(new SystemRegistryConfig
        {
            AutoLoadCoreSystems = true,
            EnableHotReload = OS.IsDebugBuild(),
            MaxConcurrentLoads = 3,
            PerformanceMonitoringEnabled = true,
            AllowCodeMods = true, // Enable code mods by default
            AutoLoadSystems = new List<string>
            {
                "script_engine",
                "dialogue_system",
                "quest_system",
                "simulation_v2_system",
                "interaction_glow_system"
            }
        });
    }

    /// <summary>
    /// Create default manifests for existing systems
    /// </summary>
    private Task CreateDefaultManifestsAsync()
    {
        // This would create manifests for existing systems during migration
        // For now, we'll just log that we need to create them
        ConsoleSystem.Log("[SystemRegistry] Default manifests need to be created for existing systems", ConsoleChannel.System);
        return Task.CompletedTask;
    }

    private void TrySetupModWatchers()
    {
        try
        {
            var absModsPath = ProjectSettings.GlobalizePath(MODS_ROOT);
            if (!System.IO.Directory.Exists(absModsPath))
            {
                return;
            }

            // Watch all mod subdirectories for DLL and manifest changes
            foreach (var dir in System.IO.Directory.GetDirectories(absModsPath))
            {
                var watcher = new System.IO.FileSystemWatcher(dir)
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true,
                    Filter = "*.*"
                };
                watcher.Changed += OnModFilesChanged;
                watcher.Created += OnModFilesChanged;
                watcher.Deleted += OnModFilesChanged;
                watcher.Renamed += OnModFilesRenamed;
                _modWatchers.Add(watcher);
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogWarn($"[SystemRegistry] Failed to initialize mod hot-reload watchers: {ex.Message}", ConsoleChannel.System);
        }
    }

    private void OnModFilesChanged(object sender, System.IO.FileSystemEventArgs e)
    {
        // Only react to manifest and DLL changes for now
        var ext = System.IO.Path.GetExtension(e.FullPath).ToLowerInvariant();
        if (ext == ".json" || ext == ".dll")
        {
            ConsoleSystem.Log($"[SystemRegistry] Mod file change detected: {e.ChangeType} {e.FullPath}", ConsoleChannel.System);
            // Simple strategy: refresh manifest cache and attempt reload of affected systems
            // Extract ModId from path
            var modsAbs = ProjectSettings.GlobalizePath(MODS_ROOT);
            var rel = e.FullPath.Replace(modsAbs, string.Empty).TrimStart(System.IO.Path.DirectorySeparatorChar);
            var parts = rel.Split(System.IO.Path.DirectorySeparatorChar, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                var modId = parts[0];
                // Re-discover this mod's manifests on idle
                CallDeferred("RefreshModManifests", modId);
                if (ext == ".dll")
                {
                    // Also try reloading systems from this mod
                    CallDeferred("ReloadSystemsForMod", modId);
                }
            }
        }
    }

    private void OnModFilesRenamed(object sender, System.IO.RenamedEventArgs e)
    {
        OnModFilesChanged(sender,
            new System.IO.FileSystemEventArgs(System.IO.WatcherChangeTypes.Changed, System.IO.Path.GetDirectoryName(e.FullPath) ?? string.Empty, System.IO.Path.GetFileName(e.FullPath)));
    }

    private void RefreshModManifests(string modId)
    {
        try
        {
            var modBase = $"{MODS_ROOT}{modId.TrimEnd('/')}/";
            TryMountModPacks(modBase);
            var candidates = new[] { $"{modBase}systems/", $"{modBase}manifests/" };
            foreach (var candidate in candidates)
            {
                var cdir = DirAccess.Open(candidate);
                if (cdir == null) continue;
                foreach (var file in cdir.GetFiles())
                {
                    if (!file.EndsWith(".json")) continue;
                    var mfPath = $"{candidate}{file}";
                    try
                    {
                        var json = FileAccess.GetFileAsString(mfPath);
                        var manifest = JsonConvert.DeserializeObject<SystemManifest>(json);
                        if (!ValidateManifest(manifest)) continue;
                        manifest.OriginDirectory = modBase;
                        _availableManifests[manifest.SystemId] = manifest;
                        ConsoleSystem.Log($"[SystemRegistry] Refreshed mod system manifest: {manifest.SystemId} from {modId}", ConsoleChannel.System);
                    }
                    catch (Exception ex)
                    {
                        ConsoleSystem.LogWarn($"[SystemRegistry] Failed to refresh mod manifest at {mfPath}: {ex.Message}", ConsoleChannel.System);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogWarn($"[SystemRegistry] Failed to refresh manifests for mod {modId}: {ex.Message}", ConsoleChannel.System);
        }
    }

    private async void ReloadSystemsForMod(string modId)
    {
        try
        {
            var modBase = $"{MODS_ROOT}{modId.TrimEnd('/')}/";
            // Find loaded systems that originate from this mod and use assemblies
            var targets = _loadedSystems.Keys
                .Where(id => _availableManifests.TryGetValue(id, out var m)
                             && string.Equals(m.OriginDirectory, modBase, StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrEmpty(m.AssemblyName)
                             && !string.IsNullOrEmpty(m.TypeName))
                .ToList();

            if (!targets.Any()) return;

            ConsoleSystem.Log($"[SystemRegistry] Hot-reloading systems for mod {modId}: {string.Join(", ", targets)}", ConsoleChannel.System);

            foreach (var id in targets)
            {
                await UnloadSystemAsync(id, true);
                await LoadSystemAsync(id, true);
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogWarn($"[SystemRegistry] Failed to hot-reload systems for mod {modId}: {ex.Message}", ConsoleChannel.System);
        }
    }

    private void TryMountModPacks(string modBase)
    {
        try
        {
            var absBase = ProjectSettings.GlobalizePath(modBase);
            if (!System.IO.Directory.Exists(absBase)) return;
            foreach (var pck in System.IO.Directory.EnumerateFiles(absBase, "*.pck", System.IO.SearchOption.TopDirectoryOnly))
            {
                if (ProjectSettings.LoadResourcePack(pck, replaceFiles: true))
                {
                    ConsoleSystem.Log($"[SystemRegistry] Mounted mod pack: {pck}", ConsoleChannel.System);
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogWarn($"[SystemRegistry] Failed to mount mod packs under {modBase}: {ex.Message}", ConsoleChannel.System);
        }
    }

    /// <summary>
    /// Register console commands for system management
    /// </summary>
    private void RegisterConsoleCommands()
    {
        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "systems",
            "List all available systems",
            "systems",
            args =>
            {
                foreach (var manifest in _availableManifests.Values)
                {
                    var status = _loadedSystems.ContainsKey(manifest.SystemId) ? "LOADED" : "AVAILABLE";
                    ConsoleSystem.Log($"  {manifest.SystemId} v{manifest.Version} [{status}] - {manifest.DisplayName}", ConsoleChannel.System);
                }

                return Task.FromResult(true);
            }
        ));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "systems_load",
            "Load a system",
            "systems_load <systemId>",
            async args =>
            {
                if (args.Length < 1)
                {
                    ConsoleSystem.LogErr("Usage: systems_load <systemId>", ConsoleChannel.System);
                    return false;
                }

                var result = await LoadSystemAsync(args[0]);
                if (!result)
                {
                    ConsoleSystem.LogErr($"Failed to load system {args[0]}", ConsoleChannel.System);
                    return false;
                }

                return true;
            }
        ));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "systems_unload",
            "Unload a system",
            "systems_unload <systemId>",
            async args =>
            {
                if (args.Length < 1)
                {
                    ConsoleSystem.LogErr("Usage: systems_unload <systemId>", ConsoleChannel.System);
                    return false;
                }

                var result = await UnloadSystemAsync(args[0]);
                if (!result)
                {
                    ConsoleSystem.LogErr($"Failed to unload system {args[0]}", ConsoleChannel.System);
                    return false;
                }

                return true;
            }
        ));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "systems_status",
            "Show loaded systems status",
            "systems_status",
            args =>
            {
                ConsoleSystem.Log("=== Loaded Systems ===", ConsoleChannel.System);
                foreach (var system in _loadedSystems.Values)
                {
                    var metrics = system.GetMetrics();
                    ConsoleSystem.Log($"  {system.SystemId}: {system.State} | Mem: {metrics.MemoryUsageBytes / 1024}KB | CPU: {metrics.CpuTimeMs:F2}ms", ConsoleChannel.System);
                }

                return Task.FromResult(true);
            }
        ));

        // Mod commands
        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "mods",
            "List discovered mod systems and their origins",
            "mods",
            args =>
            {
                ConsoleSystem.Log("=== Mod Systems ===", ConsoleChannel.System);
                foreach (var m in _availableManifests.Values.Where(m => !string.IsNullOrEmpty(m.OriginDirectory)))
                {
                    var status = _loadedSystems.ContainsKey(m.SystemId) ? "LOADED" : "AVAILABLE";
                    ConsoleSystem.Log($"  {m.SystemId} [{status}] from {m.OriginDirectory}", ConsoleChannel.System);
                }

                return Task.FromResult(true);
            }
        ));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "mod_reload",
            "Reload a system (unload then load). Usage: mod_reload <systemId>",
            "mod_reload <systemId>",
            async args =>
            {
                if (args.Length < 1)
                {
                    ConsoleSystem.LogErr("Usage: mod_reload <systemId>", ConsoleChannel.System);
                    return false;
                }

                var id = args[0];
                await UnloadSystemAsync(id, true);
                var ok = await LoadSystemAsync(id, true);
                return ok;
            }
        ));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "mod_package",
            "Zip a mod directory. Usage: mod_package <ModId>",
            "mod_package <ModId>",
            async args =>
            {
                if (args.Length < 1)
                {
                    ConsoleSystem.LogErr("Usage: mod_package <ModId>", ConsoleChannel.System);
                    return false;
                }

                var modId = args[0];
                var modBase = $"{MODS_ROOT}{modId.TrimEnd('/')}/";
                var absBase = ProjectSettings.GlobalizePath(modBase);
                if (!System.IO.Directory.Exists(absBase))
                {
                    ConsoleSystem.LogErr($"Mod folder not found: {modBase}", ConsoleChannel.System);
                    return false;
                }

                var outputDir = ProjectSettings.GlobalizePath("user://");
                var zipPath = System.IO.Path.Combine(outputDir, modId + ".zip");
                try
                {
                    if (System.IO.File.Exists(zipPath)) System.IO.File.Delete(zipPath);
                    System.IO.Compression.ZipFile.CreateFromDirectory(absBase, zipPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
                    ConsoleSystem.Log($"Packaged mod to: {zipPath}", ConsoleChannel.System);
                    return true;
                }
                catch (Exception ex)
                {
                    ConsoleSystem.LogErr($"Failed to package mod: {ex.Message}", ConsoleChannel.System);
                    return false;
                }
            }
        ));
    }

    private void FireSystemLoadedEvent(string systemId)
    {
        // Fire event to notify other systems
        // GameEvent.DispatchGlobal(new SystemLoadedEvent(systemId));
    }

    private void FireSystemUnloadedEvent(string systemId)
    {
        // Fire event to notify other systems  
        // GameEvent.DispatchGlobal(new SystemUnloadedEvent(systemId));
    }
}

/// <summary>
/// Configuration for the system registry
/// </summary>
public class SystemRegistryConfig
{
    public bool AutoLoadCoreSystems { get; set; } = true;
    public bool EnableHotReload { get; set; } = false;
    public int MaxConcurrentLoads { get; set; } = 3;
    public bool PerformanceMonitoringEnabled { get; set; } = true;
    public List<string> AutoLoadSystems { get; set; } = new();
    public bool AllowCodeMods { get; set; } = false;
}