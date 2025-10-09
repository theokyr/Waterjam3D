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

    private MultiplayerPeer _peer;
    private ulong _lobbyId;
    private ulong _hostSteamId;
    private string _lobbyJoinCode;
    private bool _isHost;
    private bool _isConnected;
    private bool _lobbyCreationPending;
    private ulong _preferredLobbyId; // If set, reuse this lobby instead of creating a new one
    private Dictionary<ulong, int> _steamIdToPeerId = new();
    private Dictionary<int, ulong> _peerIdToSteamId = new();

    public MultiplayerPeer Peer => _peer;

    public SteamNetworkAdapter()
    {
        // Wire up Steam lobby and P2P callbacks as soon as the Steam class is available
        try
        {
            if (ClassDB.ClassExists("Steam") && ClassDB.CanInstantiate("Steam"))
            {
                Steam.LobbyCreated += OnLobbyCreated;
                Steam.LobbyMatchList += OnLobbyMatchList;
                Steam.LobbyJoined += OnLobbyJoined;
                Steam.LobbyChatUpdate += OnLobbyChatUpdate;
                ConsoleSystem.Log("[SteamNetworkAdapter] Steam callbacks registered (class available)", ConsoleChannel.Network);
            }
            else
            {
                ConsoleSystem.LogWarn("[SteamNetworkAdapter] Steam class not available; callbacks not registered", ConsoleChannel.Network);
            }
        }
        catch (System.Exception ex)
        {
            ConsoleSystem.LogWarn($"[SteamNetworkAdapter] Failed to register Steam callbacks: {ex.Message}", ConsoleChannel.Network);
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
            // If a preferred lobby is provided (party lobby), remember it for discovery purposes, but do not create/modify lobbies here
            if (_preferredLobbyId != 0)
            {
                _lobbyId = _preferredLobbyId;
                _preferredLobbyId = 0;
                ConsoleSystem.Log($"[SteamNetworkAdapter] Using existing Steam lobby {_lobbyId} for discovery (no lobby creation)", ConsoleChannel.Network);
            }

            // Always create the host peer directly
            var inst = ClassDB.Instantiate("SteamMultiplayerPeer");
            GodotObject obj = (GodotObject)inst;
            if (obj is MultiplayerPeer mp)
            {
                var createErr = obj.Call("create_host");
                long err = 0;
                try { err = (long)createErr; } catch { err = 0; }
                if (err == 0)
                {
                    _peer = mp;
                    var tree = Engine.GetMainLoop() as SceneTree;
                    tree?.GetMultiplayer().SetMultiplayerPeer(_peer);
                    _isHost = true;
                    _isConnected = true;
                    ConsoleSystem.Log("[SteamNetworkAdapter] SteamMultiplayerPeer host created", ConsoleChannel.Network);
                    return true;
                }
                else
                {
                    ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to create host: {err}", ConsoleChannel.Network);
                    return false;
                }
            }
            else
            {
                ConsoleSystem.LogErr("[SteamNetworkAdapter] Could not instantiate SteamMultiplayerPeer for host", ConsoleChannel.Network);
                return false;
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to start host: {ex.Message}", ConsoleChannel.Network);
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

        // We no longer create host here; StartServer handles host peer creation. Store lobby info only.
        _lobbyId = lobbyId;
        _lobbyJoinCode = GenerateLobbyCode();
        Steam.SetLobbyData(lobbyId, "join_code", _lobbyJoinCode);
        Steam.SetLobbyData(lobbyId, "name", "Game Lobby");
        ConsoleSystem.Log($"[SteamNetworkAdapter] Steam lobby created (discovery only). ID: {lobbyId}, Join Code: {_lobbyJoinCode}", ConsoleChannel.Network);
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
            // Client connection is handled by PartyService to avoid duplicate peer creation
            ConsoleSystem.Log("[SteamNetworkAdapter] Client join detected; PartyService will manage peer creation.", ConsoleChannel.Network);
        }
    }

    private void OnLobbyChatUpdate(ulong lobbyId, long changedId, long makingChangeId, long chatState)
    {
        // Only process events for our current lobby
        if (lobbyId != _lobbyId) return;

        var stateChange = (Steam.ChatMemberStateChange)chatState;
        var changedSteamId = (ulong)changedId;
        ConsoleSystem.Log($"[SteamNetworkAdapter] Lobby chat update: {stateChange} for user {changedSteamId} in lobby {lobbyId}", ConsoleChannel.Network);

        try
        {
            // Check if this is a party lobby (has party metadata)
            var lobbyType = Steam.GetLobbyData(lobbyId, "lobby_type");
            var partyId = Steam.GetLobbyData(lobbyId, "party_id");
            var isPartyLobby = lobbyType == "party" && !string.IsNullOrEmpty(partyId);

            // Handle member join/leave events
            if (stateChange.HasFlag(Steam.ChatMemberStateChange.Entered))
            {
                ConsoleSystem.Log($"[SteamNetworkAdapter] Steam user {changedSteamId} joined lobby", ConsoleChannel.Network);
                
                // Dispatch party event if this is a party lobby
                if (isPartyLobby)
                {
                    var playerName = Steam.GetFriendPersonaName(changedSteamId);
                    var playerId = changedSteamId.ToString();
                    var member = new Waterjam.Domain.Party.PartyMember(playerId, false, playerName);
                    
                    GameEvent.DispatchGlobal(new PartyMemberJoinedEvent(partyId, member));
                    ConsoleSystem.Log($"[SteamNetworkAdapter] Dispatched PartyMemberJoinedEvent for {playerName}", ConsoleChannel.Network);
                }
            }
            else if (stateChange.HasFlag(Steam.ChatMemberStateChange.Left) || 
                     stateChange.HasFlag(Steam.ChatMemberStateChange.Disconnected) ||
                     stateChange.HasFlag(Steam.ChatMemberStateChange.Kicked) ||
                     stateChange.HasFlag(Steam.ChatMemberStateChange.Banned))
            {
                var playerId = changedSteamId.ToString();
                ConsoleSystem.Log($"[SteamNetworkAdapter] Steam user {changedSteamId} left lobby", ConsoleChannel.Network);
                
                // Dispatch party event if this is a party lobby
                if (isPartyLobby)
                {
                    GameEvent.DispatchGlobal(new PartyMemberLeftEvent(partyId, playerId));
                    ConsoleSystem.Log($"[SteamNetworkAdapter] Dispatched PartyMemberLeftEvent for {playerId}", ConsoleChannel.Network);
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Error handling lobby chat update: {ex.Message}", ConsoleChannel.Network);
        }
    }

    private void OnP2PSessionRequest(ulong steamIdRemote) { }

    private void OnP2PSessionConnectFail(ulong steamIdRemote, long sessionError) { }

    public bool Connect(string address, int port)
    {
        if (!PlatformService.IsSteamInitialized)
        {
            ConsoleSystem.LogErr("[SteamNetworkAdapter] Cannot connect: Steam not initialized", ConsoleChannel.Network);
            return false;
        }

        try
        {
            // Connection via lobby code; lobby join will trigger client creation
            return ConnectToLobby(address);
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

    private bool ConnectToSteamId(ulong steamId) { return false; }

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
                Steam.LobbyChatUpdate -= OnLobbyChatUpdate;
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
                var tree = Engine.GetMainLoop() as SceneTree;
                if (tree?.GetMultiplayer() != null)
                {
                    tree.GetMultiplayer().MultiplayerPeer = null;
                }
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
    public void Update(double delta) { }

    private void HandleP2PPackets() { }

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
    /// For clients, this includes waiting for peer assignment.
    /// </summary>
    public bool IsConnected()
    {
        if (_isHost)
        {
            return _isConnected;
        }
        else
        {
            // Clients are connected once the addon peer is set
            return _isConnected;
        }
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


