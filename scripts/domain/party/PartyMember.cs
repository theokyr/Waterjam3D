using System;

namespace Waterjam.Domain.Party;

/// <summary>
/// Represents a member in a party.
/// </summary>
public class PartyMember : IEquatable<PartyMember>
{
    /// <summary>
    /// Unique identifier for the player.
    /// </summary>
    public string PlayerId { get; }

    /// <summary>
    /// Display name for the player.
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    /// Whether this player is the lobby leader.
    /// </summary>
    public bool IsLeader { get; set; }

    /// <summary>
    /// Whether this player is ready to start the game.
    /// </summary>
    public bool IsReady { get; set; }

    /// <summary>
    /// When this player joined the lobby.
    /// </summary>
    public DateTime JoinedAt { get; }

    /// <summary>
    /// Player's selected character/class.
    /// </summary>
    public string SelectedCharacter { get; set; }

    /// <summary>
    /// Player's connection status.
    /// </summary>
    public PlayerConnectionStatus ConnectionStatus { get; set; } = PlayerConnectionStatus.Connected;

    /// <summary>
    /// Additional metadata about the player.
    /// </summary>
    public string Metadata { get; set; }

    /// <summary>
    /// Creates a new lobby player.
    /// </summary>
    public PartyMember(string playerId, bool isLeader = false, string displayName = null)
    {
        PlayerId = playerId ?? throw new ArgumentNullException(nameof(playerId));
        DisplayName = displayName ?? $"Player {playerId}";
        IsLeader = isLeader;
        IsReady = false;
        JoinedAt = DateTime.UtcNow;
    }

    public bool Equals(PartyMember other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return PlayerId == other.PlayerId;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((PartyMember)obj);
    }

    public override int GetHashCode()
    {
        return PlayerId.GetHashCode();
    }

    public override string ToString()
    {
        return $"{DisplayName} ({PlayerId})";
    }
}

/// <summary>
/// Connection status of a player in the lobby.
/// </summary>
public enum PlayerConnectionStatus
{
    /// <summary>
    /// Player is connected and active.
    /// </summary>
    Connected,

    /// <summary>
    /// Player is away/inactive.
    /// </summary>
    Away,

    /// <summary>
    /// Player has disconnected.
    /// </summary>
    Disconnected
}
