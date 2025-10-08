using System;
using System.Collections.Generic;
using System.Linq;

namespace Waterjam.Domain.Lobby;

/// <summary>
/// Settings for a game lobby.
/// </summary>
public class LobbySettings : IEquatable<LobbySettings>
{
    /// <summary>
    /// The map/scene to play on.
    /// </summary>
    public string MapPath { get; set; } = "res://scenes/dev/dev.tscn";

    /// <summary>
    /// Game mode to play.
    /// </summary>
    public string GameMode { get; set; } = "Default";

    /// <summary>
    /// Maximum number of players for this game session.
    /// </summary>
    public int MaxPlayers { get; set; } = 8;

    /// <summary>
    /// Whether friendly fire is enabled.
    /// </summary>
    public bool FriendlyFire { get; set; } = false;

    /// <summary>
    /// Game difficulty level.
    /// </summary>
    public int Difficulty { get; set; } = 1;

    /// <summary>
    /// Time limit for the game in seconds (0 = no limit).
    /// </summary>
    public int TimeLimit { get; set; } = 0;

    /// <summary>
    /// Whether the game is private (requires password/invite).
    /// </summary>
    public bool IsPrivate { get; set; } = false;

    /// <summary>
    /// Password for private games (if applicable).
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    /// Custom game rules or modifiers.
    /// </summary>
    public Dictionary<string, object> CustomRules { get; set; } = new();

    /// <summary>
    /// Whether spectators are allowed.
    /// </summary>
    public bool AllowSpectators { get; set; } = true;

    /// <summary>
    /// Whether voice chat is enabled.
    /// </summary>
    public bool VoiceChatEnabled { get; set; } = true;

    /// <summary>
    /// Whether text chat is enabled.
    /// </summary>
    public bool TextChatEnabled { get; set; } = true;

    /// <summary>
    /// Creates a copy of these settings.
    /// </summary>
    public LobbySettings Clone()
    {
        return new LobbySettings
        {
            MapPath = MapPath,
            GameMode = GameMode,
            MaxPlayers = MaxPlayers,
            FriendlyFire = FriendlyFire,
            Difficulty = Difficulty,
            TimeLimit = TimeLimit,
            IsPrivate = IsPrivate,
            Password = Password,
            CustomRules = new Dictionary<string, object>(CustomRules),
            AllowSpectators = AllowSpectators,
            VoiceChatEnabled = VoiceChatEnabled,
            TextChatEnabled = TextChatEnabled
        };
    }

    public bool Equals(LobbySettings other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;

        return MapPath == other.MapPath &&
               GameMode == other.GameMode &&
               MaxPlayers == other.MaxPlayers &&
               FriendlyFire == other.FriendlyFire &&
               Difficulty == other.Difficulty &&
               TimeLimit == other.TimeLimit &&
               IsPrivate == other.IsPrivate &&
               Password == other.Password &&
               AllowSpectators == other.AllowSpectators &&
               VoiceChatEnabled == other.VoiceChatEnabled &&
               TextChatEnabled == other.TextChatEnabled &&
               CustomRules.Count == other.CustomRules.Count &&
               CustomRules.All(kvp => other.CustomRules.TryGetValue(kvp.Key, out var value) && Equals(kvp.Value, value));
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((LobbySettings)obj);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MapPath);
        hash.Add(GameMode);
        hash.Add(MaxPlayers);
        hash.Add(FriendlyFire);
        hash.Add(Difficulty);
        hash.Add(TimeLimit);
        hash.Add(IsPrivate);
        hash.Add(Password);
        hash.Add(AllowSpectators);
        hash.Add(VoiceChatEnabled);
        hash.Add(TextChatEnabled);

        foreach (var kvp in CustomRules.OrderBy(k => k.Key))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value);
        }

        return hash.ToHashCode();
    }
}
