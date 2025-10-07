using System;

namespace Waterjam.Domain.Party;

/// <summary>
/// Represents a member of a party.
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
    /// Whether this member is the party leader.
    /// </summary>
    public bool IsLeader { get; set; }

    /// <summary>
    /// Whether this member is ready (for lobby scenarios).
    /// </summary>
    public bool IsReady { get; set; }

    /// <summary>
    /// When this member joined the party.
    /// </summary>
    public DateTime JoinedAt { get; }

    /// <summary>
    /// Additional metadata about the player (level, class, etc.).
    /// </summary>
    public string Metadata { get; set; }

    /// <summary>
    /// Creates a new party member.
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
