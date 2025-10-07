using System;

namespace Waterjam.Domain.Party;

/// <summary>
/// Represents an invitation to join a party.
/// </summary>
public class PartyInvite : IEquatable<PartyInvite>
{
    /// <summary>
    /// Unique identifier for this invitation.
    /// </summary>
    public string InviteId { get; }

    /// <summary>
    /// ID of the party being invited to.
    /// </summary>
    public string PartyId { get; }

    /// <summary>
    /// ID of the player who sent the invitation.
    /// </summary>
    public string FromPlayerId { get; }

    /// <summary>
    /// ID of the player being invited.
    /// </summary>
    public string ToPlayerId { get; }

    /// <summary>
    /// Display name of the party.
    /// </summary>
    public string PartyDisplayName { get; }

    /// <summary>
    /// Display name of the player who sent the invitation.
    /// </summary>
    public string FromPlayerDisplayName { get; }

    /// <summary>
    /// When this invitation was created.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// When this invitation expires.
    /// </summary>
    public DateTime ExpiresAt { get; }

    /// <summary>
    /// Whether this invitation has been used.
    /// </summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// Custom message included with the invitation.
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// Creates a new party invitation.
    /// </summary>
    public PartyInvite(
        string inviteId,
        string partyId,
        string fromPlayerId,
        string toPlayerId,
        string partyDisplayName,
        string fromPlayerDisplayName,
        string message = null,
        TimeSpan? validFor = null)
    {
        InviteId = inviteId ?? throw new ArgumentNullException(nameof(inviteId));
        PartyId = partyId ?? throw new ArgumentNullException(nameof(partyId));
        FromPlayerId = fromPlayerId ?? throw new ArgumentNullException(nameof(fromPlayerId));
        ToPlayerId = toPlayerId ?? throw new ArgumentNullException(nameof(toPlayerId));
        PartyDisplayName = partyDisplayName ?? throw new ArgumentNullException(nameof(partyDisplayName));
        FromPlayerDisplayName = fromPlayerDisplayName ?? throw new ArgumentNullException(nameof(fromPlayerDisplayName));

        CreatedAt = DateTime.UtcNow;
        ExpiresAt = CreatedAt + (validFor ?? TimeSpan.FromMinutes(5));
        Message = message;
        IsUsed = false;
    }

    /// <summary>
    /// Checks if this invitation is still valid (not expired and not used).
    /// </summary>
    public bool IsValid()
    {
        return !IsUsed && DateTime.UtcNow < ExpiresAt;
    }

    /// <summary>
    /// Marks this invitation as used.
    /// </summary>
    public void MarkAsUsed()
    {
        IsUsed = true;
    }

    public bool Equals(PartyInvite other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return InviteId == other.InviteId;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((PartyInvite)obj);
    }

    public override int GetHashCode()
    {
        return InviteId.GetHashCode();
    }
}
