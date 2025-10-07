using System;
using System.Collections.Generic;
using System.Linq;

namespace Waterjam.Domain.Chat;

/// <summary>
/// Represents a chat channel (party chat, lobby chat, etc.).
/// </summary>
public class ChatChannel : IEquatable<ChatChannel>
{
    /// <summary>
    /// Unique identifier for this chat channel.
    /// </summary>
    public string ChannelId { get; }

    /// <summary>
    /// Display name for this channel.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Type of this chat channel.
    /// </summary>
    public ChatChannelType ChannelType { get; }

    /// <summary>
    /// Maximum number of messages to keep in history.
    /// </summary>
    public int MaxMessageHistory { get; set; } = 100;

    /// <summary>
    /// List of participants in this channel.
    /// </summary>
    public IReadOnlyList<string> Participants => _participants.ToList();
    private readonly HashSet<string> _participants = new();

    /// <summary>
    /// Chat message history (newest first).
    /// </summary>
    public IReadOnlyList<ChatMessage> MessageHistory => _messageHistory;
    private readonly List<ChatMessage> _messageHistory = new();

    /// <summary>
    /// When this channel was created.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Whether this channel is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Creates a new chat channel.
    /// </summary>
    public ChatChannel(string channelId, string displayName, ChatChannelType channelType)
    {
        ChannelId = channelId ?? throw new ArgumentNullException(nameof(channelId));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        ChannelType = channelType;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds a participant to this channel.
    /// </summary>
    public void AddParticipant(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
            throw new ArgumentNullException(nameof(playerId));

        _participants.Add(playerId);

        // Send system message about player joining
        var joinMessage = new ChatMessage($"Player {playerId} joined the chat", ChatMessageType.JoinLeave, ChannelId);
        AddMessage(joinMessage);
    }

    /// <summary>
    /// Removes a participant from this channel.
    /// </summary>
    public void RemoveParticipant(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
            throw new ArgumentNullException(nameof(playerId));

        _participants.Remove(playerId);

        // Send system message about player leaving
        var leaveMessage = new ChatMessage($"Player {playerId} left the chat", ChatMessageType.JoinLeave, ChannelId);
        AddMessage(leaveMessage);
    }

    /// <summary>
    /// Adds a message to this channel's history.
    /// </summary>
    public void AddMessage(ChatMessage message)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        _messageHistory.Insert(0, message);

        // Trim history if it exceeds maximum
        if (_messageHistory.Count > MaxMessageHistory)
        {
            _messageHistory.RemoveRange(MaxMessageHistory, _messageHistory.Count - MaxMessageHistory);
        }
    }

    /// <summary>
    /// Sends a chat message from a player.
    /// </summary>
    public ChatMessage SendMessage(string senderPlayerId, string senderDisplayName, string content)
    {
        if (string.IsNullOrEmpty(senderPlayerId))
            throw new ArgumentNullException(nameof(senderPlayerId));

        if (string.IsNullOrEmpty(senderDisplayName))
            throw new ArgumentNullException(nameof(senderDisplayName));

        if (string.IsNullOrEmpty(content))
            throw new ArgumentNullException(nameof(content));

        var message = new ChatMessage(senderPlayerId, senderDisplayName, content, ChatMessageType.Chat, ChannelId);
        AddMessage(message);

        return message;
    }

    /// <summary>
    /// Sends a system message to this channel.
    /// </summary>
    public ChatMessage SendSystemMessage(string content)
    {
        if (string.IsNullOrEmpty(content))
            throw new ArgumentNullException(nameof(content));

        var message = new ChatMessage(content, ChatMessageType.System, ChannelId);
        AddMessage(message);

        return message;
    }

    /// <summary>
    /// Checks if a player is a participant in this channel.
    /// </summary>
    public bool IsParticipant(string playerId)
    {
        return _participants.Contains(playerId);
    }

    /// <summary>
    /// Gets the most recent messages (up to the specified count).
    /// </summary>
    public IReadOnlyList<ChatMessage> GetRecentMessages(int count = 50)
    {
        return _messageHistory.Take(count).ToList();
    }

    /// <summary>
    /// Clears all messages from this channel.
    /// </summary>
    public void ClearMessages()
    {
        _messageHistory.Clear();
    }

    public bool Equals(ChatChannel other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return ChannelId == other.ChannelId;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((ChatChannel)obj);
    }

    public override int GetHashCode()
    {
        return ChannelId.GetHashCode();
    }
}

/// <summary>
/// Types of chat channels.
/// </summary>
public enum ChatChannelType
{
    /// <summary>
    /// Party chat channel.
    /// </summary>
    Party,

    /// <summary>
    /// Lobby chat channel.
    /// </summary>
    Lobby,

    /// <summary>
    /// Global chat channel.
    /// </summary>
    Global,

    /// <summary>
    /// Private message channel.
    /// </summary>
    Private
}
