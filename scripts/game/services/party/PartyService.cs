using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotSteam;
using Waterjam.Core.Services;
using Waterjam.Core.Services.Network;
using Waterjam.Core.Systems.Console;
using Waterjam.Domain.Party;
using Waterjam.Domain.Chat;
using Waterjam.Events;

namespace Waterjam.Game.Services.Party;

/// <summary>
/// Service for managing parties and party invitations.
/// </summary>
public partial class PartyService : BaseService,
    IGameEventHandler<CreatePartyRequestEvent>,
    IGameEventHandler<JoinPartyRequestEvent>,
    IGameEventHandler<LeavePartyRequestEvent>,
    IGameEventHandler<InviteToPartyRequestEvent>,
    IGameEventHandler<RespondToPartyInviteRequestEvent>
{
    private readonly Dictionary<string, Waterjam.Domain.Party.Party> _parties = new();
    private readonly Dictionary<string, Waterjam.Domain.Party.PartyInvite> _invites = new();
    private readonly Dictionary<string, string> _playerPartyMap = new(); // playerId -> partyId
    private readonly Dictionary<string, ChatChannel> _partyChatChannels = new(); // partyId -> chatChannel

    private string _localPlayerId;
    private Random _random = new();
    private ulong _currentSteamLobbyId = 0; // Track the current party's Steam lobby ID for invites
    private string _lastConnectLeaderId = null; // Prevent repeated client connect attempts
    private bool _gameStartingProcessed = false; // Prevent infinite loop when client processes game_starting flag

    public override void _Ready()
    {
        base._Ready();
        ConsoleSystem.Log("PartyService initialized", ConsoleChannel.Game);

        // Set up Steam callbacks if Steam is available
        if (PlatformService.IsSteamInitialized)
        {
            SetupSteamCallbacks();
            // Initialize local player ID from Steam for frictionless UI/avatar flows
            try
            {
                var sid = Steam.GetSteamID();
                if (sid != 0)
                {
                    _localPlayerId = sid.ToString();
                    ConsoleSystem.Log($"[PartyService] Local player set from Steam: {_localPlayerId}", ConsoleChannel.Game);
                }
            }
            catch (Exception ex)
            {
                ConsoleSystem.LogWarn($"[PartyService] Failed to read SteamID: {ex.Message}", ConsoleChannel.Game);
            }
        }
        else if (string.IsNullOrEmpty(_localPlayerId))
        {
            // Fallback local ID for non-Steam environments
            _localPlayerId = "local";
        }

        // Register console commands for debugging
        RegisterConsoleCommands();
    }

    /// <summary>
    /// Ensure the Steam addon client peer is created and connected to the given host Steam ID.
    /// Safe to call multiple times; no-ops if already connected.
    /// </summary>
    private void EnsureClientPeerConnected(ulong hostSteamId, string context)
    {
        try
        {
            var mpApi = GetTree()?.GetMultiplayer();
            if (mpApi == null)
            {
                ConsoleSystem.LogWarn($"[PartyService] ({context}) Multiplayer API missing", ConsoleChannel.Network);
                return;
            }

            var currentPeer = mpApi.MultiplayerPeer;
            if (currentPeer != null && currentPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
            {
                // Already connected
                return;
            }

            var inst = ClassDB.Instantiate("SteamMultiplayerPeer");
            GodotObject obj = (GodotObject)inst;
            if (obj is MultiplayerPeer peer)
            {
                var createErr = obj.Call("create_client", hostSteamId);
                long err = 0;
                try { err = (long)createErr; } catch { err = 0; }
                if (err == 0)
                {
                    mpApi.MultiplayerPeer = peer;
                    try
                    {
                        var status = peer.GetConnectionStatus();
                        var uid = mpApi.GetUniqueId();
                        ConsoleSystem.Log($"[PartyService] ({context}) Created SteamMultiplayerPeer client to host {hostSteamId}; status={status}, uid={uid}", ConsoleChannel.Network);
                    }
                    catch { }
                }
                else
                {
                    ConsoleSystem.LogErr($"[PartyService] ({context}) create_client returned {err}", ConsoleChannel.Network);
                }
            }
            else
            {
                ConsoleSystem.LogErr($"[PartyService] ({context}) Could not instantiate SteamMultiplayerPeer", ConsoleChannel.Network);
            }
        }
        catch (System.Exception ex)
        {
            ConsoleSystem.LogErr($"[PartyService] ({context}) EnsureClientPeerConnected failed: {ex.Message}", ConsoleChannel.Network);
        }
    }

    /// <summary>
    /// Public helper to ensure the local client is connected to the party leader via Steam.
    /// Used by UI flows when a multiplayer game is starting.
    /// </summary>
    public void EnsureClientConnectedToLeader()
    {
        try
        {
            if (!PlatformService.IsSteamInitialized)
            {
                return;
            }

            var lobbyId = _currentSteamLobbyId;
            if (lobbyId == 0)
            {
                return;
            }

            var leaderSteamId = Steam.GetLobbyOwner(lobbyId);
            if (leaderSteamId != 0)
            {
                EnsureClientPeerConnected(leaderSteamId, "EnsureClientConnectedToLeader");
            }
        }
        catch (System.Exception ex)
        {
            ConsoleSystem.LogWarn($"[PartyService] EnsureClientConnectedToLeader failed: {ex.Message}", ConsoleChannel.Network);
        }
    }

    private void SetupSteamCallbacks()
    {
        // Listen for Steam lobby events that affect party management
        Steam.LobbyChatUpdate += OnSteamLobbyChatUpdate;
        Steam.LobbyCreated += OnSteamLobbyCreated;
        Steam.LobbyJoined += OnSteamLobbyJoined;
        Steam.LobbyDataUpdate += OnSteamLobbyDataUpdate;
        Steam.LobbyMessage += OnSteamLobbyMessage;
    }

    private void OnSteamLobbyCreated(long result, ulong lobbyId)
    {
        if (result == 1) // k_EResultOK
        {
            _currentSteamLobbyId = lobbyId;
            ConsoleSystem.Log($"[PartyService] Steam lobby created for party: {lobbyId}", ConsoleChannel.Game);
            
            // Set lobby type to party so peers know this is a party lobby
            Steam.SetLobbyData(lobbyId, "lobby_type", "party");
            // Ensure standard name key used by NetworkService filtering
            try { Steam.SetLobbyData(lobbyId, "name", "Waterjam3D"); } catch {}
            
            // Store party info
            var party = GetCurrentPlayerParty();
            if (party != null)
            {
                Steam.SetLobbyData(lobbyId, "party_id", party.PartyId);
                Steam.SetLobbyData(lobbyId, "party_name", party.DisplayName);
                Steam.SetLobbyData(lobbyId, "party_code", party.PartyCode);
            }
        }
        else
        {
            ConsoleSystem.LogErr($"[PartyService] Failed to create Steam lobby: result {result}", ConsoleChannel.Game);
        }
    }

    private void OnSteamLobbyJoined(ulong lobbyId, long permissions, bool locked, long response)
    {
        if (response != 1) return; // Only proceed if join was successful
        
        // Check if this is a party lobby
        var lobbyType = Steam.GetLobbyData(lobbyId, "lobby_type");
        if (lobbyType == "party")
        {
            var partyId = Steam.GetLobbyData(lobbyId, "party_id");
            var partyName = Steam.GetLobbyData(lobbyId, "party_name");
            var partyCode = Steam.GetLobbyData(lobbyId, "party_code");
            
            if (!string.IsNullOrEmpty(partyId) && !_parties.ContainsKey(partyId))
            {
                // We joined a party lobby - create local party state
                var hostSteamId = Steam.GetLobbyOwner(lobbyId);
                var hostId = hostSteamId.ToString();

                var party = new Waterjam.Domain.Party.Party(partyId, partyCode, hostId, partyName);
                _parties[partyId] = party;
                _playerPartyMap[_localPlayerId] = partyId;
                _currentSteamLobbyId = lobbyId;

                // Add ourselves as a member (not as leader)
                var localName = Steam.GetPersonaName();
                var localMember = new PartyMember(_localPlayerId, false, localName);
                party.AddMember(localMember);

                // Create chat channel
                var chatChannel = new ChatChannel(partyId, $"Party: {party.DisplayName}", ChatChannelType.Party);
                _partyChatChannels[partyId] = chatChannel;

                ConsoleSystem.Log($"[PartyService] Joined party '{partyName}' via Steam lobby, host: {hostId}, local: {_localPlayerId}", ConsoleChannel.Game);
                GameEvent.DispatchGlobal(new PartyJoinedEvent(partyId, _localPlayerId));
            }
            else if (!string.IsNullOrEmpty(partyId) && _parties.ContainsKey(partyId))
            {
                // We joined a party lobby but already have local party state
                var party = _parties[partyId];
                if (!_playerPartyMap.ContainsKey(_localPlayerId))
                {
                    _playerPartyMap[_localPlayerId] = partyId;
                    _currentSteamLobbyId = lobbyId;

                    // Add ourselves as a member if not already present
                    var localName = Steam.GetPersonaName();
                    var localMember = new Waterjam.Domain.Party.PartyMember(_localPlayerId, false, localName);
                    if (!party.ContainsPlayer(_localPlayerId))
                    {
                        party.AddMember(localMember);
                    }

                    ConsoleSystem.Log($"[PartyService] Updated existing party state for joined lobby, party: {party.DisplayName}", ConsoleChannel.Game);
                    GameEvent.DispatchGlobal(new PartyJoinedEvent(partyId, _localPlayerId));
                }
            }
        }

        // Attempt to ensure client is connected to leader on any successful lobby join
        try
        {
            var hostSteamId = Steam.GetLobbyOwner(lobbyId);
            if (hostSteamId != 0)
            {
                EnsureClientPeerConnected(hostSteamId, "OnSteamLobbyJoined(initial)");
            }
        }
        catch (System.Exception ex)
        {
            ConsoleSystem.LogWarn($"[PartyService] Failed to ensure client peer on initial lobby join: {ex.Message}", ConsoleChannel.Network);
        }

        // If a game is already launched by the host, immediately load that scene
        try
        {
            var gameLaunched = Steam.GetLobbyData(lobbyId, "game_launched");
            var gameScenePath = Steam.GetLobbyData(lobbyId, "game_scene_path");
            var gameLeader = Steam.GetLobbyData(lobbyId, "game_leader");

            if (!string.IsNullOrEmpty(gameLaunched) && gameLaunched == "true")
            {
                // Ensure client peer exists and is connected to host
                try
                {
                    if (!string.IsNullOrEmpty(gameLeader) && ulong.TryParse(gameLeader, out var leaderSteamId))
                    {
                        EnsureClientPeerConnected(leaderSteamId, "OnSteamLobbyJoined");
                    }
                    else
                    {
                        ConsoleSystem.LogWarn("[PartyService] game_leader missing or invalid on game_launched (OnSteamLobbyJoined)", ConsoleChannel.Network);
                    }
                }
                catch (System.Exception ex)
                {
                    ConsoleSystem.LogWarn($"[PartyService] Failed to ensure client peer on game_launched: {ex.Message}", ConsoleChannel.Network);
                }

                // Ensure scene path fallback
                var scenePath = !string.IsNullOrEmpty(gameScenePath) ? gameScenePath : "res://scenes/dev/dev.tscn";
                ConsoleSystem.Log($"[PartyService] Lobby has game_launched=true; loading scene: {scenePath}", ConsoleChannel.Game);

                // Trigger the game start flow locally
                GameEvent.DispatchGlobal(new NewGameStartedEvent(scenePath));
            }
        }
        catch (System.Exception ex)
        {
            ConsoleSystem.LogWarn($"[PartyService] Failed to auto-load launched game scene: {ex.Message}", ConsoleChannel.Game);
        }
    }

    private void OnSteamLobbyDataUpdate(uint success, ulong lobbyId, ulong memberId)
    {
        if (success != 1) return; // 1 = k_EResultOK

        // Check for game starting message
        try
        {
            var gameStarting = Steam.GetLobbyData(lobbyId, "game_starting");
            var gameLaunched = Steam.GetLobbyData(lobbyId, "game_launched");
            var gameScenePath = Steam.GetLobbyData(lobbyId, "game_scene_path");
            var gameLeader = Steam.GetLobbyData(lobbyId, "game_leader");
            var gameLobbyId = Steam.GetLobbyData(lobbyId, "game_lobby_id");

            // Check if game has been launched (host pressed "Start Game" in lobby panel)
            if (!string.IsNullOrEmpty(gameLaunched) && gameLaunched == "true")
            {
                if (!string.IsNullOrEmpty(gameLeader) && gameLeader != _localPlayerId)
                {
                    ConsoleSystem.Log($"[PartyService] Game launched by host, loading scene: {gameScenePath}", ConsoleChannel.Game);

                    // Ensure client has a connected peer to the host
                    try
                    {
                        if (ulong.TryParse(gameLeader, out var leaderSteamId))
                        {
                            EnsureClientPeerConnected(leaderSteamId, "OnSteamLobbyDataUpdate");
                        }
                        else
                        {
                            ConsoleSystem.LogWarn("[PartyService] game_leader invalid on lobby data update", ConsoleChannel.Network);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        ConsoleSystem.LogWarn($"[PartyService] Failed to ensure client peer on lobby data: {ex.Message}", ConsoleChannel.Network);
                    }

                    // Load the scene directly
                    var scenePath = !string.IsNullOrEmpty(gameScenePath) ? gameScenePath : "res://scenes/dev/dev.tscn";
                    GameEvent.DispatchGlobal(new NewGameStartedEvent(scenePath));

                    // Note: Don't clear the flag here - let the host do it
                    // Clients shouldn't modify lobby data
                    return;
                }
            }

            // New flow: If game is starting and we're not the leader, trigger the game start flow
            if (!string.IsNullOrEmpty(gameStarting) && gameStarting == "true" && !_gameStartingProcessed)
            {
                if (!string.IsNullOrEmpty(gameLeader) && gameLeader != _localPlayerId)
                {
                    _gameStartingProcessed = true; // Prevent processing this again
                    ConsoleSystem.Log("[PartyService] Detected game starting, triggering multiplayer join flow", ConsoleChannel.Game);
                    
                    // Join the game lobby
                    if (!string.IsNullOrEmpty(gameLobbyId))
                    {
                        ConsoleSystem.Log($"[PartyService] Joining game lobby {gameLobbyId}", ConsoleChannel.Game);
                        GameEvent.DispatchGlobal(new JoinLobbyRequestEvent(gameLobbyId));
                    }
                    
                    // Trigger the main menu to show lobby UI (this will handle networking connection)
                    GameEvent.DispatchGlobal(new UiShowLobbyScreenEvent());

                        // Also ensure client peer proactively if leader is known
                        if (ulong.TryParse(gameLeader, out var leaderSteamId))
                        {
                            EnsureClientPeerConnected(leaderSteamId, "OnSteamLobbyDataUpdate(game_starting)");
                        }
                    return;
                }
            }
            
            // Old flow support (legacy)
            var leaderId = Steam.GetLobbyData(lobbyId, "lobby_leader_id");
            var nav = Steam.GetLobbyData(lobbyId, "navigate_to_lobby");

            if (!string.IsNullOrEmpty(nav) && nav == "true")
            {
                // If we are not the leader, navigate to lobby UI
                if (!string.IsNullOrEmpty(leaderId) && leaderId != _localPlayerId)
                {
                    ConsoleSystem.Log("[PartyService] LobbyDataUpdate detected navigation to lobby (legacy)", ConsoleChannel.Game);
                    GameEvent.DispatchGlobal(new UiShowLobbyScreenEvent());

                    // If we have a game lobby ID, try to join that lobby
                    if (!string.IsNullOrEmpty(gameLobbyId))
                    {
                        ConsoleSystem.Log($"[PartyService] Found game lobby ID {gameLobbyId}, attempting to join", ConsoleChannel.Game);
                        GameEvent.DispatchGlobal(new JoinLobbyRequestEvent(gameLobbyId));
                    }

                    // With addon peer, client creation happens via lobby join; skip direct network connect
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogWarn($"[PartyService] Error in LobbyDataUpdate: {ex.Message}", ConsoleChannel.Game);
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (PlatformService.IsSteamInitialized)
        {
            Steam.RunCallbacks();
        }
    }

    /// <summary>
    /// Gets the current Steam lobby ID for the party (for invites).
    /// </summary>
    public ulong GetCurrentSteamLobbyId()
    {
        return _currentSteamLobbyId;
    }

    /// <summary>
    /// Gets the party that a player is currently in.
    /// </summary>
    public Waterjam.Domain.Party.Party GetPlayerParty(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
        {
            return null;
        }

        if (_playerPartyMap.TryGetValue(playerId, out var partyId))
        {
            return _parties.GetValueOrDefault(partyId);
        }
        return null;
    }

    /// <summary>
    /// Gets the current player's party.
    /// </summary>
    public Waterjam.Domain.Party.Party GetCurrentPlayerParty()
    {
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            return null;
        }
        return GetPlayerParty(_localPlayerId);
    }

    /// <summary>
    /// Gets all active parties.
    /// </summary>
    public IReadOnlyCollection<Waterjam.Domain.Party.Party> GetAllParties()
    {
        return _parties.Values.ToList();
    }

    /// <summary>
    /// Gets all pending invites for a player.
    /// </summary>
    public IReadOnlyCollection<Waterjam.Domain.Party.PartyInvite> GetPlayerInvites(string playerId)
    {
        return _invites.Values
            .Where(invite => invite.ToPlayerId == playerId && invite.IsValid())
            .ToList();
    }

    /// <summary>
    /// Gets a party by ID.
    /// </summary>
    public Waterjam.Domain.Party.Party GetParty(string partyId)
    {
        return _parties.GetValueOrDefault(partyId);
    }

    /// <summary>
    /// Gets a party by its code.
    /// </summary>
    public Waterjam.Domain.Party.Party GetPartyByCode(string partyCode)
    {
        return _parties.Values.FirstOrDefault(party => party.PartyCode == partyCode);
    }

    /// <summary>
    /// Gets the chat channel for a party.
    /// </summary>
    public ChatChannel GetPartyChatChannel(string partyId)
    {
        return _partyChatChannels.GetValueOrDefault(partyId);
    }

    /// <summary>
    /// Sends a chat message to a party.
    /// </summary>
    public ChatMessage SendPartyChatMessage(string partyId, string senderPlayerId, string senderDisplayName, string content)
    {
        var chatChannel = _partyChatChannels.GetValueOrDefault(partyId);
        if (chatChannel == null)
        {
            ConsoleSystem.LogErr($"No chat channel found for party {partyId}", ConsoleChannel.Game);
            return null;
        }

        var message = chatChannel.SendMessage(senderPlayerId, senderDisplayName, content);
        GameEvent.DispatchGlobal(new PartyChatMessageEvent(partyId, message));

        // Broadcast message via Steam lobby chat if we're in a Steam lobby
        if (PlatformService.IsSteamInitialized && _currentSteamLobbyId != 0)
        {
            try
            {
                // Format: CHAT|senderPlayerId|senderDisplayName|content
                var chatMessage = $"CHAT|{senderPlayerId}|{senderDisplayName}|{content}";
                Steam.SendLobbyChatMsg(_currentSteamLobbyId, chatMessage);
                ConsoleSystem.Log($"[PartyService] Sent chat message via Steam lobby", ConsoleChannel.Game);
            }
            catch (Exception ex)
            {
                ConsoleSystem.LogWarn($"[PartyService] Failed to send chat via Steam: {ex.Message}", ConsoleChannel.Game);
            }
        }

        return message;
    }
    
    private void OnSteamLobbyMessage(ulong lobbyId, long userId, string message, long chatType)
    {
        try
        {
            // Only process our party's lobby
            if (lobbyId != _currentSteamLobbyId) return;
            
            // Parse message format: CHAT|senderPlayerId|senderDisplayName|content
            var parts = message.Split('|', 4);
            if (parts.Length == 4 && parts[0] == "CHAT")
            {
                var senderPlayerId = parts[1];
                var senderDisplayName = parts[2];
                var content = parts[3];
                
                // Don't process our own messages (already added locally)
                if (senderPlayerId == _localPlayerId) return;
                
                // Find the party for this lobby
                var currentParty = GetCurrentPlayerParty();
                if (currentParty == null) return;
                
                var chatChannel = _partyChatChannels.GetValueOrDefault(currentParty.PartyId);
                if (chatChannel == null) return;
                
                // Add the message and dispatch event
                var chatMessage = chatChannel.SendMessage(senderPlayerId, senderDisplayName, content);
                GameEvent.DispatchGlobal(new PartyChatMessageEvent(currentParty.PartyId, chatMessage));
                
                ConsoleSystem.Log($"[PartyService] Received chat message from {senderDisplayName}: {content}", ConsoleChannel.Game);
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[PartyService] Error processing lobby chat message: {ex.Message}", ConsoleChannel.Game);
        }
    }

    /// <summary>
    /// Generates a unique party code.
    /// </summary>
    private string GeneratePartyCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        string code;

        do
        {
            code = new string(Enumerable.Range(0, 6)
                .Select(_ => chars[_random.Next(chars.Length)])
                .ToArray());
        }
        while (_parties.Values.Any(p => p.PartyCode == code));

        return code;
    }

    /// <summary>
    /// Generates a unique party ID.
    /// </summary>
    private string GeneratePartyId()
    {
        return Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Generates a unique invite ID.
    /// </summary>
    private string GenerateInviteId()
    {
        return Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Sets the local player ID (usually called when player logs in).
    /// </summary>
    public void SetLocalPlayerId(string playerId)
    {
        _localPlayerId = playerId;
        ConsoleSystem.Log($"Local player ID set to: {playerId}", ConsoleChannel.Game);
    }

    /// <summary>
    /// Gets the local player ID.
    /// </summary>
    public string GetLocalPlayerId()
    {
        return _localPlayerId;
    }

    public void OnGameEvent(CreatePartyRequestEvent eventArgs)
    {
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            ConsoleSystem.LogErr("Cannot create party: local player ID not set", ConsoleChannel.Game);
            return;
        }

        try
        {
            var partyId = GeneratePartyId();
            var partyCode = GeneratePartyCode();
            var party = new Waterjam.Domain.Party.Party(partyId, partyCode, _localPlayerId, eventArgs.DisplayName);

            _parties[partyId] = party;
            _playerPartyMap[_localPlayerId] = partyId;

            // Create chat channel for the party
            var chatChannel = new ChatChannel(partyId, $"Party: {party.DisplayName}", ChatChannelType.Party);
            _partyChatChannels[partyId] = chatChannel;

            ConsoleSystem.Log($"Created party '{party.DisplayName}' with code '{partyCode}'", ConsoleChannel.Game);

            // Create a Steam lobby for social/invite purposes
            // This lobby will be reused for networking when the game starts
            if (PlatformService.IsSteamInitialized)
            {
                CreateSteamLobby(party, eventArgs.MaxMembers);
            }

            GameEvent.DispatchGlobal(new PartyCreatedEvent(partyId, partyCode, _localPlayerId, party.DisplayName));
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to create party: {ex.Message}", ConsoleChannel.Game);
        }
    }

    private void CreateSteamLobby(Waterjam.Domain.Party.Party party, int maxMembers)
    {
        try
        {
            Steam.CreateLobby(Steam.LobbyType.FriendsOnly, maxMembers);

            // Store party info in lobby data (will be set in callback)
            ConsoleSystem.Log($"Creating Steam lobby for party '{party.DisplayName}'", ConsoleChannel.Game);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to create Steam lobby: {ex.Message}", ConsoleChannel.Game);
        }
    }

    private void OnSteamLobbyChatUpdate(ulong lobbyId, long changedId, long makingChangeId, long chatState)
    {
        var stateChange = (Steam.ChatMemberStateChange)chatState;
        ConsoleSystem.Log($"Steam lobby chat update: {stateChange} for user {changedId} in lobby {lobbyId}", ConsoleChannel.Game);

        // Handle player join/leave events from Steam lobby
        if (stateChange.HasFlag(Steam.ChatMemberStateChange.Entered))
        {
            var playerName = Steam.GetFriendPersonaName((ulong)changedId);
            var playerId = ((ulong)changedId).ToString();

            // If this player isn't already in our party, add them
            var currentParty = GetCurrentPlayerParty();
            if (currentParty != null && !currentParty.ContainsPlayer(playerId))
            {
                var member = new Waterjam.Domain.Party.PartyMember(playerId, false, playerName);
                currentParty.AddMember(member);

                ConsoleSystem.Log($"Added Steam user {playerName} to party via lobby join", ConsoleChannel.Game);
                GameEvent.DispatchGlobal(new PartyMemberJoinedEvent(currentParty.PartyId, member));
            }
        }
        else if (stateChange.HasFlag(Steam.ChatMemberStateChange.Left))
        {
            var playerId = changedId.ToString();

            // Remove player from party if they're leaving the lobby
            var currentParty = GetCurrentPlayerParty();
            if (currentParty != null && currentParty.ContainsPlayer(playerId))
            {
                currentParty.RemoveMember(playerId);

                ConsoleSystem.Log($"Removed Steam user {playerId} from party via lobby leave", ConsoleChannel.Game);
                GameEvent.DispatchGlobal(new PartyMemberLeftEvent(currentParty.PartyId, playerId));
            }
        }
    }

    public void OnGameEvent(JoinPartyRequestEvent eventArgs)
    {
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            ConsoleSystem.LogErr("Cannot join party: local player ID not set", ConsoleChannel.Game);
            return;
        }

        try
        {
            var party = GetPartyByCode(eventArgs.PartyCode);
            if (party == null)
            {
                ConsoleSystem.LogErr($"Party with code '{eventArgs.PartyCode}' not found", ConsoleChannel.Game);
                return;
            }

            if (party.ContainsPlayer(_localPlayerId))
            {
                ConsoleSystem.Log($"Player {_localPlayerId} is already in party {party.DisplayName}", ConsoleChannel.Game);
                return;
            }

            var member = new Waterjam.Domain.Party.PartyMember(_localPlayerId);
            party.AddMember(member);
            _playerPartyMap[_localPlayerId] = party.PartyId;

            // Add player to party chat channel
            var chatChannel = _partyChatChannels.GetValueOrDefault(party.PartyId);
            if (chatChannel != null)
            {
                chatChannel.AddParticipant(_localPlayerId);
            }

            ConsoleSystem.Log($"Player {_localPlayerId} joined party '{party.DisplayName}'", ConsoleChannel.Game);

            GameEvent.DispatchGlobal(new PartyJoinedEvent(party.PartyId, _localPlayerId));
            GameEvent.DispatchGlobal(new PartyMemberJoinedEvent(party.PartyId, member));
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to join party: {ex.Message}", ConsoleChannel.Game);
        }
    }

    public void OnGameEvent(LeavePartyRequestEvent eventArgs)
    {
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            ConsoleSystem.LogErr("Cannot leave party: local player ID not set", ConsoleChannel.Game);
            return;
        }

        try
        {
            var party = GetPlayerParty(_localPlayerId);
            if (party == null)
            {
                ConsoleSystem.Log($"Player {_localPlayerId} is not in any party", ConsoleChannel.Game);
                return;
            }

            var playerId = _localPlayerId;
            party.RemoveMember(playerId);
            _playerPartyMap.Remove(playerId);

            // Remove player from party chat channel
            var chatChannel = _partyChatChannels.GetValueOrDefault(party.PartyId);
            if (chatChannel != null)
            {
                chatChannel.RemoveParticipant(playerId);
            }

            ConsoleSystem.Log($"Player {playerId} left party '{party.DisplayName}'", ConsoleChannel.Game);

            GameEvent.DispatchGlobal(new PartyLeftEvent(party.PartyId, playerId));
            GameEvent.DispatchGlobal(new PartyMemberLeftEvent(party.PartyId, playerId));

            // Disband party if empty
            if (party.Members.Count == 0)
            {
                _parties.Remove(party.PartyId);
                _partyChatChannels.Remove(party.PartyId);
                ConsoleSystem.Log($"Party '{party.DisplayName}' disbanded (empty)", ConsoleChannel.Game);
                GameEvent.DispatchGlobal(new PartyDisbandedEvent(party.PartyId));
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to leave party: {ex.Message}", ConsoleChannel.Game);
        }
    }

    public void OnGameEvent(InviteToPartyRequestEvent eventArgs)
    {
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            ConsoleSystem.LogErr("Cannot send invite: local player ID not set", ConsoleChannel.Game);
            return;
        }

        try
        {
            var party = GetPlayerParty(_localPlayerId);
            if (party == null)
            {
                // Frictionless flow: auto-create a party if not already in one
                var partyId = GeneratePartyId();
                var partyCode = GeneratePartyCode();
                var displayName = "My Party";
                try
                {
                    if (PlatformService.IsSteamInitialized)
                    {
                        var name = Steam.GetPersonaName();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            displayName = $"{name}'s Party";
                        }
                    }
                }
                catch { }

                party = new Waterjam.Domain.Party.Party(partyId, partyCode, _localPlayerId, displayName);
                _parties[partyId] = party;
                _playerPartyMap[_localPlayerId] = partyId;

                var chatChannel = new ChatChannel(partyId, $"Party: {displayName}", ChatChannelType.Party);
                _partyChatChannels[partyId] = chatChannel;

                ConsoleSystem.Log($"[PartyService] Auto-created party '{displayName}' to send invite.", ConsoleChannel.Game);

                // Create a Steam lobby for social/invite purposes
                // This lobby will be reused for networking when the game starts
                if (PlatformService.IsSteamInitialized)
                {
                    CreateSteamLobby(party, 8); // Default max 8 players
                }

                GameEvent.DispatchGlobal(new PartyCreatedEvent(partyId, partyCode, _localPlayerId, displayName));
            }

            if (party.LeaderPlayerId != _localPlayerId)
            {
                ConsoleSystem.LogErr("Cannot send invite: only party leader can invite players", ConsoleChannel.Game);
                return;
            }

            var inviteId = GenerateInviteId();
            var invite = new Waterjam.Domain.Party.PartyInvite(
                inviteId,
                party.PartyId,
                _localPlayerId,
                eventArgs.PlayerId,
                party.DisplayName,
                "Player", // TODO: Get actual display name
                eventArgs.Message
            );

            _invites[inviteId] = invite;

            ConsoleSystem.Log($"Sent party invite to player {eventArgs.PlayerId}", ConsoleChannel.Game);

            GameEvent.DispatchGlobal(new PartyInviteSentEvent(inviteId, _localPlayerId, eventArgs.PlayerId));
            GameEvent.DispatchGlobal(new PartyInviteReceivedEvent(invite));
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to send party invite: {ex.Message}", ConsoleChannel.Game);
        }
    }

    public void OnGameEvent(RespondToPartyInviteRequestEvent eventArgs)
    {
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            ConsoleSystem.LogErr("Cannot respond to invite: local player ID not set", ConsoleChannel.Game);
            return;
        }

        try
        {
            var invite = _invites.GetValueOrDefault(eventArgs.InviteId);
            if (invite == null || !invite.IsValid())
            {
                ConsoleSystem.LogErr($"Invalid or expired invite: {eventArgs.InviteId}", ConsoleChannel.Game);
                return;
            }

            if (invite.ToPlayerId != _localPlayerId)
            {
                ConsoleSystem.LogErr("Cannot respond to invite: invite is not for this player", ConsoleChannel.Game);
                return;
            }

            if (eventArgs.Accept)
            {
                var party = GetParty(invite.PartyId);
                if (party == null)
                {
                    ConsoleSystem.LogErr("Cannot accept invite: party no longer exists", ConsoleChannel.Game);
                    return;
                }

                var member = new Waterjam.Domain.Party.PartyMember(_localPlayerId);
                party.AddMember(member);
                _playerPartyMap[_localPlayerId] = party.PartyId;

                ConsoleSystem.Log($"Player {_localPlayerId} accepted party invite and joined '{party.DisplayName}'", ConsoleChannel.Game);

                GameEvent.DispatchGlobal(new PartyInviteAcceptedEvent(eventArgs.InviteId, party.PartyId, _localPlayerId));
                GameEvent.DispatchGlobal(new PartyJoinedEvent(party.PartyId, _localPlayerId));
                GameEvent.DispatchGlobal(new PartyMemberJoinedEvent(party.PartyId, member));
            }
            else
            {
                ConsoleSystem.Log($"Player {_localPlayerId} declined party invite", ConsoleChannel.Game);
                GameEvent.DispatchGlobal(new PartyInviteDeclinedEvent(eventArgs.InviteId, _localPlayerId));
            }

            invite.MarkAsUsed();
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to respond to party invite: {ex.Message}", ConsoleChannel.Game);
        }
    }

    private void RegisterConsoleCommands()
    {
        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "party_create",
            "Create a new party",
            "party_create [name]",
            async (args) =>
            {
                var displayName = args.Length > 0 ? string.Join(" ", args) : "My Party";
                GameEvent.DispatchGlobal(new CreatePartyRequestEvent(displayName));
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "party_join",
            "Join a party by code",
            "party_join <code>",
            async (args) =>
            {
                if (args.Length == 0)
                {
                    ConsoleSystem.Log("Usage: party_join <code>", ConsoleChannel.Game);
                    return false;
                }
                GameEvent.DispatchGlobal(new JoinPartyRequestEvent(args[0]));
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "party_leave",
            "Leave current party",
            "party_leave",
            async (args) =>
            {
                GameEvent.DispatchGlobal(new LeavePartyRequestEvent());
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "party_invite",
            "Invite player to current party",
            "party_invite <playerId> [message]",
            async (args) =>
            {
                if (args.Length == 0)
                {
                    ConsoleSystem.Log("Usage: party_invite <playerId> [message]", ConsoleChannel.Game);
                    return false;
                }
                var message = args.Length > 1 ? string.Join(" ", args.Skip(1)) : null;
                GameEvent.DispatchGlobal(new InviteToPartyRequestEvent(args[0], message));
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "party_list",
            "List all active parties",
            "party_list",
            async (args) =>
            {
                var parties = GetAllParties();
                if (parties.Count == 0)
                {
                    ConsoleSystem.Log("No active parties", ConsoleChannel.Game);
                    return true;
                }

                foreach (var party in parties)
                {
                    ConsoleSystem.Log($"Party: {party.DisplayName} (Code: {party.PartyCode}, Members: {party.Members.Count})", ConsoleChannel.Game);
                }
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "party_info",
            "Show current party info",
            "party_info",
            async (args) =>
            {
                var party = GetCurrentPlayerParty();
                if (party == null)
                {
                    ConsoleSystem.Log("Not in a party", ConsoleChannel.Game);
                    return true;
                }

                ConsoleSystem.Log($"Party: {party.DisplayName}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Code: {party.PartyCode}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Leader: {party.GetLeader()?.DisplayName ?? "Unknown"}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Members: {party.Members.Count}/{party.MaxMembers}", ConsoleChannel.Game);

                foreach (var member in party.Members)
                {
                    ConsoleSystem.Log($"  - {member.DisplayName} {(member.IsLeader ? "(Leader)" : "")}", ConsoleChannel.Game);
                }
                return true;
            }));
    }
}
