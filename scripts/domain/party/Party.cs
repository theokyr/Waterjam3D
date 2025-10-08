using System;
using System.Collections.Generic;
using System.Linq;

namespace Waterjam.Domain.Party;

/// <summary>
/// Represents a party that players can join.
/// </summary>
public class Party : IEquatable<Party>
{
    /// <summary>
    /// Unique identifier for this party.
    /// </summary>
    public string PartyId { get; }

    /// <summary>
    /// Human-readable party code that players can use to join.
    /// </summary>
    public string PartyCode { get; }

    /// <summary>
    /// Display name for the party.
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    /// The player ID of the party leader.
    /// </summary>
    public string LeaderPlayerId { get; private set; }

    /// <summary>
    /// List of party members.
    /// </summary>
    public IReadOnlyList<PartyMember> Members => _members;
    private readonly List<PartyMember> _members = new();

    /// <summary>
    /// Maximum number of members allowed in this party.
    /// </summary>
    public int MaxMembers { get; set; } = 8;

    /// <summary>
    /// Whether this party is currently in a lobby.
    /// </summary>
    public bool IsInLobby { get; set; }

    /// <summary>
    /// When this party was created.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// When this party was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a new party.
    /// </summary>
    public Party(string partyId, string partyCode, string leaderPlayerId, string displayName = null)
    {
        PartyId = partyId ?? throw new ArgumentNullException(nameof(partyId));
        PartyCode = partyCode ?? throw new ArgumentNullException(nameof(partyCode));
        LeaderPlayerId = leaderPlayerId ?? throw new ArgumentNullException(nameof(leaderPlayerId));
        DisplayName = displayName ?? $"Party {partyCode}";
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;

        // Add the leader as the first member
        AddMember(new PartyMember(leaderPlayerId, isLeader: true));
    }

    /// <summary>
    /// Adds a member to the party.
    /// </summary>
    public void AddMember(PartyMember member)
    {
        if (_members.Count >= MaxMembers)
            throw new InvalidOperationException($"Party is already at maximum capacity ({MaxMembers} members)");

        if (_members.Any(m => m.PlayerId == member.PlayerId))
            throw new InvalidOperationException($"Player {member.PlayerId} is already in the party");

        _members.Add(member);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Removes a member from the party.
    /// </summary>
    public bool RemoveMember(string playerId)
    {
        var member = _members.FirstOrDefault(m => m.PlayerId == playerId);
        if (member == null)
            return false;

        _members.Remove(member);

        // If the leader left, promote the next member to leader
        if (member.IsLeader && _members.Count > 0)
        {
            _members[0].IsLeader = true;
            LeaderPlayerId = _members[0].PlayerId;
        }
        else if (member.IsLeader && _members.Count == 0)
        {
            // Party is empty, this should be handled by the service
            LeaderPlayerId = null;
        }

        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Changes the party leader.
    /// </summary>
    public bool ChangeLeader(string newLeaderPlayerId)
    {
        var currentLeader = _members.FirstOrDefault(m => m.IsLeader);
        var newLeader = _members.FirstOrDefault(m => m.PlayerId == newLeaderPlayerId);

        if (currentLeader == null || newLeader == null)
            return false;

        currentLeader.IsLeader = false;
        newLeader.IsLeader = true;
        LeaderPlayerId = newLeaderPlayerId;
        UpdatedAt = DateTime.UtcNow;

        return true;
    }

    /// <summary>
    /// Gets a member by player ID.
    /// </summary>
    public PartyMember GetMember(string playerId)
    {
        return _members.FirstOrDefault(m => m.PlayerId == playerId);
    }

    /// <summary>
    /// Checks if a player is in this party.
    /// </summary>
    public bool ContainsPlayer(string playerId)
    {
        return _members.Any(m => m.PlayerId == playerId);
    }

    /// <summary>
    /// Gets the party leader member.
    /// </summary>
    public PartyMember GetLeader()
    {
        return _members.FirstOrDefault(m => m.IsLeader);
    }

    public bool Equals(Party other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return PartyId == other.PartyId;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((Party)obj);
    }

    public override int GetHashCode()
    {
        return PartyId.GetHashCode();
    }
}
