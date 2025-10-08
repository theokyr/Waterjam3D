using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Waterjam.Domain.Chat;
using Waterjam.Domain.Party;
using Waterjam.Events;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Helper class for serializing and deserializing party state for network transmission.
/// </summary>
public static class PartyNetworkSerializer
{
    /// <summary>
    /// Builds a Godot dictionary representing a complete party state.
    /// </summary>
    public static Godot.Collections.Dictionary ToDictionary(Party party)
    {
        if (party == null)
            throw new ArgumentNullException(nameof(party));

        var data = new Godot.Collections.Dictionary();

        // Basic party info
        data["party_id"] = party.PartyId;
        data["party_code"] = party.PartyCode;
        data["display_name"] = party.DisplayName;
        data["leader_player_id"] = party.LeaderPlayerId;
        data["max_members"] = party.MaxMembers;
        data["is_in_lobby"] = party.IsInLobby;
        data["created_at"] = party.CreatedAt.ToString("O");
        data["updated_at"] = party.UpdatedAt.ToString("O");

        // Serialize members
        var membersArray = new Godot.Collections.Array();
        foreach (var member in party.Members)
        {
            var memberDict = SerializePartyMemberToDictionary(member);
            membersArray.Add(memberDict);
        }
        data["members"] = membersArray;

        return data;
    }

    /// <summary>
    /// Serializes a complete party state for network transmission.
    /// </summary>
    public static byte[] SerializePartyState(Party party)
    {
        if (party == null)
            throw new ArgumentNullException(nameof(party));

        try
        {
            var data = ToDictionary(party);

            // Convert to JSON bytes
            var jsonString = Json.Stringify(data);
            return jsonString.ToUtf8Buffer();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to serialize party state: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// Deserializes party state from network data.
    /// </summary>
    public static Party DeserializePartyState(byte[] data)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentNullException(nameof(data));

        try
        {
            var jsonString = data.GetStringFromUtf8();
            var json = new Json();
            var parseResult = json.Parse(jsonString);

            if (parseResult != Error.Ok)
            {
                GD.PrintErr($"Failed to parse party state JSON: {parseResult}");
                return null;
            }

            var dataDict = json.Data.AsGodotDictionary();
            return FromDictionary(dataDict);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to deserialize party state: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reconstructs a Party from a Godot dictionary representation.
    /// </summary>
    public static Party FromDictionary(Godot.Collections.Dictionary dataDict)
    {
        // Extract basic party info
        var partyId = dataDict.ContainsKey("party_id") ? dataDict["party_id"].AsString() : null;
        var partyCode = dataDict.ContainsKey("party_code") ? dataDict["party_code"].AsString() : null;
        var displayName = dataDict.ContainsKey("display_name") ? dataDict["display_name"].AsString() : null;
        var leaderPlayerId = dataDict.ContainsKey("leader_player_id") ? dataDict["leader_player_id"].AsString() : null;
        var maxMembers = dataDict.ContainsKey("max_members") ? dataDict["max_members"].AsInt32() : 8;
        var isInLobby = dataDict.ContainsKey("is_in_lobby") && dataDict["is_in_lobby"].AsBool();

        // Create party (leader is added automatically)
        var party = new Party(partyId, partyCode, leaderPlayerId, displayName)
        {
            MaxMembers = maxMembers,
            IsInLobby = isInLobby
        };

        // Add/merge members
        if (dataDict.ContainsKey("members"))
        {
            var membersArray = dataDict["members"].AsGodotArray();
            foreach (var memberData in membersArray)
            {
                var member = DeserializePartyMember(memberData.AsGodotDictionary());
                if (member == null) continue;

                if (member.PlayerId == leaderPlayerId)
                {
                    // Update leader info from payload
                    var leader = party.GetMember(leaderPlayerId);
                    if (leader != null)
                    {
                        leader.DisplayName = member.DisplayName;
                        leader.IsReady = member.IsReady;
                        leader.SelectedCharacter = member.SelectedCharacter;
                        leader.ConnectionStatus = member.ConnectionStatus;
                        leader.Metadata = member.Metadata;
                    }
                }
                else if (!party.ContainsPlayer(member.PlayerId))
                {
                    try
                    {
                        party.AddMember(member);
                    }
                    catch
                    {
                        // Ignore validation failures on client-side reconstruction
                    }
                }
            }
        }

        return party;
    }

    /// <summary>
    /// Serializes a party member for network transmission.
    /// </summary>
    public static byte[] SerializePartyMember(PartyMember player)
    {
        if (player == null)
            throw new ArgumentNullException(nameof(player));

        try
        {
            var data = SerializePartyMemberToDictionary(player);

            var jsonString = Json.Stringify(data);
            return jsonString.ToUtf8Buffer();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to serialize party member: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// Deserializes a party member from network data.
    /// </summary>
    public static PartyMember DeserializePartyMember(byte[] data)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentNullException(nameof(data));

        try
        {
            var jsonString = data.GetStringFromUtf8();
            var json = new Json();
            var parseResult = json.Parse(jsonString);

            if (parseResult != Error.Ok)
            {
                GD.PrintErr($"Failed to parse party member JSON: {parseResult}");
                return null;
            }

            return DeserializePartyMember(json.Data.AsGodotDictionary());
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to deserialize party member: {ex.Message}");
            return null;
        }
    }

    private static PartyMember DeserializePartyMember(Godot.Collections.Dictionary data)
    {
        try
        {
            var playerId = data["player_id"].AsString();
            var displayName = data["display_name"].AsString();
            var isLeader = data["is_leader"].AsBool();
            var isReady = data["is_ready"].AsBool();
            var selectedCharacter = data["selected_character"].AsString();
            var connectionStatus = (PlayerConnectionStatus)data["connection_status"].AsInt32();
            var metadata = data["metadata"].AsString();

            var player = new PartyMember(playerId, isLeader, displayName);
            player.IsReady = isReady;
            player.SelectedCharacter = selectedCharacter;
            player.ConnectionStatus = connectionStatus;
            player.Metadata = metadata;

            return player;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to deserialize party member data: {ex.Message}");
            return null;
        }
    }

    private static Godot.Collections.Dictionary SerializePartyMemberToDictionary(PartyMember player)
    {
        var data = new Godot.Collections.Dictionary();
        data["player_id"] = player.PlayerId;
        data["display_name"] = player.DisplayName;
        data["is_leader"] = player.IsLeader;
        data["is_ready"] = player.IsReady;
        data["joined_at"] = player.JoinedAt.ToString("O");
        data["selected_character"] = player.SelectedCharacter ?? "";
        data["connection_status"] = (int)player.ConnectionStatus;
        data["metadata"] = player.Metadata ?? "";
        return data;
    }

    private static Godot.Collections.Dictionary SerializePartySettings(PartySettings settings)
    {
        var data = new Godot.Collections.Dictionary();
        data["map_path"] = settings.MapPath;
        data["game_mode"] = settings.GameMode;
        data["max_players"] = settings.MaxPlayers;
        data["friendly_fire"] = settings.FriendlyFire;
        data["difficulty"] = settings.Difficulty;
        data["time_limit"] = settings.TimeLimit;
        data["is_private"] = settings.IsPrivate;
        data["password"] = settings.Password ?? "";
        data["allow_spectators"] = settings.AllowSpectators;
        data["voice_chat_enabled"] = settings.VoiceChatEnabled;
        data["text_chat_enabled"] = settings.TextChatEnabled;

        // Serialize custom rules
        var customRulesArray = new Godot.Collections.Array();
        foreach (var kvp in settings.CustomRules)
        {
            var ruleDict = new Godot.Collections.Dictionary();
            ruleDict["key"] = kvp.Key;
            ruleDict["value"] = kvp.Value?.ToString() ?? "";
            customRulesArray.Add(ruleDict);
        }
        data["custom_rules"] = customRulesArray;

        return data;
    }

    private static PartySettings DeserializePartySettings(Godot.Collections.Dictionary data)
    {
        try
        {
            var settings = new PartySettings();
            settings.MapPath = data["map_path"].AsString();
            settings.GameMode = data["game_mode"].AsString();
            settings.MaxPlayers = data["max_players"].AsInt32();
            settings.FriendlyFire = data["friendly_fire"].AsBool();
            settings.Difficulty = data["difficulty"].AsInt32();
            settings.TimeLimit = data["time_limit"].AsInt32();
            settings.IsPrivate = data["is_private"].AsBool();
            settings.Password = data["password"].AsString();
            settings.AllowSpectators = data["allow_spectators"].AsBool();
            settings.VoiceChatEnabled = data["voice_chat_enabled"].AsBool();
            settings.TextChatEnabled = data["text_chat_enabled"].AsBool();

            // Deserialize custom rules
            if (data.ContainsKey("custom_rules"))
            {
                var customRulesArray = data["custom_rules"].AsGodotArray();
                foreach (var ruleData in customRulesArray)
                {
                    var ruleDict = ruleData.AsGodotDictionary();
                    var key = ruleDict["key"].AsString();
                    var value = ruleDict["value"].AsString();
                    settings.CustomRules[key] = value;
                }
            }

            return settings;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to deserialize lobby settings: {ex.Message}");
            return new PartySettings();
        }
    }

    /// <summary>
    /// Serializes a chat message for network transmission.
    /// </summary>
    public static byte[] SerializeChatMessage(ChatMessage message)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        try
        {
            var data = new Godot.Collections.Dictionary();
            data["id"] = message.MessageId;
            data["sender_player_id"] = message.SenderPlayerId;
            data["sender_display_name"] = message.SenderDisplayName;
            data["content"] = message.Content;
            data["timestamp"] = message.SentAt.ToString("O");
            data["type"] = (int)message.MessageType;

            var jsonString = Json.Stringify(data);
            return jsonString.ToUtf8Buffer();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to serialize chat message: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// Deserializes a chat message from network data.
    /// </summary>
    public static ChatMessage DeserializeChatMessage(byte[] data)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentNullException(nameof(data));

        try
        {
            var jsonString = data.GetStringFromUtf8();
            var json = new Json();
            var parseResult = json.Parse(jsonString);

            if (parseResult != Error.Ok)
            {
                GD.PrintErr($"Failed to parse chat message JSON: {parseResult}");
                return null;
            }

            var dataDict = json.Data.AsGodotDictionary();

            var id = dataDict["id"].AsString();
            var senderPlayerId = dataDict["sender_player_id"].AsString();
            var senderDisplayName = dataDict["sender_display_name"].AsString();
            var content = dataDict["content"].AsString();
            var timestampStr = dataDict["timestamp"].AsString();
            var type = (ChatMessageType)dataDict["type"].AsInt32();

            if (!DateTime.TryParse(timestampStr, out var timestamp))
            {
                timestamp = DateTime.UtcNow;
            }

            // Reconstruct preserving original metadata where possible
            ChatMessage reconstructed;
            if (senderPlayerId == "system")
            {
                reconstructed = new ChatMessage(content, type, dataDict.ContainsKey("channel_id") ? dataDict["channel_id"].AsString() : null);
            }
            else
            {
                reconstructed = new ChatMessage(senderPlayerId, senderDisplayName, content, type, dataDict.ContainsKey("channel_id") ? dataDict["channel_id"].AsString() : null);
            }

            // Note: ChatMessage generates new IDs/timestamps; we can't set them directly as properties are readonly.
            return reconstructed;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to deserialize chat message: {ex.Message}");
            return null;
        }
    }
}
