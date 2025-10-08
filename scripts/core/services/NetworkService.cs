using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using GodotSteam;
using Waterjam.Events;
using Waterjam.Core.Systems.Console;
using Waterjam.Core.Services.Network;
using Waterjam.Game;
using Waterjam.Game.Services.Voice;

namespace Waterjam.Core.Services;

/// <summary>
/// Manages networking for both client and server modes.
/// Handles connections, message routing, and network state management.
/// </summary>
public partial class NetworkService : BaseService,
    IGameEventHandler<GameInitializedEvent>,
    IGameEventHandler<SceneLoadEvent>,
    IGameEventHandler<NewGameStartedEvent>
{
    // Configuration
    private NetworkConfig _config;
    private NetworkMode _mode = NetworkMode.None;

    // Pluggable backend adapter
    private INetworkAdapter _adapter;

    // Server state
    private readonly Dictionary<long, ClientState> _connectedClients = new();
    private ulong _currentTick;
    private double _tickAccumulator;
    private const double TICK_RATE = 30.0; // 30 Hz simulation
    private const double TICK_INTERVAL = 1.0 / TICK_RATE;

    // Client state
    private long _clientId = -1;
    private readonly Queue<InputPacket> _inputBuffer = new();
    private ulong _lastProcessedInput;

    // Protocol versioning
    public const string PROTOCOL_VERSION = "0.1.0";
    public static readonly string[] SUPPORTED_PROTOCOLS = { "0.1.0" };

    // Player management
    private readonly Dictionary<long, PlayerEntity> _networkedPlayers = new();

    public bool IsServer => _mode == NetworkMode.Server || _mode == NetworkMode.LocalServer;
    public bool IsClient => _mode == NetworkMode.Client || _mode == NetworkMode.LocalServer;
    public new bool IsConnected => _adapter != null && _adapter.Peer != null && _adapter.Peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;
    public NetworkBackend CurrentBackend => _config.Backend;
    public NetworkMode Mode => _mode;
    public ulong CurrentTick => _currentTick;
    public IReadOnlyDictionary<long, PlayerEntity> NetworkedPlayers => _networkedPlayers;

    public override void _Ready()
    {
        base._Ready();
        _config = LoadNetworkConfig();

        InitializeAdapter();

        // Register console commands
        RegisterConsoleCommands();

        ConsoleSystem.Log("[NetworkService] Initialized", ConsoleChannel.Network);
    }

    public override void _Process(double delta)
    {
        // Update Steam adapter if it's the current backend
        if (_adapter is SteamNetworkAdapter steamAdapter)
        {
            steamAdapter.Update();
        }

        if (!IsServer) return;

        // Run fixed-tick simulation loop on server
        _tickAccumulator += delta;
        while (_tickAccumulator >= TICK_INTERVAL)
        {
            _tickAccumulator -= TICK_INTERVAL;
            ServerTick();
        }
    }

    public void OnGameEvent(GameInitializedEvent eventArgs)
    {
        // Auto-start local server for singleplayer if configured
        if (_config.AutoStartLocalServer && _mode == NetworkMode.None)
        {
            StartLocalServer();
        }
    }

    public void OnGameEvent(SceneLoadEvent eventArgs)
    {
        // When a new gameplay scene is loaded in multiplayer, spawn players for all connected peers
        if (IsServer && !string.Equals(eventArgs.ScenePath, "res://scenes/ui/MainMenu.tscn", System.StringComparison.OrdinalIgnoreCase))
        {
            ConsoleSystem.Log($"[NetworkService] Scene loaded in multiplayer: {eventArgs.ScenePath}, spawning all players (connected={_connectedClients.Count})", ConsoleChannel.Network);
            
            // Give the scene a moment to initialize - use CallDeferred to ensure we're in the proper context
            CallDeferred(MethodName.SpawnAllPlayers);
        }
    }

    public void OnGameEvent(NewGameStartedEvent eventArgs)
    {
        // When the game starts in multiplayer, the server broadcasts the scene to load to all clients
        // Note: GameService handles the local scene load, we only need to broadcast to clients here
        if (IsServer)
        {
            // Only send RPC if we have an active multiplayer peer (i.e., not a solo lobby)
            var multiplayer = GetTree()?.GetMultiplayer();
            if (multiplayer?.MultiplayerPeer != null && multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
            {
                ConsoleSystem.Log($"[NetworkService] Broadcasting scene load to all clients: {eventArgs.LevelScenePath}", ConsoleChannel.Network);
                Rpc(MethodName.RpcLoadScene, eventArgs.LevelScenePath);
            }
            else
            {
                ConsoleSystem.Log($"[NetworkService] No multiplayer peer active, skipping RPC broadcast (solo lobby)", ConsoleChannel.Network);
            }
        }
    }

    // Removed legacy LobbyStartedEvent handler during Party migration

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcLoadScene(string scenePath)
    {
        ConsoleSystem.Log($"[NetworkService] Received RPC to load scene: {scenePath}", ConsoleChannel.Network);
        
        // Dispatch scene load request on this client
        GameEvent.DispatchGlobal(new SceneLoadRequestedEvent(scenePath));
    }

    private void SpawnAllPlayers()
    {
        if (!IsServer) return;

        ConsoleSystem.Log($"[NetworkService] Spawning all players after scene load; server will spawn {1 + _connectedClients.Keys.Count(k => k != 1)} players", ConsoleChannel.Network);

        // Spawn local player (peer ID 1 for server)
        SpawnPlayerForClient(1);
        
        // Spawn all connected remote clients
        foreach (var clientId in _connectedClients.Keys.ToArray())
        {
            if (clientId != 1) // Skip server/local player
            {
                SpawnPlayerForClient(clientId);
            }
        }
    }

    #region Server Methods

    /// <summary>
    /// Start a dedicated server on the specified port
    /// </summary>
    public bool StartServer(int port = 0)
    {
        if (_mode != NetworkMode.None)
        {
            ConsoleSystem.LogErr("[NetworkService] Cannot start server: already in network mode", ConsoleChannel.Network);
            return false;
        }

        port = port > 0 ? port : _config.ServerPort;

        try
        {
            // Preserve existing adapter configuration (e.g., preferred Steam lobby)
            // Only (re)initialize if we don't have an adapter yet or backend changed
            if (_adapter == null || _adapter.Backend != _config.Backend)
            {
                InitializeAdapter();
            }

            if (_adapter == null)
            {
                ConsoleSystem.LogErr("[NetworkService] No network adapter available", ConsoleChannel.Network);
                return false;
            }

            var ok = _adapter.StartServer(port, _config.MaxPlayers);
            if (!ok)
            {
                ConsoleSystem.LogErr("[NetworkService] Failed to start server (adapter)", ConsoleChannel.Network);
                return false;
            }

            GetTree().GetMultiplayer().MultiplayerPeer = _adapter.Peer;
            _mode = NetworkMode.Server;

            // Connect signals
            GetTree().GetMultiplayer().PeerConnected += OnPeerConnected;
            GetTree().GetMultiplayer().PeerDisconnected += OnPeerDisconnected;

            ConsoleSystem.Log($"[NetworkService] Server started on port {port}", ConsoleChannel.Network);
            GameEvent.DispatchGlobal(new NetworkServerStartedEvent(port));

            return true;
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[NetworkService] Failed to start server: {ex.Message}", ConsoleChannel.Network);
            _adapter?.Disconnect();
            return false;
        }
    }

    /// <summary>
    /// Configure adapter to reuse an existing Steam lobby if possible.
    /// Call before StartServer when a party lobby exists.
    /// </summary>
    public void ConfigureSteamLobbyReuse(ulong lobbyId)
    {
        if (_adapter is SteamNetworkAdapter steam)
        {
            if (lobbyId != 0)
            {
                steam.SetPreferredLobbyId(lobbyId);
                ConsoleSystem.Log($"[NetworkService] Configured Steam adapter to reuse lobby {lobbyId}", ConsoleChannel.Network);
            }
        }
    }

    /// <summary>
    /// Start a local server for singleplayer (acts as both client and server)
    /// </summary>
    public bool StartLocalServer()
    {
        if (_mode != NetworkMode.None)
        {
            ConsoleSystem.LogErr("[NetworkService] Cannot start local server: already in network mode", ConsoleChannel.Network);
            return false;
        }

        try
        {
            // For local server, we use offline mode but with server authority
            _mode = NetworkMode.LocalServer;
            _clientId = 1; // Local player is client ID 1
            _currentTick = 0;

            ConsoleSystem.Log("[NetworkService] Local server started (singleplayer mode)", ConsoleChannel.Network);
            GameEvent.DispatchGlobal(new NetworkLocalServerStartedEvent());

            return true;
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[NetworkService] Failed to start local server: {ex.Message}", ConsoleChannel.Network);
            return false;
        }
    }

    private void ServerTick()
    {
        _currentTick++;

        // Process queued inputs from clients
        // TODO: Process input packets

        // Run simulation
        GameEvent.DispatchGlobal(new NetworkServerTickEvent(_currentTick));

        // Send snapshots to clients (at reduced rate, e.g., every 1-2 ticks for 20Hz)
        if (_currentTick % 2 == 0) // 15 Hz snapshot rate
        {
            SendSnapshotsToClients();
        }
    }

    private void SendSnapshotsToClients()
    {
        // TODO: Gather world state and send to clients
        // This will be implemented with the state replication system
    }

    private void OnPeerConnected(long peerId)
    {
        ConsoleSystem.Log($"[NetworkService] Peer connected: {peerId}", ConsoleChannel.Network);

        var clientState = new ClientState
        {
            PeerId = peerId,
            ConnectedAt = Time.GetTicksMsec(),
            LastHeartbeat = Time.GetTicksMsec()
        };

        _connectedClients[peerId] = clientState;
        GameEvent.DispatchGlobal(new NetworkClientConnectedEvent(peerId));

        // Send handshake
        SendHandshake(peerId);
    }

    private void OnPeerDisconnected(long peerId)
    {
        ConsoleSystem.Log($"[NetworkService] Peer disconnected: {peerId}", ConsoleChannel.Network);

        // Remove the player entity
        RemovePlayerForClient(peerId);

        _connectedClients.Remove(peerId);
        GameEvent.DispatchGlobal(new NetworkClientDisconnectedEvent(peerId));
    }

    private void SendHandshake(long peerId)
    {
        // Acknowledge connection
        ConsoleSystem.Log($"[NetworkService] Sending handshake to peer {peerId}", ConsoleChannel.Network);

        // Spawn player entity for the new client
        SpawnPlayerForClient(peerId);
    }

    /// <summary>
    /// Spawns a player entity for a newly connected client.
    /// </summary>
    private void SpawnPlayerForClient(long peerId)
    {
        if (!IsServer) return;

        try
        {
            // Verify we have a valid scene tree
            var tree = GetTree();
            if (tree == null)
            {
                ConsoleSystem.LogErr("[NetworkService] GetTree() returned null, cannot spawn player", ConsoleChannel.Network);
                return;
            }

            var currentScene = tree.CurrentScene;
            if (currentScene == null)
            {
                ConsoleSystem.LogErr("[NetworkService] CurrentScene is null, cannot spawn player", ConsoleChannel.Network);
                return;
            }

            // Find a spawn point (for now, just use origin with some offset)
            var spawnPosition = new Vector3(peerId * 2, 2, 0);

            // Load the player scene
            var playerScene = GD.Load<PackedScene>("res://scenes/Player.tscn");
            if (playerScene == null)
            {
                ConsoleSystem.LogErr("[NetworkService] Could not load Player scene", ConsoleChannel.Network);
                return;
            }

            // Instance the player
            var playerInstance = playerScene.Instantiate<PlayerEntity>();
            if (playerInstance == null)
            {
                ConsoleSystem.LogErr("[NetworkService] Failed to instantiate player", ConsoleChannel.Network);
                return;
            }

            // Set player properties
            playerInstance.Name = $"Player_{peerId}";
            
            // Set network authority BEFORE adding to tree
            try
            {
                playerInstance.SetMultiplayerAuthority((int)peerId);
                ConsoleSystem.Log($"[NetworkService] Set multiplayer authority for peer {peerId}", ConsoleChannel.Network);
            }
            catch (Exception authEx)
            {
                ConsoleSystem.LogErr($"[NetworkService] Failed to set multiplayer authority: {authEx.Message}", ConsoleChannel.Network);
            }

            // Add to the scene tree
            currentScene.AddChild(playerInstance);

            // Set position after adding to tree
            playerInstance.GlobalPosition = spawnPosition;

            // Track the player
            _networkedPlayers[peerId] = playerInstance;

            ConsoleSystem.Log($"[NetworkService] Spawned player for peer {peerId} at {spawnPosition}", ConsoleChannel.Network);

            // Register player with voice chat service
            var voiceChatService = GetNodeOrNull<VoiceChatService>("/root/VoiceChatService");
            if (voiceChatService != null)
            {
                voiceChatService.RegisterNetworkPlayer(peerId, playerInstance);
            }

            // Notify other clients about the new player
            GameEvent.DispatchGlobal(new NetworkPlayerSpawnedEvent(peerId, spawnPosition, $"Player_{peerId}"));

            // Broadcast spawn to clients so they instantiate their local proxies
            var multiplayer = GetTree()?.GetMultiplayer();
            if (multiplayer?.MultiplayerPeer != null && multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
            {
                Rpc(MethodName.RpcClientSpawnPlayer, (int)peerId, spawnPosition, playerInstance.Name);
                ConsoleSystem.Log($"[NetworkService] Broadcasted RpcClientSpawnPlayer for peer {peerId}", ConsoleChannel.Network);
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[NetworkService] Failed to spawn player for peer {peerId}: {ex.Message}", ConsoleChannel.Network);
        }
    }

    /// <summary>
    /// Removes a player entity when a client disconnects.
    /// </summary>
    private void RemovePlayerForClient(long peerId)
    {
        if (_networkedPlayers.TryGetValue(peerId, out var player))
        {
            // Unregister from voice chat service first
            var voiceChatService = GetNodeOrNull<VoiceChatService>("/root/VoiceChatService");
            if (voiceChatService != null)
            {
                voiceChatService.UnregisterNetworkPlayer(peerId);
            }

            player.QueueFree();
            _networkedPlayers.Remove(peerId);

            ConsoleSystem.Log($"[NetworkService] Removed player for peer {peerId}", ConsoleChannel.Network);

            // Notify other clients about the player leaving
            GameEvent.DispatchGlobal(new NetworkPlayerRemovedEvent(peerId));

            // Broadcast despawn to clients
            var multiplayer = GetTree()?.GetMultiplayer();
            if (multiplayer?.MultiplayerPeer != null && multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
            {
                Rpc(MethodName.RpcClientRemovePlayer, (int)peerId);
                ConsoleSystem.Log($"[NetworkService] Broadcasted RpcClientRemovePlayer for peer {peerId}", ConsoleChannel.Network);
            }
        }
    }

    /// <summary>
    /// Gets a player entity by peer ID.
    /// </summary>
    public PlayerEntity GetPlayerEntity(long peerId)
    {
        return _networkedPlayers.GetValueOrDefault(peerId);
    }

    #endregion

    #region Client Methods

    /// <summary>
    /// Connect to a server as a client
    /// </summary>
    public bool ConnectToServer(string address, int port = 0)
    {
        if (_mode != NetworkMode.None)
        {
            ConsoleSystem.LogErr("[NetworkService] Cannot connect: already in network mode", ConsoleChannel.Network);
            return false;
        }

        port = port > 0 ? port : _config.ServerPort;

        try
        {
            InitializeAdapter();

            if (_adapter == null)
            {
                ConsoleSystem.LogErr("[NetworkService] No network adapter available", ConsoleChannel.Network);
                return false;
            }

            var ok = _adapter.Connect(address, port);
            if (!ok)
            {
                ConsoleSystem.LogErr("[NetworkService] Failed to connect (adapter)", ConsoleChannel.Network);
                return false;
            }

            GetTree().GetMultiplayer().MultiplayerPeer = _adapter.Peer;
            _mode = NetworkMode.Client;

            // Connect signals
            GetTree().GetMultiplayer().ConnectedToServer += OnConnectedToServer;
            GetTree().GetMultiplayer().ConnectionFailed += OnConnectionFailed;
            GetTree().GetMultiplayer().ServerDisconnected += OnServerDisconnected;

            ConsoleSystem.Log($"[NetworkService] Connecting to {address}:{port}...", ConsoleChannel.Network);

            return true;
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[NetworkService] Failed to connect: {ex.Message}", ConsoleChannel.Network);
            _adapter?.Disconnect();
            return false;
        }
    }

    private void OnConnectedToServer()
    {
        _clientId = GetTree().GetMultiplayer().GetUniqueId();
        ConsoleSystem.Log($"[NetworkService] Connected to server (client ID: {_clientId})", ConsoleChannel.Network);
        GameEvent.DispatchGlobal(new NetworkConnectedToServerEvent(_clientId));
    }

    private void OnConnectionFailed()
    {
        ConsoleSystem.LogErr("[NetworkService] Connection to server failed", ConsoleChannel.Network);
        Disconnect();
        GameEvent.DispatchGlobal(new NetworkConnectionFailedEvent());
    }

    private void OnServerDisconnected()
    {
        ConsoleSystem.Log("[NetworkService] Disconnected from server", ConsoleChannel.Network);
        Disconnect();
        GameEvent.DispatchGlobal(new NetworkDisconnectedFromServerEvent());
    }

    /// <summary>
    /// Send input to server
    /// </summary>
    public void SendInput(InputPacket input)
    {
        if (!IsClient)
        {
            ConsoleSystem.LogErr("[NetworkService] Cannot send input: not in client mode", ConsoleChannel.Network);
            return;
        }

        _inputBuffer.Enqueue(input);

        // In local server mode, process immediately
        if (_mode == NetworkMode.LocalServer)
        {
            ProcessLocalInput(input);
        }
        else
        {
            // TODO: Send to remote server
        }
    }

    private void ProcessLocalInput(InputPacket input)
    {
        // Process input immediately in local server mode
        GameEvent.DispatchGlobal(new NetworkInputReceivedEvent(_clientId, input));
        _lastProcessedInput = input.SequenceNumber;
    }

    #endregion

    #region Client RPC Handlers

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcClientSpawnPlayer(int peerId, Vector3 position, string playerName)
    {
        // Only clients should execute this
        if (IsServer) return;

        try
        {
            if (_networkedPlayers.ContainsKey(peerId))
            {
                ConsoleSystem.Log($"[NetworkService] RpcClientSpawnPlayer ignored; peer {peerId} already exists", ConsoleChannel.Network);
                return;
            }

            var tree = GetTree();
            var currentScene = tree?.CurrentScene;
            if (currentScene == null)
            {
                ConsoleSystem.LogErr("[NetworkService] RpcClientSpawnPlayer: CurrentScene is null", ConsoleChannel.Network);
                return;
            }

            var playerScene = GD.Load<PackedScene>("res://scenes/Player.tscn");
            var playerInstance = playerScene.Instantiate<PlayerEntity>();
            playerInstance.Name = playerName ?? $"Player_{peerId}";
            try
            {
                playerInstance.SetMultiplayerAuthority(peerId);
            }
            catch (Exception authEx)
            {
                ConsoleSystem.LogWarn($"[NetworkService] RpcClientSpawnPlayer: Failed to set authority for {peerId}: {authEx.Message}", ConsoleChannel.Network);
            }

            currentScene.AddChild(playerInstance);
            playerInstance.GlobalPosition = position;
            _networkedPlayers[peerId] = playerInstance;

            ConsoleSystem.Log($"[NetworkService] RpcClientSpawnPlayer: Spawned peer {peerId} at {position}", ConsoleChannel.Network);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[NetworkService] RpcClientSpawnPlayer failed: {ex.Message}", ConsoleChannel.Network);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcClientRemovePlayer(int peerId)
    {
        if (IsServer) return;

        if (_networkedPlayers.TryGetValue(peerId, out var player))
        {
            player.QueueFree();
            _networkedPlayers.Remove(peerId);
            ConsoleSystem.Log($"[NetworkService] RpcClientRemovePlayer: Removed peer {peerId}", ConsoleChannel.Network);
        }
    }

    #endregion

    #region General Methods

    /// <summary>
    /// Disconnect from network (works for both client and server)
    /// </summary>
    public void Disconnect()
    {
        // Skip if we're not in a network mode
        if (Mode == NetworkMode.None)
        {
            ConsoleSystem.Log("[NetworkService] Already disconnected, nothing to do", ConsoleChannel.Network);
            return;
        }

        var multiplayer = GetTree().GetMultiplayer();
        if (multiplayer != null && multiplayer.MultiplayerPeer != null)
        {
            // Disconnect signals - use try-catch to handle cases where they weren't connected
            try
            {
                if (IsServer)
                {
                    multiplayer.PeerConnected -= OnPeerConnected;
                    multiplayer.PeerDisconnected -= OnPeerDisconnected;
                }
                else
                {
                    multiplayer.ConnectedToServer -= OnConnectedToServer;
                    multiplayer.ConnectionFailed -= OnConnectionFailed;
                    multiplayer.ServerDisconnected -= OnServerDisconnected;
                }
            }
            catch (System.Exception ex)
            {
                // Signals weren't connected, which is fine
                ConsoleSystem.Log($"[NetworkService] Note: Some signals weren't connected during disconnect: {ex.Message}", ConsoleChannel.Network);
            }

            multiplayer.MultiplayerPeer = null;
        }

        _adapter?.Disconnect();

        _connectedClients.Clear();
        _inputBuffer.Clear();
        _mode = NetworkMode.None;
        _clientId = -1;
        _currentTick = 0;

        ConsoleSystem.Log("[NetworkService] Disconnected", ConsoleChannel.Network);
    }

    private NetworkConfig LoadNetworkConfig()
    {
        // TODO: Load from settings file
        return new NetworkConfig
        {
            ServerPort = 7777,
            MaxPlayers = 32,
            TickRate = 30,
            SnapshotRate = 20,
            AutoStartLocalServer = false, // Don't auto-start; let explicit SP/MP flow handle it
            EnableCompression = true,
            InterpolationDelay = 100, // ms
            Backend = NetworkBackend.Steam
        };
    }

    private void RegisterConsoleCommands()
    {
        var consoleSystem = GetNodeOrNull<ConsoleSystem>("/root/ConsoleSystem");
        if (consoleSystem == null) return;

        // Server commands
        consoleSystem.RegisterCommand(new ConsoleCommand(
            "net_start_server",
            "Start a dedicated server",
            "net_start_server [port]",
            async (args) =>
            {
                int port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 0;
                StartServer(port);
                return true;
            }));

        // Client commands
        consoleSystem.RegisterCommand(new ConsoleCommand(
            "net_connect",
            "Connect to a server",
            "net_connect <address> [port]",
            async (args) =>
            {
                if (args.Length == 0)
                {
                    ConsoleSystem.Log("Usage: net_connect <address> [port]", ConsoleChannel.Network);
                    return false;
                }

                string address = args[0];
                int port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 0;
                return ConnectToServer(address, port);
            }));

        // Multiplayer testing commands
        consoleSystem.RegisterCommand(new ConsoleCommand(
            "net_create_lobby",
            "Create a Steam lobby for multiplayer",
            "net_create_lobby [max_players]",
            async (args) =>
            {
                int maxPlayers = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 8;

                if (!PlatformService.IsSteamInitialized)
                {
                    ConsoleSystem.LogErr("Steam is not initialized. Make sure Steam is running.", ConsoleChannel.Network);
                    return false;
                }

                // Switch to Steam backend if not already
                if (_config.Backend != NetworkBackend.Steam)
                {
                    _config.Backend = NetworkBackend.Steam;
                    InitializeAdapter();
                    ConsoleSystem.Log("Switched to Steam networking backend", ConsoleChannel.Network);
                }

                var success = StartServer(0);
                if (success)
                {
                    var steamAdapter = _adapter as SteamNetworkAdapter;
                    if (steamAdapter != null)
                    {
                        ConsoleSystem.Log($"Steam lobby created! Join code: {steamAdapter.GetLobbyJoinCode()}", ConsoleChannel.Network);
                    }
                }
                return success;
            }));

        consoleSystem.RegisterCommand(new ConsoleCommand(
            "net_join_lobby",
            "Join a Steam lobby using join code",
            "net_join_lobby <join_code>",
            async (args) =>
            {
                if (args.Length == 0)
                {
                    ConsoleSystem.Log("Usage: net_join_lobby <join_code>", ConsoleChannel.Network);
                    return false;
                }

                if (!PlatformService.IsSteamInitialized)
                {
                    ConsoleSystem.LogErr("Steam is not initialized. Make sure Steam is running.", ConsoleChannel.Network);
                    return false;
                }

                // Switch to Steam backend if not already
                if (_config.Backend != NetworkBackend.Steam)
                {
                    _config.Backend = NetworkBackend.Steam;
                    InitializeAdapter();
                    ConsoleSystem.Log("Switched to Steam networking backend", ConsoleChannel.Network);
                }

                return ConnectToServer(args[0], 0);
            }));

        consoleSystem.RegisterCommand(new ConsoleCommand(
            "net_disconnect",
            "Disconnect from network",
            "net_disconnect",
            async (args) =>
            {
                Disconnect();
                return true;
            }));

        consoleSystem.RegisterCommand(new ConsoleCommand(
            "net_status",
            "Show network status",
            "net_status",
            async (args) =>
            {
                ConsoleSystem.Log($"Network Mode: {_mode}", ConsoleChannel.Network);
                ConsoleSystem.Log($"Connected: {IsConnected}", ConsoleChannel.Network);
                ConsoleSystem.Log($"Backend: {_config.Backend}", ConsoleChannel.Network);
                if (IsServer)
                {
                    ConsoleSystem.Log($"Tick: {_currentTick}", ConsoleChannel.Network);
                    ConsoleSystem.Log($"Connected clients: {_connectedClients.Count}", ConsoleChannel.Network);
                }

                if (IsClient)
                {
                    ConsoleSystem.Log($"Client ID: {_clientId}", ConsoleChannel.Network);
                    ConsoleSystem.Log($"Last processed input: {_lastProcessedInput}", ConsoleChannel.Network);
                }

                return true;
            }));

        consoleSystem.RegisterCommand(new ConsoleCommand(
            "net_backend",
            "Get or set networking backend",
            "net_backend [Steam|P2P|Null]",
            async (args) =>
            {
                if (args.Length == 0)
                {
                    ConsoleSystem.Log($"Backend: {_config.Backend}", ConsoleChannel.Network);
                    return true;
                }

                if (!System.Enum.TryParse<NetworkBackend>(args[0], true, out var backend))
                {
                    ConsoleSystem.Log("Usage: net_backend [Steam|P2P|Null]", ConsoleChannel.Network);
                    return false;
                }

                if (_mode != NetworkMode.None)
                {
                    ConsoleSystem.Log("[NetworkService] Switching backend requires disconnecting...", ConsoleChannel.Network);
                    Disconnect();
                }

                _config.Backend = backend;
                InitializeAdapter();
                ConsoleSystem.Log($"Backend set to {backend}", ConsoleChannel.Network);
                return true;
            }));
    }

    #endregion

    private void InitializeAdapter()
    {
        switch (_config.Backend)
        {
            case NetworkBackend.Steam:
                _adapter = new SteamNetworkAdapter();
                break;
            case NetworkBackend.P2P:
                _adapter = new P2PNetworkAdapter();
                break;
            case NetworkBackend.Null:
            default:
                _adapter = new NullNetworkAdapter();
                break;
        }
    }
}

/// <summary>
/// Network operating mode
/// </summary>
public enum NetworkMode
{
    None, // Not networked
    LocalServer, // Singleplayer (local authoritative server)
    Server, // Dedicated server
    Client // Client connected to remote server
}

/// <summary>
/// Network configuration
/// </summary>
public class NetworkConfig
{
    public int ServerPort { get; set; } = 7777;
    public int MaxPlayers { get; set; } = 32;
    public int TickRate { get; set; } = 30;
    public int SnapshotRate { get; set; } = 20;
    public bool AutoStartLocalServer { get; set; } = true;
    public bool EnableCompression { get; set; } = true;
    public int InterpolationDelay { get; set; } = 100; // milliseconds
    public NetworkBackend Backend { get; set; } = NetworkBackend.Steam;
}

/// <summary>
/// State tracking for connected clients
/// </summary>
public class ClientState
{
    public long PeerId { get; set; }
    public ulong ConnectedAt { get; set; }
    public ulong LastHeartbeat { get; set; }
    public ulong LastProcessedInput { get; set; }
    public string Username { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Input packet from client to server
/// </summary>
public struct InputPacket
{
    public ulong SequenceNumber;
    public ulong Tick;
    public Vector3 Movement;
    public Vector2 Look;
    public Dictionary<string, bool> Actions; // Jump, interact, etc.
}