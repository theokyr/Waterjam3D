using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotSteam;
using Waterjam.Core.Services;
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

    private void SetupSteamCallbacks()
    {
        // Listen for Steam lobby events that affect party management
        Steam.LobbyChatUpdate += OnSteamLobbyChatUpdate;
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

        return message;
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

            // Note: Parties are social groups only. Steam lobbies are created when starting a game session.

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

                // Note: Parties are social groups only. Steam lobbies are created when starting a game session.

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
