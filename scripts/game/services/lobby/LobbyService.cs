using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Waterjam.Core.Services;
using GodotSteam;
using Waterjam.Core.Systems.Console;
using Waterjam.Domain.Lobby;
using Waterjam.Domain.Chat;
using Waterjam.Events;

namespace Waterjam.Game.Services.Lobby;

/// <summary>
/// Service for managing game lobbies.
/// </summary>
public partial class LobbyService : BaseService,
    IGameEventHandler<CreateLobbyRequestEvent>,
    IGameEventHandler<JoinLobbyRequestEvent>,
    IGameEventHandler<LeaveLobbyRequestEvent>,
    IGameEventHandler<ChangeLobbyLeaderRequestEvent>,
    IGameEventHandler<UpdateLobbySettingsRequestEvent>,
    IGameEventHandler<StartGameRequestEvent>,
    IGameEventHandler<SetPlayerReadyRequestEvent>
{
    private readonly Dictionary<string, Waterjam.Domain.Lobby.Lobby> _lobbies = new();
    private readonly Dictionary<string, string> _playerLobbyMap = new(); // playerId -> lobbyId
    private readonly Dictionary<string, ChatChannel> _lobbyChatChannels = new(); // lobbyId -> chatChannel

    private string _localPlayerId;
    private Random _random = new();

    public override void _Ready()
    {
        base._Ready();
        ConsoleSystem.Log("LobbyService initialized", ConsoleChannel.Game);

        // Register console commands for debugging
        RegisterConsoleCommands();

        // Initialize local player ID from platform (e.g., Steam) for frictionless UI/flows
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            if (PlatformService.IsSteamInitialized)
            {
                try
                {
                    var sid = Steam.GetSteamID();
                    if (sid != 0)
                    {
                        _localPlayerId = sid.ToString();
                        ConsoleSystem.Log($"[LobbyService] Local player set from Steam: {_localPlayerId}", ConsoleChannel.Game);
                    }
                }
                catch (Exception ex)
                {
                    ConsoleSystem.LogWarn($"[LobbyService] Failed to read SteamID: {ex.Message}", ConsoleChannel.Game);
                }
            }

            // Fallback ID in non-Steam environments
            if (string.IsNullOrEmpty(_localPlayerId))
            {
                _localPlayerId = "local";
            }
        }
    }

    /// <summary>
    /// Gets the lobby that a player is currently in.
    /// </summary>
    public Waterjam.Domain.Lobby.Lobby GetPlayerLobby(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
        {
            return null;
        }
        if (_playerLobbyMap.TryGetValue(playerId, out var lobbyId))
        {
            return _lobbies.GetValueOrDefault(lobbyId);
        }
        return null;
    }

    /// <summary>
    /// Gets the current player's lobby.
    /// </summary>
    public Waterjam.Domain.Lobby.Lobby GetCurrentPlayerLobby()
    {
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            return null;
        }
        return GetPlayerLobby(_localPlayerId);
    }

    /// <summary>
    /// Gets all active lobbies.
    /// </summary>
    public IReadOnlyCollection<Waterjam.Domain.Lobby.Lobby> GetAllLobbies()
    {
        return _lobbies.Values.ToList();
    }

    /// <summary>
    /// Gets a lobby by ID.
    /// </summary>
    public Waterjam.Domain.Lobby.Lobby GetLobby(string lobbyId)
    {
        if (string.IsNullOrEmpty(lobbyId))
        {
            return null;
        }
        return _lobbies.GetValueOrDefault(lobbyId);
    }

    /// <summary>
    /// Gets the chat channel for a lobby.
    /// </summary>
    public ChatChannel GetLobbyChatChannel(string lobbyId)
    {
        if (string.IsNullOrEmpty(lobbyId))
        {
            return null;
        }
        return _lobbyChatChannels.GetValueOrDefault(lobbyId);
    }

    /// <summary>
    /// Sends a chat message to a lobby.
    /// </summary>
    public ChatMessage SendLobbyChatMessage(string lobbyId, string senderPlayerId, string senderDisplayName, string content)
    {
        var chatChannel = _lobbyChatChannels.GetValueOrDefault(lobbyId);
        if (chatChannel == null)
        {
            ConsoleSystem.LogErr($"No chat channel found for lobby {lobbyId}", ConsoleChannel.Game);
            return null;
        }

        var message = chatChannel.SendMessage(senderPlayerId, senderDisplayName, content);
        GameEvent.DispatchGlobal(new LobbyChatMessageEvent(lobbyId, message));

        return message;
    }

    /// <summary>
    /// Generates a unique lobby ID.
    /// </summary>
    private string GenerateLobbyId()
    {
        return Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Generates a unique join code for private lobbies.
    /// </summary>
    private string GenerateJoinCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        string code;

        do
        {
            code = new string(Enumerable.Range(0, 8)
                .Select(_ => chars[_random.Next(chars.Length)])
                .ToArray());
        }
        while (_lobbies.Values.Any(l => l.JoinCode == code));

        return code;
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

    public void OnGameEvent(CreateLobbyRequestEvent eventArgs)
    {
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            ConsoleSystem.LogErr("Cannot create lobby: local player ID not set", ConsoleChannel.Game);
            return;
        }

        try
        {
            var lobbyId = GenerateLobbyId();
            var lobby = new Waterjam.Domain.Lobby.Lobby(lobbyId, _localPlayerId, eventArgs.DisplayName, eventArgs.Settings);

            _lobbies[lobbyId] = lobby;
            _playerLobbyMap[_localPlayerId] = lobbyId;

            // If the player has a party, add all current party members to this lobby
            try
            {
                var partyService = GetNodeOrNull<Waterjam.Game.Services.Party.PartyService>("/root/PartyService");
                var currentParty = partyService?.GetCurrentPlayerParty();
                if (currentParty != null)
                {
                    foreach (var member in currentParty.Members)
                    {
                        if (!lobby.ContainsPlayer(member.PlayerId))
                        {
                            var lobbyPlayer = new LobbyPlayer(member.PlayerId, isLeader: member.PlayerId == lobby.LeaderPlayerId, displayName: member.DisplayName);
                            lobby.AddPlayer(lobbyPlayer);
                            _playerLobbyMap[member.PlayerId] = lobbyId;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ConsoleSystem.LogWarn($"[LobbyService] Failed to import party members into lobby: {ex.Message}", ConsoleChannel.Game);
            }

            // Create chat channel for the lobby and add current participants
            var chatChannel = new ChatChannel(lobbyId, $"Lobby: {lobby.DisplayName}", ChatChannelType.Lobby);
            _lobbyChatChannels[lobbyId] = chatChannel;
            foreach (var p in lobby.Players)
            {
                chatChannel.AddParticipant(p.PlayerId);
            }

            ConsoleSystem.Log($"Created lobby '{lobby.DisplayName}'", ConsoleChannel.Game);

            GameEvent.DispatchGlobal(new LobbyCreatedEvent(lobbyId, _localPlayerId, lobby.DisplayName));
            foreach (var p in lobby.Players)
            {
                GameEvent.DispatchGlobal(new LobbyPlayerJoinedEvent(lobbyId, p));
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to create lobby: {ex.Message}", ConsoleChannel.Game);
        }
    }

    public void OnGameEvent(JoinLobbyRequestEvent eventArgs)
    {
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            ConsoleSystem.LogErr("Cannot join lobby: local player ID not set", ConsoleChannel.Game);
            return;
        }

        try
        {
            var lobby = GetLobby(eventArgs.LobbyId);
            if (lobby == null)
            {
                ConsoleSystem.LogErr($"Lobby '{eventArgs.LobbyId}' not found", ConsoleChannel.Game);
                return;
            }

            if (lobby.ContainsPlayer(_localPlayerId))
            {
                ConsoleSystem.Log($"Player {_localPlayerId} is already in lobby {lobby.DisplayName}", ConsoleChannel.Game);
                return;
            }

            var player = new Waterjam.Domain.Lobby.LobbyPlayer(_localPlayerId);
            lobby.AddPlayer(player);
            _playerLobbyMap[_localPlayerId] = lobby.LobbyId;

            // Add player to lobby chat channel
            var chatChannel = _lobbyChatChannels.GetValueOrDefault(lobby.LobbyId);
            if (chatChannel != null)
            {
                chatChannel.AddParticipant(_localPlayerId);
            }

            ConsoleSystem.Log($"Player {_localPlayerId} joined lobby '{lobby.DisplayName}'", ConsoleChannel.Game);

            GameEvent.DispatchGlobal(new LobbyJoinedEvent(lobby.LobbyId, _localPlayerId));
            GameEvent.DispatchGlobal(new LobbyPlayerJoinedEvent(lobby.LobbyId, player));
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to join lobby: {ex.Message}", ConsoleChannel.Game);
        }
    }

    public void OnGameEvent(LeaveLobbyRequestEvent eventArgs)
    {
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            ConsoleSystem.LogErr("Cannot leave lobby: local player ID not set", ConsoleChannel.Game);
            return;
        }

        try
        {
            var lobby = GetPlayerLobby(_localPlayerId);
            if (lobby == null)
            {
                ConsoleSystem.Log($"Player {_localPlayerId} is not in any lobby", ConsoleChannel.Game);
                return;
            }

            var playerId = _localPlayerId;
            lobby.RemovePlayer(playerId);
            _playerLobbyMap.Remove(playerId);

            // Remove player from lobby chat channel
            var chatChannel = _lobbyChatChannels.GetValueOrDefault(lobby.LobbyId);
            if (chatChannel != null)
            {
                chatChannel.RemoveParticipant(playerId);
            }

            ConsoleSystem.Log($"Player {playerId} left lobby '{lobby.DisplayName}'", ConsoleChannel.Game);

            GameEvent.DispatchGlobal(new LobbyLeftEvent(lobby.LobbyId, playerId));
            GameEvent.DispatchGlobal(new LobbyPlayerLeftEvent(lobby.LobbyId, playerId));

            // Disband lobby if empty or if leader left and no other players
            if (lobby.Players.Count == 0 || (lobby.LeaderPlayerId == null && lobby.Players.Count == 0))
            {
                _lobbies.Remove(lobby.LobbyId);
                _lobbyChatChannels.Remove(lobby.LobbyId);
                ConsoleSystem.Log($"Lobby '{lobby.DisplayName}' disbanded (empty)", ConsoleChannel.Game);
                GameEvent.DispatchGlobal(new LobbyEndedEvent(lobby.LobbyId));
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to leave lobby: {ex.Message}", ConsoleChannel.Game);
        }
    }

    public void OnGameEvent(ChangeLobbyLeaderRequestEvent eventArgs)
    {
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            ConsoleSystem.LogErr("Cannot change leader: local player ID not set", ConsoleChannel.Game);
            return;
        }

        try
        {
            var lobby = GetPlayerLobby(_localPlayerId);
            if (lobby == null)
            {
                ConsoleSystem.LogErr("Cannot change leader: not in a lobby", ConsoleChannel.Game);
                return;
            }

            if (lobby.LeaderPlayerId != _localPlayerId)
            {
                ConsoleSystem.LogErr("Cannot change leader: only the current leader can change leadership", ConsoleChannel.Game);
                return;
            }

            if (!lobby.ChangeLeader(eventArgs.NewLeaderPlayerId))
            {
                ConsoleSystem.LogErr($"Failed to change leader to {eventArgs.NewLeaderPlayerId}", ConsoleChannel.Game);
                return;
            }

            ConsoleSystem.Log($"Changed lobby leader to {eventArgs.NewLeaderPlayerId}", ConsoleChannel.Game);
            GameEvent.DispatchGlobal(new LobbyLeaderChangedEvent(lobby.LobbyId, eventArgs.NewLeaderPlayerId));
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to change lobby leader: {ex.Message}", ConsoleChannel.Game);
        }
    }

    public void OnGameEvent(UpdateLobbySettingsRequestEvent eventArgs)
    {
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            ConsoleSystem.LogErr("Cannot update settings: local player ID not set", ConsoleChannel.Game);
            return;
        }

        try
        {
            var lobby = GetPlayerLobby(_localPlayerId);
            if (lobby == null)
            {
                ConsoleSystem.LogErr("Cannot update settings: not in a lobby", ConsoleChannel.Game);
                return;
            }

            if (!lobby.UpdateSettings(eventArgs.NewSettings, _localPlayerId))
            {
                ConsoleSystem.LogErr("Cannot update settings: only the leader can change lobby settings", ConsoleChannel.Game);
                return;
            }

            ConsoleSystem.Log($"Updated lobby settings for '{lobby.DisplayName}'", ConsoleChannel.Game);
            GameEvent.DispatchGlobal(new LobbySettingsChangedEvent(lobby.LobbyId, eventArgs.NewSettings));
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to update lobby settings: {ex.Message}", ConsoleChannel.Game);
        }
    }

    public void OnGameEvent(StartGameRequestEvent eventArgs)
    {
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            ConsoleSystem.LogErr("Cannot start game: local player ID not set", ConsoleChannel.Game);
            return;
        }

        try
        {
            var lobby = GetPlayerLobby(_localPlayerId);
            if (lobby == null)
            {
                ConsoleSystem.LogErr("Cannot start game: not in a lobby", ConsoleChannel.Game);
                return;
            }

			// Validate leader and state explicitly for clearer feedback and to avoid duplicate attempts
			if (lobby.LeaderPlayerId != _localPlayerId)
			{
				ConsoleSystem.LogErr("Cannot start game: only the leader can start the game", ConsoleChannel.Game);
				return;
			}

			if (lobby.Status != LobbyStatus.Waiting)
			{
				ConsoleSystem.LogWarn($"Cannot start game: lobby status is {lobby.Status}", ConsoleChannel.Game);
				return;
			}

			if (!lobby.StartGame(_localPlayerId))
			{
				ConsoleSystem.LogErr("Cannot start game: failed to transition lobby state", ConsoleChannel.Game);
				return;
			}

            ConsoleSystem.Log($"Starting game for lobby '{lobby.DisplayName}'", ConsoleChannel.Game);
            
            // Update lobby status
            lobby.Status = LobbyStatus.InGame;
            
            GameEvent.DispatchGlobal(new LobbyStartedEvent(lobby.LobbyId));

			// Trigger actual game start and scene transition using the lobby's selected map
			var levelPath = lobby.Settings?.MapPath ?? "res://scenes/dev/dev.tscn";
			ConsoleSystem.Log($"[LobbyService] Transitioning all players to scene: {levelPath}", ConsoleChannel.Game);
			GameEvent.DispatchGlobal(new NewGameStartedEvent(levelPath));
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to start game: {ex.Message}", ConsoleChannel.Game);
        }
    }

    public void OnGameEvent(SetPlayerReadyRequestEvent eventArgs)
    {
        if (string.IsNullOrEmpty(_localPlayerId))
        {
            ConsoleSystem.LogErr("Cannot set ready status: local player ID not set", ConsoleChannel.Game);
            return;
        }

        try
        {
            var lobby = GetPlayerLobby(_localPlayerId);
            if (lobby == null)
            {
                ConsoleSystem.LogErr("Cannot set ready status: not in a lobby", ConsoleChannel.Game);
                return;
            }

            var player = lobby.GetPlayer(_localPlayerId);
            if (player == null)
            {
                ConsoleSystem.LogErr("Cannot set ready status: player not found in lobby", ConsoleChannel.Game);
                return;
            }

            var oldReady = player.IsReady;
            player.IsReady = eventArgs.IsReady;

            if (oldReady != eventArgs.IsReady)
            {
                ConsoleSystem.Log($"Player {_localPlayerId} is {(eventArgs.IsReady ? "ready" : "not ready")}", ConsoleChannel.Game);
                GameEvent.DispatchGlobal(new LobbyPlayerReadyChangedEvent(lobby.LobbyId, _localPlayerId, eventArgs.IsReady));
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to set ready status: {ex.Message}", ConsoleChannel.Game);
        }
    }

    private void RegisterConsoleCommands()
    {
        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "lobby_create",
            "Create a new lobby",
            "lobby_create [name]",
            async (args) =>
            {
                var displayName = args.Length > 0 ? string.Join(" ", args) : "My Lobby";
                GameEvent.DispatchGlobal(new CreateLobbyRequestEvent(displayName));
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "lobby_join",
            "Join a lobby by ID",
            "lobby_join <lobbyId>",
            async (args) =>
            {
                if (args.Length == 0)
                {
                    ConsoleSystem.Log("Usage: lobby_join <lobbyId>", ConsoleChannel.Game);
                    return false;
                }
                GameEvent.DispatchGlobal(new JoinLobbyRequestEvent(args[0]));
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "lobby_leave",
            "Leave current lobby",
            "lobby_leave",
            async (args) =>
            {
                GameEvent.DispatchGlobal(new LeaveLobbyRequestEvent());
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "lobby_leader",
            "Change lobby leader",
            "lobby_leader <playerId>",
            async (args) =>
            {
                if (args.Length == 0)
                {
                    ConsoleSystem.Log("Usage: lobby_leader <playerId>", ConsoleChannel.Game);
                    return false;
                }
                GameEvent.DispatchGlobal(new ChangeLobbyLeaderRequestEvent(args[0]));
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "lobby_ready",
            "Set ready status",
            "lobby_ready [true|false]",
            async (args) =>
            {
                bool isReady = args.Length == 0 || args[0].ToLower() != "false";
                GameEvent.DispatchGlobal(new SetPlayerReadyRequestEvent(isReady));
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "lobby_start",
            "Start the game",
            "lobby_start",
            async (args) =>
            {
                GameEvent.DispatchGlobal(new StartGameRequestEvent());
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "lobby_list",
            "List all active lobbies",
            "lobby_list",
            async (args) =>
            {
                var lobbies = GetAllLobbies();
                if (lobbies.Count == 0)
                {
                    ConsoleSystem.Log("No active lobbies", ConsoleChannel.Game);
                    return true;
                }

                foreach (var lobby in lobbies)
                {
                    ConsoleSystem.Log($"Lobby: {lobby.DisplayName} (Players: {lobby.Players.Count}/{lobby.MaxPlayers}, Status: {lobby.Status})", ConsoleChannel.Game);
                }
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "lobby_info",
            "Show current lobby info",
            "lobby_info",
            async (args) =>
            {
                var lobby = GetCurrentPlayerLobby();
                if (lobby == null)
                {
                    ConsoleSystem.Log("Not in a lobby", ConsoleChannel.Game);
                    return true;
                }

                ConsoleSystem.Log($"Lobby: {lobby.DisplayName}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Leader: {lobby.GetLeader()?.DisplayName ?? "Unknown"}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Players: {lobby.Players.Count}/{lobby.MaxPlayers}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Status: {lobby.Status}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Map: {lobby.Settings.MapPath}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Game Mode: {lobby.Settings.GameMode}", ConsoleChannel.Game);

                foreach (var player in lobby.Players)
                {
                    ConsoleSystem.Log($"  - {player.DisplayName} {(player.IsLeader ? "(Leader)" : "")} {(player.IsReady ? "(Ready)" : "")}", ConsoleChannel.Game);
                }
                return true;
            }));

        // Set current lobby map path: lobby_set_map <res://path/to/scene.tscn>
        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "lobby_set_map",
            "Set current lobby map path",
            "lobby_set_map <scenePath>",
            async (args) =>
            {
                if (args.Length == 0)
                {
                    ConsoleSystem.Log("Usage: lobby_set_map <scenePath>", ConsoleChannel.Game);
                    return false;
                }

                var current = GetCurrentPlayerLobby();
                if (current == null)
                {
                    ConsoleSystem.Log("Not in a lobby", ConsoleChannel.Game);
                    return false;
                }

                var settings = current.Settings?.Clone() ?? new LobbySettings();
                settings.MapPath = string.Join(" ", args);
                GameEvent.DispatchGlobal(new UpdateLobbySettingsRequestEvent(settings));
                return true;
            }));
    }
}
