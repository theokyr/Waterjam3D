using System;
using System.Collections.Generic;
using System.Linq;

namespace Waterjam.Domain.Lobby;

/// <summary>
/// Represents a game lobby where players gather before starting a game session.
/// </summary>
public class Lobby : IEquatable<Lobby>
{
    /// <summary>
    /// Unique identifier for this lobby.
    /// </summary>
    public string LobbyId { get; }

    /// <summary>
    /// Display name for the lobby.
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    /// The player ID of the lobby leader/owner.
    /// </summary>
    public string LeaderPlayerId { get; private set; }

    /// <summary>
    /// List of players in the lobby.
    /// </summary>
    public IReadOnlyList<LobbyPlayer> Players => _players;
    private readonly List<LobbyPlayer> _players = new();

    /// <summary>
    /// Current lobby settings.
    /// </summary>
    public LobbySettings Settings { get; set; }

    /// <summary>
    /// Maximum number of players allowed in this lobby.
    /// </summary>
    public int MaxPlayers { get; set; } = 8;

    /// <summary>
    /// Whether the lobby is currently locked (no one can join).
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// Whether the lobby is private (requires invite or code to join).
    /// </summary>
    public bool IsPrivate { get; set; }

    /// <summary>
    /// Join code for private lobbies.
    /// </summary>
    public string JoinCode { get; set; }

    /// <summary>
    /// Current status of the lobby.
    /// </summary>
    public LobbyStatus Status { get; set; } = LobbyStatus.Waiting;

    /// <summary>
    /// When this lobby was created.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// When this lobby was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a new lobby.
    /// </summary>
    public Lobby(string lobbyId, string leaderPlayerId, string displayName = null, LobbySettings settings = null)
    {
        LobbyId = lobbyId ?? throw new ArgumentNullException(nameof(lobbyId));
        LeaderPlayerId = leaderPlayerId ?? throw new ArgumentNullException(nameof(leaderPlayerId));
        DisplayName = displayName ?? $"Lobby {lobbyId}";
        Settings = settings ?? new LobbySettings();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;

        // Add the leader as the first player
        AddPlayer(new LobbyPlayer(leaderPlayerId, isLeader: true));
    }

    /// <summary>
    /// Adds a player to the lobby.
    /// </summary>
    public void AddPlayer(LobbyPlayer player)
    {
        if (_players.Count >= MaxPlayers)
            throw new InvalidOperationException($"Lobby is already at maximum capacity ({MaxPlayers} players)");

        if (_players.Any(p => p.PlayerId == player.PlayerId))
            throw new InvalidOperationException($"Player {player.PlayerId} is already in the lobby");

        if (IsLocked && !player.IsLeader)
            throw new InvalidOperationException("Lobby is locked and cannot accept new players");

        _players.Add(player);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Removes a player from the lobby.
    /// </summary>
    public bool RemovePlayer(string playerId)
    {
        var player = _players.FirstOrDefault(p => p.PlayerId == playerId);
        if (player == null)
            return false;

        _players.Remove(player);

        // If the leader left, promote the next player to leader
        if (player.IsLeader && _players.Count > 0)
        {
            _players[0].IsLeader = true;
            LeaderPlayerId = _players[0].PlayerId;
        }
        else if (player.IsLeader && _players.Count == 0)
        {
            // Lobby is empty, this should be handled by the service
            LeaderPlayerId = null;
        }

        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Changes the lobby leader.
    /// </summary>
    public bool ChangeLeader(string newLeaderPlayerId)
    {
        var currentLeader = _players.FirstOrDefault(p => p.IsLeader);
        var newLeader = _players.FirstOrDefault(p => p.PlayerId == newLeaderPlayerId);

        if (currentLeader == null || newLeader == null)
            return false;

        currentLeader.IsLeader = false;
        newLeader.IsLeader = true;
        LeaderPlayerId = newLeaderPlayerId;
        UpdatedAt = DateTime.UtcNow;

        return true;
    }

    /// <summary>
    /// Gets a player by player ID.
    /// </summary>
    public LobbyPlayer GetPlayer(string playerId)
    {
        return _players.FirstOrDefault(p => p.PlayerId == playerId);
    }

    /// <summary>
    /// Checks if a player is in this lobby.
    /// </summary>
    public bool ContainsPlayer(string playerId)
    {
        return _players.Any(p => p.PlayerId == playerId);
    }

    /// <summary>
    /// Gets the lobby leader.
    /// </summary>
    public LobbyPlayer GetLeader()
    {
        return _players.FirstOrDefault(p => p.IsLeader);
    }

    /// <summary>
    /// Updates the lobby settings (only leader can do this).
    /// </summary>
    public bool UpdateSettings(LobbySettings newSettings, string requesterPlayerId)
    {
        if (LeaderPlayerId != requesterPlayerId)
            return false;

        Settings = newSettings ?? throw new ArgumentNullException(nameof(newSettings));
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Starts the game (only leader can do this).
    /// </summary>
    public bool StartGame(string requesterPlayerId)
    {
        if (LeaderPlayerId != requesterPlayerId)
            return false;

        if (Status != LobbyStatus.Waiting)
            return false;

        Status = LobbyStatus.Starting;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    public bool Equals(Lobby other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return LobbyId == other.LobbyId;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((Lobby)obj);
    }

    public override int GetHashCode()
    {
        return LobbyId.GetHashCode();
    }
}

/// <summary>
/// Current status of a lobby.
/// </summary>
public enum LobbyStatus
{
    /// <summary>
    /// Lobby is waiting for players and leader to start.
    /// </summary>
    Waiting,

    /// <summary>
    /// Game is starting/loading.
    /// </summary>
    Starting,

    /// <summary>
    /// Game is in progress.
    /// </summary>
    InGame,

    /// <summary>
    /// Game has ended.
    /// </summary>
    Finished
}
