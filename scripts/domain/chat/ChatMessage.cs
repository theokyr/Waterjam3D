using System;

namespace Waterjam.Domain.Chat;

/// <summary>
/// Represents a chat message in a party or lobby.
/// </summary>
public class ChatMessage : IEquatable<ChatMessage>
{
    /// <summary>
    /// Unique identifier for this message.
    /// </summary>
    public string MessageId { get; }

    /// <summary>
    /// ID of the player who sent this message.
    /// </summary>
    public string SenderPlayerId { get; }

    /// <summary>
    /// Display name of the player who sent this message.
    /// </summary>
    public string SenderDisplayName { get; }

    /// <summary>
    /// The actual message content.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// When this message was sent.
    /// </summary>
    public DateTime SentAt { get; }

    /// <summary>
    /// Type of chat message (party, lobby, system, etc.).
    /// </summary>
    public ChatMessageType MessageType { get; }

    /// <summary>
    /// ID of the party or lobby this message belongs to (if applicable).
    /// </summary>
    public string ChannelId { get; }

    /// <summary>
    /// Whether this is a system message (not from a player).
    /// </summary>
    public bool IsSystemMessage { get; }

    /// <summary>
    /// Creates a new chat message from a player.
    /// </summary>
    public ChatMessage(string senderPlayerId, string senderDisplayName, string content, ChatMessageType messageType, string channelId = null)
    {
        MessageId = Guid.NewGuid().ToString();
        SenderPlayerId = senderPlayerId ?? throw new ArgumentNullException(nameof(senderPlayerId));
        SenderDisplayName = senderDisplayName ?? throw new ArgumentNullException(nameof(senderDisplayName));
        Content = content ?? throw new ArgumentNullException(nameof(content));
        SentAt = DateTime.UtcNow;
        MessageType = messageType;
        ChannelId = channelId;
        IsSystemMessage = false;
    }

    /// <summary>
    /// Creates a new system chat message.
    /// </summary>
    public ChatMessage(string content, ChatMessageType messageType, string channelId = null)
    {
        MessageId = Guid.NewGuid().ToString();
        SenderPlayerId = "system";
        SenderDisplayName = "System";
        Content = content ?? throw new ArgumentNullException(nameof(content));
        SentAt = DateTime.UtcNow;
        MessageType = messageType;
        ChannelId = channelId;
        IsSystemMessage = true;
    }

    public bool Equals(ChatMessage other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return MessageId == other.MessageId;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((ChatMessage)obj);
    }

    public override int GetHashCode()
    {
        return MessageId.GetHashCode();
    }

    public override string ToString()
    {
        return $"{SenderDisplayName}: {Content}";
    }
}

/// <summary>
/// Types of chat messages.
/// </summary>
public enum ChatMessageType
{
    /// <summary>
    /// Regular chat message.
    /// </summary>
    Chat,

    /// <summary>
    /// System announcement or notification.
    /// </summary>
    System,

    /// <summary>
    /// Player joined/left notification.
    /// </summary>
    JoinLeave,

    /// <summary>
    /// Game-related message (round start, etc.).
    /// </summary>
    GameEvent
}
