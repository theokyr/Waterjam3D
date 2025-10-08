using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotSteam;
using Waterjam.Core.Services;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Steam networking adapter using GodotSteam C# bindings for P2P multiplayer connectivity.
/// Supports lobby creation, joining, and direct Steam P2P connections.
/// </summary>
public class SteamNetworkAdapter : INetworkAdapter
{
    public NetworkBackend Backend => NetworkBackend.Steam;

    private OfflineMultiplayerPeer _peer;
    private ulong _lobbyId;
    private ulong _hostSteamId;
    private string _lobbyJoinCode;
    private bool _isHost;
    private bool _isConnected;
    private bool _lobbyCreationPending;
    private Dictionary<ulong, int> _steamIdToPeerId = new();
    private Dictionary<int, ulong> _peerIdToSteamId = new();
    private int _nextPeerId = 2; // 1 is reserved for server

    public MultiplayerPeer Peer => _peer;

    public SteamNetworkAdapter()
    {
        // Wire up Steam lobby and P2P callbacks
        if (PlatformService.IsSteamInitialized)
        {
            Steam.LobbyCreated += OnLobbyCreated;
            Steam.LobbyMatchList += OnLobbyMatchList;
            Steam.LobbyJoined += OnLobbyJoined;
            Steam.P2PSessionRequest += OnP2PSessionRequest;
            Steam.P2PSessionConnectFail += OnP2PSessionConnectFail;
            ConsoleSystem.Log("[SteamNetworkAdapter] Steam callbacks registered", ConsoleChannel.Network);
        }
    }

    public bool StartServer(int port, int maxPlayers)
    {
        if (!PlatformService.IsSteamInitialized)
        {
            ConsoleSystem.LogErr("[SteamNetworkAdapter] Cannot start server: Steam not initialized", ConsoleChannel.Network);
            return false;
        }

        try
        {
            // Create a Steam lobby - this is async, we'll handle the response in OnLobbyCreated
            Steam.CreateLobby(Steam.LobbyType.FriendsOnly, maxPlayers);

            _isHost = true;
            _lobbyCreationPending = true;
            ConsoleSystem.Log($"[SteamNetworkAdapter] Creating Steam lobby (max {maxPlayers} players)", ConsoleChannel.Network);

            return true;
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to create lobby: {ex.Message}", ConsoleChannel.Network);
            return false;
        }
    }

    private void OnLobbyCreated(long result, ulong lobbyId)
    {
        _lobbyCreationPending = false;
        
        if (result != 1) // 1 = k_EResultOK
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to create lobby: result {result}", ConsoleChannel.Network);
            _isHost = false;
            return;
        }

        _lobbyId = lobbyId;
        _lobbyJoinCode = GenerateLobbyCode();
        _isConnected = true;

        // Set lobby data
        Steam.SetLobbyData(lobbyId, "join_code", _lobbyJoinCode);
        Steam.SetLobbyData(lobbyId, "name", "Game Lobby");

        ConsoleSystem.Log($"[SteamNetworkAdapter] Steam lobby created! ID: {lobbyId}, Join Code: {_lobbyJoinCode}", ConsoleChannel.Network);

        // Use OfflineMultiplayerPeer as a simple peer wrapper
        // We'll handle Steam P2P networking manually
        _peer = new OfflineMultiplayerPeer();
        
        // Allow P2P relay through Steam servers for better connectivity
        Steam.AllowP2PPacketRelay(true);
        
        ConsoleSystem.Log("[SteamNetworkAdapter] Steam P2P server created successfully", ConsoleChannel.Network);
    }

    private void OnLobbyMatchList(Godot.Collections.Array lobbies)
    {
        ConsoleSystem.Log($"[SteamNetworkAdapter] Found {lobbies.Count} lobbies", ConsoleChannel.Network);
        
        if (lobbies.Count > 0)
        {
            // Join the first matching lobby
            var lobbyId = lobbies[0].AsUInt64();
            Steam.JoinLobby(lobbyId);
        }
    }

    private void OnLobbyJoined(ulong lobbyId, long permissions, bool locked, long response)
    {
        ConsoleSystem.Log($"[SteamNetworkAdapter] Lobby joined! ID: {lobbyId}, Response: {response}, Locked: {locked}", ConsoleChannel.Network);
        
        // Response codes: 1 = Success, others are errors
        if (response != 1)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to join lobby: response code {response}", ConsoleChannel.Network);
            return;
        }

        _lobbyId = lobbyId;
        _isConnected = true;

        // Get the host's Steam ID (owner of the lobby)
        var numMembers = Steam.GetNumLobbyMembers(lobbyId);
        ConsoleSystem.Log($"[SteamNetworkAdapter] Lobby has {numMembers} members", ConsoleChannel.Network);

        // The lobby owner is at index 0
        if (numMembers > 0)
        {
            _hostSteamId = Steam.GetLobbyMemberByIndex(lobbyId, 0);
            ConsoleSystem.Log($"[SteamNetworkAdapter] Lobby host Steam ID: {_hostSteamId}", ConsoleChannel.Network);
        }

        // Get lobby data
        var lobbyName = Steam.GetLobbyData(lobbyId, "name");
        _lobbyJoinCode = Steam.GetLobbyData(lobbyId, "join_code");
        ConsoleSystem.Log($"[SteamNetworkAdapter] Joined lobby '{lobbyName}' with code: {_lobbyJoinCode}", ConsoleChannel.Network);

        // Determine if we're host or client
        var localSteamId = Steam.GetSteamID();
        _isHost = (_hostSteamId == localSteamId);
        
        if (_isHost)
        {
            // We're the host - the server peer was already created in OnLobbyCreated
            ConsoleSystem.Log($"[SteamNetworkAdapter] We are the lobby host", ConsoleChannel.Network);
            
            // Add all existing lobby members as peers
            for (int i = 0; i < numMembers; i++)
            {
                var memberSteamId = Steam.GetLobbyMemberByIndex(lobbyId, i);
                if (memberSteamId != localSteamId)
                {
                    // Members will connect when they join, peers will be added via P2PSessionRequest
                    ConsoleSystem.Log($"[SteamNetworkAdapter] Lobby member {i}: Steam ID {memberSteamId}", ConsoleChannel.Network);
                }
            }
        }
        else
        {
            // We're a client - connect to the host via Steam P2P
            ConsoleSystem.Log($"[SteamNetworkAdapter] Connecting to host Steam ID {_hostSteamId} via Steam P2P...", ConsoleChannel.Network);
            
            _peer = new OfflineMultiplayerPeer();
            
            // Send initial connection packet to host
            var connectionPacket = new byte[] { 0xFF, 0xFE }; // Magic bytes for connection request
            Steam.SendP2PPacket(_hostSteamId, connectionPacket, Steam.P2PSend.Reliable, 0);
            
            ConsoleSystem.Log("[SteamNetworkAdapter] Steam P2P client created successfully", ConsoleChannel.Network);
        }
    }

    private void OnP2PSessionRequest(ulong steamIdRemote)
    {
        ConsoleSystem.Log($"[SteamNetworkAdapter] P2P session request from Steam ID: {steamIdRemote}", ConsoleChannel.Network);
        
        // Accept the P2P session
        Steam.AcceptP2PSessionWithUser(steamIdRemote);
        
        if (_isHost)
        {
            // Assign a peer ID to this client if we haven't already
            if (!_steamIdToPeerId.ContainsKey(steamIdRemote))
            {
                int peerId = _nextPeerId++;
                _steamIdToPeerId[steamIdRemote] = peerId;
                _peerIdToSteamId[peerId] = steamIdRemote;
                
                // Send peer ID assignment to the client
                var assignmentPacket = new byte[5];
                assignmentPacket[0] = 0xFF; // Magic byte
                assignmentPacket[1] = 0xFD; // Peer ID assignment marker
                BitConverter.GetBytes(peerId).CopyTo(assignmentPacket, 1);
                Steam.SendP2PPacket(steamIdRemote, assignmentPacket, Steam.P2PSend.Reliable, 0);
                
                ConsoleSystem.Log($"[SteamNetworkAdapter] Assigned peer ID {peerId} to Steam ID {steamIdRemote}", ConsoleChannel.Network);
            }
        }
    }

    private void OnP2PSessionConnectFail(ulong steamIdRemote, long sessionError)
    {
        var errorType = (Steam.P2PSessionError)sessionError;
        ConsoleSystem.LogErr($"[SteamNetworkAdapter] P2P session connect fail with {steamIdRemote}: {errorType}", ConsoleChannel.Network);
        
        if (_steamIdToPeerId.TryGetValue(steamIdRemote, out var peerId))
        {
            _steamIdToPeerId.Remove(steamIdRemote);
            _peerIdToSteamId.Remove(peerId);
        }
    }

    public bool Connect(string address, int port)
    {
        if (!PlatformService.IsSteamInitialized)
        {
            ConsoleSystem.LogErr("[SteamNetworkAdapter] Cannot connect: Steam not initialized", ConsoleChannel.Network);
            return false;
        }

        try
        {
            // For Steam, we connect via lobby code or direct Steam ID
            if (ulong.TryParse(address, out ulong steamId))
            {
                // Direct connection to Steam ID
                return ConnectToSteamId(steamId);
            }
            else
            {
                // Connection via lobby code
                return ConnectToLobby(address);
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to connect: {ex.Message}", ConsoleChannel.Network);
            return false;
        }
    }

    private bool ConnectToLobby(string lobbyCode)
    {
        try
        {
            // Request lobby list (this is async, we'll handle the response in OnLobbyMatchList)
            Steam.AddRequestLobbyListStringFilter("join_code", lobbyCode, Steam.LobbyComparison.LobbyComparisonEqual);
            Steam.RequestLobbyList();

            ConsoleSystem.Log($"[SteamNetworkAdapter] Searching for lobby with code: {lobbyCode}", ConsoleChannel.Network);
            return true;
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to search for lobby: {ex.Message}", ConsoleChannel.Network);
            return false;
        }
    }

    private bool ConnectToSteamId(ulong steamId)
    {
        try
        {
            // Request P2P session with the host
            // Send a small ping to open NAT path; channel 0
            Steam.SendP2PPacket(steamId, new byte[1] { 0 }, GodotSteam.Steam.P2PSend.Reliable, 0);

            _hostSteamId = steamId;
            ConsoleSystem.Log($"[SteamNetworkAdapter] Requesting P2P session with Steam ID: {steamId}", ConsoleChannel.Network);
            return true;
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to connect to Steam ID: {ex.Message}", ConsoleChannel.Network);
            return false;
        }
    }

    public void Disconnect()
    {
        try
        {
            // Unhook Steam callbacks
            if (PlatformService.IsSteamInitialized)
            {
                Steam.LobbyCreated -= OnLobbyCreated;
                Steam.LobbyMatchList -= OnLobbyMatchList;
                Steam.LobbyJoined -= OnLobbyJoined;
                Steam.P2PSessionRequest -= OnP2PSessionRequest;
                Steam.P2PSessionConnectFail -= OnP2PSessionConnectFail;
            }
            
            // Close all P2P sessions
            foreach (var steamId in _steamIdToPeerId.Keys.ToArray())
            {
                Steam.CloseP2PSessionWithUser(steamId);
            }
            _steamIdToPeerId.Clear();
            _peerIdToSteamId.Clear();

            if (_lobbyId != 0)
            {
                Steam.LeaveLobby(_lobbyId);
                _lobbyId = 0;
            }

            if (_peer != null)
            {
                _peer.Close();
                _peer = null;
            }

            _isConnected = false;
            _isHost = false;
            _hostSteamId = 0;
            _lobbyJoinCode = null;
            _lobbyCreationPending = false;

            ConsoleSystem.Log("[SteamNetworkAdapter] Disconnected", ConsoleChannel.Network);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Error during disconnect: {ex.Message}", ConsoleChannel.Network);
        }
    }

    /// <summary>
    /// Updates Steam callbacks and handles P2P packets. Should be called from NetworkService.
    /// </summary>
    public void Update()
    {
        if (!PlatformService.IsSteamInitialized) return;

        try
        {
            // Handle P2P packets
            HandleP2PPackets();

            // Update Steam callbacks
            Steam.RunCallbacks();
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Error during update: {ex.Message}", ConsoleChannel.Network);
        }
    }

    private void HandleP2PPackets()
    {
        if (!PlatformService.IsSteamInitialized || !_isConnected) return;

        try
        {
            // Check for incoming P2P packets on channel 0
            var packetSize = Steam.GetAvailableP2PPacketSize(0);
            while (packetSize > 0)
            {
                var packetInfo = Steam.ReadP2PPacket(packetSize, 0);
                if (packetInfo != null && packetInfo.ContainsKey("data") && packetInfo.ContainsKey("steam_id_remote"))
                {
                    var data = packetInfo["data"].AsByteArray();
                    var steamIdRemote = packetInfo["steam_id_remote"].AsUInt64();
                    
                    // Handle special control packets
                    if (data.Length >= 2 && data[0] == 0xFF)
                    {
                        if (data[1] == 0xFE) // Connection request
                        {
                            // Already handled by P2PSessionRequest callback
                            ConsoleSystem.Log($"[SteamNetworkAdapter] Connection request from {steamIdRemote}", ConsoleChannel.Network);
                        }
                        else if (data[1] == 0xFD && !_isHost) // Peer ID assignment
                        {
                            int assignedPeerId = BitConverter.ToInt32(data, 1);
                            if (assignedPeerId > 0)
                            {
                                ConsoleSystem.Log($"[SteamNetworkAdapter] Received peer ID assignment: {assignedPeerId}", ConsoleChannel.Network);
                                // Store the peer ID mapping
                                _steamIdToPeerId[steamIdRemote] = 1; // Server is always peer 1
                                _peerIdToSteamId[1] = steamIdRemote;
                            }
                        }
                    }
                    else
                    {
                        // Regular game packet - forward to the network service
                        // The NetworkService will handle these through the multiplayer API
                        ConsoleSystem.Log($"[SteamNetworkAdapter] Received game packet from {steamIdRemote} ({data.Length} bytes)", ConsoleChannel.Debug);
                    }
                }
                
                // Check for next packet
                packetSize = Steam.GetAvailableP2PPacketSize(0);
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Error handling P2P packets: {ex.Message}", ConsoleChannel.Network);
        }
    }

    private string GenerateLobbyCode()
    {
        try
        {
            // Generate a simple 6-character alphanumeric code
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Range(0, 6)
                .Select(_ => chars[random.Next(chars.Length)])
                .ToArray());
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to generate lobby code: {ex.Message}", ConsoleChannel.Network);
            return "ERROR";
        }
    }

    /// <summary>
    /// Gets the current lobby join code.
    /// </summary>
    public string GetLobbyJoinCode()
    {
        return _lobbyJoinCode;
    }

    /// <summary>
    /// Gets the current lobby ID.
    /// </summary>
    public ulong GetLobbyId()
    {
        return _lobbyId;
    }

    /// <summary>
    /// Gets the Steam ID of the lobby host.
    /// </summary>
    public ulong GetHostSteamId()
    {
        return _hostSteamId;
    }

    /// <summary>
    /// Checks if this client is the lobby host.
    /// </summary>
    public bool IsHost()
    {
        return _isHost;
    }

    /// <summary>
    /// Gets the current connection status.
    /// </summary>
    public bool IsConnected()
    {
        return _isConnected;
    }
}


