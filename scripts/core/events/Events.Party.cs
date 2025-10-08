using Waterjam.Domain.Party;
using Waterjam.Domain.Chat;
using Waterjam.Game.Services.Voice;

namespace Waterjam.Events;

// Party Events
public record PartyCreatedEvent(string PartyId, string PartyCode, string LeaderPlayerId, string DisplayName) : IGameEvent;

public record PartyJoinedEvent(string PartyId, string PlayerId) : IGameEvent;

public record PartyLeftEvent(string PartyId, string PlayerId) : IGameEvent;

public record PartyMemberJoinedEvent(string PartyId, PartyMember Member) : IGameEvent;

public record PartyMemberLeftEvent(string PartyId, string PlayerId) : IGameEvent;

public record PartyLeaderChangedEvent(string PartyId, string NewLeaderPlayerId) : IGameEvent;

public record PartyDisbandedEvent(string PartyId) : IGameEvent;

public record PartyInviteSentEvent(string InviteId, string FromPlayerId, string ToPlayerId) : IGameEvent;

public record PartyInviteReceivedEvent(PartyInvite Invite) : IGameEvent;

public record PartyInviteAcceptedEvent(string InviteId, string PartyId, string PlayerId) : IGameEvent;

public record PartyInviteDeclinedEvent(string InviteId, string PlayerId) : IGameEvent;

public record PartyInviteExpiredEvent(string InviteId) : IGameEvent;

// Chat Events
public record PartyChatMessageEvent(string PartyId, ChatMessage Message) : IGameEvent;
public record LobbyChatMessageEvent(string LobbyId, ChatMessage Message) : IGameEvent;

// Voice Chat Events
public record VoicePlayerStartedTalkingEvent(string PlayerId) : IGameEvent;
public record VoicePlayerStoppedTalkingEvent(string PlayerId) : IGameEvent;
public record VoicePlayerMutedEvent(string PlayerId, bool IsMuted) : IGameEvent;
public record VoiceProximityConnectedEvent(string PlayerId1, string PlayerId2) : IGameEvent;
public record VoiceProximityDisconnectedEvent(string PlayerId1, string PlayerId2) : IGameEvent;
public record VoiceSettingsChangedEvent(VoiceSettings OldSettings, VoiceSettings NewSettings) : IGameEvent;

// Lobby Events
public record LobbyCreatedEvent(string LobbyId, string LeaderPlayerId, string DisplayName) : IGameEvent;

public record LobbyInviteReceivedEvent(ulong UserId, ulong LobbyId) : IGameEvent;

public record LobbyJoinedEvent(string LobbyId, string PlayerId) : IGameEvent;

public record LobbyLeftEvent(string LobbyId, string PlayerId) : IGameEvent;

public record PartyPlayerJoinedEvent(string PartyId, PartyMember Player) : IGameEvent;

public record PartyPlayerLeftEvent(string PartyId, string PlayerId) : IGameEvent;

public record PartySettingsChangedEvent(string PartyId, PartySettings NewSettings) : IGameEvent;

public record PartyStartedEvent(string PartyId) : IGameEvent;

public record PartyEndedEvent(string PartyId) : IGameEvent;


public record PartyPlayerReadyChangedEvent(string PartyId, string PlayerId, bool IsReady) : IGameEvent;

// Progression Events
public record ProgressionLoadedEvent(string PlayerId) : IGameEvent;
public record CurrencyChangedEvent(string PlayerId, int NewAmount, int ChangeAmount) : IGameEvent;
public record ItemUnlockedEvent(string PlayerId, string ItemId) : IGameEvent;
public record AchievementCompletedEvent(string PlayerId, string AchievementId) : IGameEvent;
public record StatisticChangedEvent(string PlayerId, string StatName, int OldValue, int NewValue) : IGameEvent;

// Requests/Commands
public record CreatePartyRequestEvent(string DisplayName, int MaxMembers = 8) : IGameEvent;

public record JoinPartyRequestEvent(string PartyCode) : IGameEvent;

public record LeavePartyRequestEvent() : IGameEvent;

public record InviteToPartyRequestEvent(string PlayerId, string Message = null) : IGameEvent;

public record RespondToPartyInviteRequestEvent(string InviteId, bool Accept) : IGameEvent;

public record CreateLobbyRequestEvent(string DisplayName, PartySettings Settings = null) : IGameEvent;

public record JoinLobbyRequestEvent(string LobbyId) : IGameEvent;

public record ChangePartyLeaderRequestEvent(string NewLeaderPlayerId) : IGameEvent;

public record UpdatePartySettingsRequestEvent(PartySettings NewSettings) : IGameEvent;

public record StartGameRequestEvent() : IGameEvent;

public record SetPlayerReadyRequestEvent(bool IsReady) : IGameEvent;
