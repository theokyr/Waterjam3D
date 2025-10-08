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

    private MultiplayerPeer _peer;
    private ulong _lobbyId;
    private ulong _hostSteamId;
    private string _lobbyJoinCode;
    private bool _isHost;
    private bool _isConnected;
    private bool _lobbyCreationPending;

    public MultiplayerPeer Peer => _peer;

    public SteamNetworkAdapter()
    {
        // Wire up Steam lobby callbacks
        if (PlatformService.IsSteamInitialized)
        {
            Steam.LobbyCreated += OnLobbyCreated;
            Steam.LobbyMatchList += OnLobbyMatchList;
            // Note: LobbyJoinRequested is not available in current GodotSteam C# bindings
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

        // Now create the actual networking peer (using ENet for reliable networking)
        CreateLobbyPeer();
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
            }

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
        if (!PlatformService.IsSteamInitialized) return;

        try
        {
            // Check for incoming P2P packets
            // Poll channel 0 only for now
            while (Steam.GetAvailableP2PPacketSize() > 0)
            {
                var packetInfo = Steam.ReadP2PPacket(1024, 0);
                if (packetInfo != null && packetInfo.ContainsKey("data") && packetInfo.ContainsKey("steam_id_remote"))
                {
                    var data = packetInfo["data"].AsByteArray();
                    var from = packetInfo["steam_id_remote"].AsUInt64();
                    // TODO: route 'data' to higher-level message handler
                    ConsoleSystem.Log($"[SteamNetworkAdapter] Received P2P packet from {from} ({data.Length} bytes)", ConsoleChannel.Network);
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Error handling P2P packets: {ex.Message}", ConsoleChannel.Network);
        }
    }

    private void CreateLobbyPeer()
    {
        try
        {
            // Hybrid approach: Use Steam for matchmaking/lobby, ENet for actual game networking
            // This gives us reliable networking with Steam friend integration
            _peer = new ENetMultiplayerPeer();

            // Initialize as a host if we're the lobby owner
            if (_isHost)
            {
                // Use a consistent port for hosting (clients will connect to host's public IP via Steam)
                var error = ((ENetMultiplayerPeer)_peer).CreateServer(7777, 32);
                if (error != Error.Ok)
                {
                    ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to create server peer: {error}", ConsoleChannel.Network);
                    return;
                }

                ConsoleSystem.Log("[SteamNetworkAdapter] Created ENet server peer on port 7777", ConsoleChannel.Network);
            }
            else
            {
                // Client will connect via ENet to the host
                // The host's IP will be shared via Steam lobby data
                ConsoleSystem.Log("[SteamNetworkAdapter] Prepared client peer", ConsoleChannel.Network);
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamNetworkAdapter] Failed to create lobby peer: {ex.Message}", ConsoleChannel.Network);
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


