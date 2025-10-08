using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotSteam;
using Waterjam.Core.Services;
using Waterjam.Core.Services.Network;
using Waterjam.Core.Systems.Console;
using Waterjam.Events;
using Waterjam.Domain.Party;
using Waterjam.Game.Services.Party;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Steam networking adapter using GodotSteam C# bindings for P2P multiplayer connectivity.
/// Supports lobby creation, joining, and direct Steam P2P connections.
/// </summary>
public class SteamNetworkAdapter : INetworkAdapter
{
    public NetworkBackend Backend => NetworkBackend.Steam;

    private SteamP2PMultiplayerPeer _peer;
    private ulong _lobbyId;
    private ulong _hostSteamId;
    private string _lobbyJoinCode;
    private bool _isHost;
    private bool _isConnected;
    private bool _lobbyCreationPending;
    private ulong _preferredLobbyId; // If set, reuse this lobby instead of creating a new one
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

    /// <summary>
    /// Reuse an existing Steam lobby instead of creating a new one when starting the server.
    /// Only valid if the local Steam user is the owner of the lobby.
    /// </summary>
    public void SetPreferredLobbyId(ulong lobbyId)
    {
        _preferredLobbyId = lobbyId;
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
            // If a preferred lobby is set and we're its owner, reuse it instead of creating a new one
            if (_preferredLobbyId != 0)
            {
                var owner = Steam.GetLobbyOwner(_preferredLobbyId);
                var me = Steam.GetSteamID();
                ConsoleSystem.Log($"[SteamNetworkAdapter] Checking preferred lobby {_preferredLobbyId}, owner: {owner}, me: {me}", ConsoleChannel.Network);

                if (owner == me)
                {
                    _isHost = true;
                    _lobbyId = _preferredLobbyId;
                    _preferredLobbyId = 0; // consume

                    // Initialize peer immediately; we are the host
                    _peer = new SteamP2PMultiplayerPeer();
                    _peer.InitializeAsHost(Steam.GetSteamID());
                    Steam.AllowP2PPacketRelay(true);

                    // Ensure lobby data is set for game networking
                    _lobbyJoinCode = GenerateLobbyCode();
                    Steam.SetLobbyData(_lobbyId, "join_code", _lobbyJoinCode);
                    Steam.SetLobbyData(_lobbyId, "name", "Game Lobby");

                    _isConnected = true;
                    ConsoleSystem.Log($"[SteamNetworkAdapter] Reusing existing Steam lobby {_lobbyId} as host", ConsoleChannel.Network);
                    return true;
                }
                else
                {
                    ConsoleSystem.LogWarn($"[SteamNetworkAdapter] Preferred lobby {_preferredLobbyId} is not owned by local user (owner: {owner}, me: {me}); creating a new lobby", ConsoleChannel.Network);
                    _preferredLobbyId = 0; // reset since we can't use it
                }
            }

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

        // If we're already connected (reusing existing lobby), don't process this callback
        if (_isConnected && _lobbyId != 0)
        {
            ConsoleSystem.Log($"[SteamNetworkAdapter] Ignoring OnLobbyCreated for existing lobby {lobbyId} (already connected to {_lobbyId})", ConsoleChannel.Network);
            return;
        }

        _lobbyId = lobbyId;
        _lobbyJoinCode = GenerateLobbyCode();
        _isConnected = true;

        // Set lobby data
        Steam.SetLobbyData(lobbyId, "join_code", _lobbyJoinCode);
        Steam.SetLobbyData(lobbyId, "name", "Game Lobby");

        ConsoleSystem.Log($"[SteamNetworkAdapter] Steam lobby created! ID: {lobbyId}, Join Code: {_lobbyJoinCode}", ConsoleChannel.Network);

        // Initialize Steam-backed MultiplayerPeer
        _peer = new SteamP2PMultiplayerPeer();
        _peer.InitializeAsHost(Steam.GetSteamID());

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
            
            _peer = new SteamP2PMultiplayerPeer();
            _peer.InitializeAsClient(_hostSteamId);
            
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

                // Register mapping in MultiplayerPeer and send current party state to the new peer
                _peer?.MapPeer(peerId, steamIdRemote);
                SendCurrentLobbyStateToPeer(steamIdRemote);
            }
        }
        else
        {
            // We're a client - request current lobby state from host
            RequestLobbyStateFromHost();
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
                        else if (data[1] == 0x01) // Lobby state packet
                        {
                            HandleLobbyStatePacket(data, steamIdRemote);
                        }
                        else if (data[1] == 0x02) // Lobby event packet (enveloped message)
                        {
                            HandleLobbyMessagePacket(data, steamIdRemote);
                        }
                        else if (data[1] == 0x03) // Lobby state request packet
                        {
                            HandleLobbyStateRequestPacket(data, steamIdRemote);
                        }
                        else
                        {
                            // Unknown control header - ignore
                        }
                    }
                    else
                    {
                        // Regular game packet - feed into MultiplayerPeer for Godot RPC
                        _peer?.EnqueueIncoming(data, steamIdRemote);
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
    /// Returns connected peer IDs (Steam-assigned mapping), excluding the server (1) by default.
    /// </summary>
    public IEnumerable<long> GetConnectedPeerIds(bool includeServer = false)
    {
        if (includeServer)
        {
            return _peerIdToSteamId.Keys.Select(k => (long)k);
        }
        return _peerIdToSteamId.Keys.Where(id => id != 1).Select(k => (long)k);
    }

    /// <summary>
    /// Handles incoming party state packets from remote peers (transported via Steam lobby channel).
    /// </summary>
    private void HandleLobbyStatePacket(byte[] data, ulong steamIdRemote)
    {
        try
        {
            // Extract the actual lobby state data (skip the 2-byte header)
            var stateData = new byte[data.Length - 2];
            Array.Copy(data, 2, stateData, 0, stateData.Length);

            // Deserialize the party state
            var party = PartyNetworkSerializer.DeserializePartyState(stateData);
            if (party == null)
            {
                ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to deserialize party state from {steamIdRemote}", ConsoleChannel.Network);
                return;
            }

            ConsoleSystem.Log($"[SteamNetworkAdapter] Received party state for {party.DisplayName} from {steamIdRemote}", ConsoleChannel.Network);
            // Note: for now we only log receipt; PartyService maintains authoritative local party state.
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Error handling party state packet from {steamIdRemote}: {ex.Message}", ConsoleChannel.Network);
        }
    }

    /// <summary>
    /// Handles incoming lobby event packets from remote peers.
    /// </summary>
    private void HandleLobbyEventPacket(byte[] data, ulong steamIdRemote)
    {
        try
        {
            // For now, this is a placeholder for handling specific lobby events
            // In a full implementation, we'd deserialize and dispatch specific lobby events
            ConsoleSystem.Log($"[SteamNetworkAdapter] Received lobby event packet from {steamIdRemote} ({data.Length} bytes)", ConsoleChannel.Network);

            // TODO: Implement specific event deserialization and dispatching
            // This would handle events like player joined, settings changed, etc.
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Error handling lobby event packet from {steamIdRemote}: {ex.Message}", ConsoleChannel.Network);
        }
    }

    /// <summary>
    /// Handles lobby message packets that use the NetworkMessageRegistry envelope format.
    /// </summary>
    private void HandleLobbyMessagePacket(byte[] data, ulong steamIdRemote)
    {
        try
        {
            // Strip header (0xFF, 0x02)
            var payloadBytes = new byte[data.Length - 2];
            Array.Copy(data, 2, payloadBytes, 0, payloadBytes.Length);

            if (NetworkMessageRegistry.TryDeserialize(payloadBytes, out var message))
            {
                NetworkMessageRegistry.Dispatch(message);
            }
            else
            {
                ConsoleSystem.LogWarn($"[SteamNetworkAdapter] Failed to parse lobby message packet from {steamIdRemote}", ConsoleChannel.Network);
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Error handling lobby message packet from {steamIdRemote}: {ex.Message}", ConsoleChannel.Network);
        }
    }

    /// <summary>
    /// Sends party state to all connected peers.
    /// </summary>
    public void BroadcastLobbyState(Party party)
    {
        if (!PlatformService.IsSteamInitialized || !_isConnected || !_isHost)
        {
            return;
        }

        try
        {
            var stateData = PartyNetworkSerializer.SerializePartyState(party);
            if (stateData.Length > 0)
            {
                // Create packet with header
                var packetData = new byte[stateData.Length + 2];
                packetData[0] = 0xFF; // Magic byte
                packetData[1] = 0x01; // Lobby state packet type
                Array.Copy(stateData, 0, packetData, 2, stateData.Length);

                // Send to all connected Steam peers
                foreach (var steamId in _steamIdToPeerId.Keys.ToArray())
                {
                    if (steamId != Steam.GetSteamID()) // Don't send to ourselves
                    {
                        Steam.SendP2PPacket(steamId, packetData, Steam.P2PSend.Reliable, 0);
                        ConsoleSystem.Log($"[SteamNetworkAdapter] Sent party state to Steam ID {steamId}", ConsoleChannel.Network);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to broadcast party state: {ex.Message}", ConsoleChannel.Network);
        }
    }

    /// <summary>
    /// Sends a lobby event to all connected peers.
    /// </summary>
    public void BroadcastLobbyEvent(IGameEvent lobbyEvent)
    {
        if (!PlatformService.IsSteamInitialized || !_isConnected || !_isHost)
        {
            return;
        }

        try
        {
            // For now support only LobbyStateMessage via registry if provided indirectly
            ConsoleSystem.Log($"[SteamNetworkAdapter] Broadcasting lobby event: {lobbyEvent.GetType().Name}", ConsoleChannel.Network);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to broadcast lobby event: {ex.Message}", ConsoleChannel.Network);
        }
    }

    /// <summary>
    /// Broadcasts a registry-based lobby message to all peers (enveloped).
    /// </summary>
    public void BroadcastLobbyRegistryMessage(INetworkMessage message)
    {
        if (!PlatformService.IsSteamInitialized || !_isConnected || !_isHost || message == null)
        {
            return;
        }

        try
        {
            var bytes = NetworkMessageRegistry.Serialize(message);
            if (bytes == null || bytes.Length == 0) return;

            var packetData = new byte[bytes.Length + 2];
            packetData[0] = 0xFF; // Magic
            packetData[1] = 0x02; // Lobby message packet type
            Array.Copy(bytes, 0, packetData, 2, bytes.Length);

            foreach (var steamId in _steamIdToPeerId.Keys.ToArray())
            {
                if (steamId != Steam.GetSteamID())
                {
                    Steam.SendP2PPacket(steamId, packetData, Steam.P2PSend.Reliable, 0);
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to broadcast registry message: {ex.Message}", ConsoleChannel.Network);
        }
    }

    /// <summary>
    /// Gets the current connection status.
    /// </summary>
    public bool IsConnected()
    {
        return _isConnected;
    }

    /// <summary>
    /// Sends the current lobby state to a newly connected peer.
    /// </summary>
    private void SendCurrentLobbyStateToPeer(ulong steamIdRemote)
    {
        try
        {
            if (!PlatformService.IsSteamInitialized || !_isConnected)
            {
                return;
            }

            // Retrieve current party from PartyService
            var tree = Godot.Engine.GetMainLoop() as Godot.SceneTree;
            var partyService = tree != null ? tree.Root.GetNodeOrNull<PartyService>("/root/PartyService") : null;
            var party = partyService?.GetCurrentPlayerParty();
            if (party == null)
            {
                ConsoleSystem.Log("[SteamNetworkAdapter] No current party available to send", ConsoleChannel.Network);
                return;
            }

            var stateData = PartyNetworkSerializer.SerializePartyState(party);
            if (stateData == null || stateData.Length == 0)
            {
                ConsoleSystem.LogWarn("[SteamNetworkAdapter] SerializePartyState returned empty payload", ConsoleChannel.Network);
                return;
            }

            var packetData = new byte[stateData.Length + 2];
            packetData[0] = 0xFF; // Magic byte
            packetData[1] = 0x01; // Party state packet
            System.Array.Copy(stateData, 0, packetData, 2, stateData.Length);

            Steam.SendP2PPacket(steamIdRemote, packetData, Steam.P2PSend.Reliable, 0);
            ConsoleSystem.Log($"[SteamNetworkAdapter] Sent party state to Steam ID {steamIdRemote}", ConsoleChannel.Network);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to send party state to peer {steamIdRemote}: {ex.Message}", ConsoleChannel.Network);
        }
    }

    /// <summary>
    /// Requests the current lobby state from the host when joining as a client.
    /// </summary>
    private void RequestLobbyStateFromHost()
    {
        try
        {
            if (_hostSteamId != 0)
            {
                // Send a lobby state request packet to the host
                var requestPacket = new byte[3];
                requestPacket[0] = 0xFF; // Magic byte
                requestPacket[1] = 0x03; // Lobby state request packet type
                requestPacket[2] = 0x01; // Request current state

                Steam.SendP2PPacket(_hostSteamId, requestPacket, Steam.P2PSend.Reliable, 0);
                ConsoleSystem.Log($"[SteamNetworkAdapter] Requested current lobby state from host {_hostSteamId}", ConsoleChannel.Network);
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to request lobby state from host: {ex.Message}", ConsoleChannel.Network);
        }
    }

    /// <summary>
    /// Handles incoming lobby state request packets from clients.
    /// </summary>
    private void HandleLobbyStateRequestPacket(byte[] data, ulong steamIdRemote)
    {
        try
        {
            // This is a request from a client for the current party state
            ConsoleSystem.Log($"[SteamNetworkAdapter] Received party state request from {steamIdRemote}", ConsoleChannel.Network);
            SendCurrentLobbyStateToPeer(steamIdRemote);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Error handling party state request from {steamIdRemote}: {ex.Message}", ConsoleChannel.Network);
        }
    }
}


