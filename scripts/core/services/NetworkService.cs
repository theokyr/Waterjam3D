using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Waterjam.Events;
using Waterjam.Core.Systems.Console;
using Waterjam.Core.Services.Network;

namespace Waterjam.Core.Services;

/// <summary>
/// Manages networking for both client and server modes.
/// Handles connections, message routing, and network state management.
/// </summary>
public partial class NetworkService : BaseService,
    IGameEventHandler<GameInitializedEvent>
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

    public bool IsServer => _mode == NetworkMode.Server || _mode == NetworkMode.LocalServer;
    public bool IsClient => _mode == NetworkMode.Client || _mode == NetworkMode.LocalServer;
    public new bool IsConnected => _adapter != null && _adapter.Peer != null && _adapter.Peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;
    public NetworkBackend CurrentBackend => _config.Backend;
    public NetworkMode Mode => _mode;
    public ulong CurrentTick => _currentTick;

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
            InitializeAdapter();

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

        _connectedClients.Remove(peerId);
        GameEvent.DispatchGlobal(new NetworkClientDisconnectedEvent(peerId));
    }

    private void SendHandshake(long peerId)
    {
        // TODO: Implement protocol handshake with mod requirements
        // For now, just acknowledge connection
        ConsoleSystem.Log($"[NetworkService] Sending handshake to peer {peerId}", ConsoleChannel.Network);
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

    #region General Methods

    /// <summary>
    /// Disconnect from network (works for both client and server)
    /// </summary>
    public void Disconnect()
    {
        var multiplayer = GetTree().GetMultiplayer();
        if (multiplayer != null && multiplayer.MultiplayerPeer != null)
        {
            // Disconnect signals
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
            AutoStartLocalServer = true,
            EnableCompression = true,
            InterpolationDelay = 100, // ms
            Backend = NetworkBackend.ENet
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
            "net_backend [ENet|Steam|P2P|Null]",
            async (args) =>
            {
                if (args.Length == 0)
                {
                    ConsoleSystem.Log($"Backend: {_config.Backend}", ConsoleChannel.Network);
                    return true;
                }

                if (!System.Enum.TryParse<NetworkBackend>(args[0], true, out var backend))
                {
                    ConsoleSystem.Log("Usage: net_backend [ENet|Steam|P2P|Null]", ConsoleChannel.Network);
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
            case NetworkBackend.ENet:
                _adapter = new ENetNetworkAdapter();
                break;
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
    public NetworkBackend Backend { get; set; } = NetworkBackend.ENet;
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