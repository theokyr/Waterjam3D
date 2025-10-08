using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Waterjam.Domain.Party;

namespace Waterjam.Core.Services.Network;

public interface INetworkMessage
{
    string TypeId { get; }
}

public sealed class NetworkMessageEnvelope
{
    public string Type { get; set; }
    public int Version { get; set; } = 1;
    public Godot.Collections.Dictionary Payload { get; set; }
}

public static class NetworkMessageRegistry
{
    private sealed class Entry
    {
        public Func<INetworkMessage, Godot.Collections.Dictionary> ToPayload { get; init; }
        public Func<Godot.Collections.Dictionary, INetworkMessage> FromPayload { get; init; }
        public Action<INetworkMessage> Handler { get; init; }
    }

    private static readonly Dictionary<string, Entry> _typeIdToEntry = new();

    static NetworkMessageRegistry()
    {
        // For now, disable lobby-specific message registration; party flow uses events/services.
    }

    public static void Register<TMessage>(string typeId,
        Func<TMessage, Godot.Collections.Dictionary> toPayload,
        Func<Godot.Collections.Dictionary, TMessage> fromPayload,
        Action<TMessage> handler = null) where TMessage : INetworkMessage
    {
        if (string.IsNullOrWhiteSpace(typeId)) throw new ArgumentNullException(nameof(typeId));
        if (toPayload == null) throw new ArgumentNullException(nameof(toPayload));
        if (fromPayload == null) throw new ArgumentNullException(nameof(fromPayload));

        _typeIdToEntry[typeId] = new Entry
        {
            ToPayload = (m) => toPayload((TMessage)m),
            FromPayload = (d) => fromPayload(d),
            Handler = handler != null ? new Action<INetworkMessage>(m => handler((TMessage)m)) : null
        };
    }

    public static byte[] Serialize(INetworkMessage message)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        if (!_typeIdToEntry.TryGetValue(message.TypeId, out var entry))
        {
            GD.PrintErr($"[NetworkMessageRegistry] Unknown message type: {message.TypeId}");
            return Array.Empty<byte>();
        }

        var envelope = new NetworkMessageEnvelope
        {
            Type = message.TypeId,
            Version = 1,
            Payload = entry.ToPayload(message)
        };

        var dict = new Godot.Collections.Dictionary
        {
            { "type", envelope.Type },
            { "version", envelope.Version },
            { "payload", envelope.Payload }
        };

        var json = Json.Stringify(dict);
        return json.ToUtf8Buffer();
    }

    public static bool TryDeserialize(byte[] bytes, out INetworkMessage message)
    {
        message = null;
        if (bytes == null || bytes.Length == 0) return false;

        try
        {
            var jsonString = bytes.GetStringFromUtf8();
            var json = new Json();
            var parse = json.Parse(jsonString);
            if (parse != Error.Ok) return false;

            var dict = json.Data.AsGodotDictionary();
            var typeId = dict.ContainsKey("type") ? dict["type"].AsString() : null;
            if (string.IsNullOrWhiteSpace(typeId)) return false;

            if (!_typeIdToEntry.TryGetValue(typeId, out var entry)) return false;

            var payload = dict.ContainsKey("payload") ? dict["payload"].AsGodotDictionary() : new Godot.Collections.Dictionary();
            message = entry.FromPayload(payload);
            return message != null;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NetworkMessageRegistry] Failed to deserialize message: {ex.Message}");
            return false;
        }
    }

    public static void Dispatch(INetworkMessage message)
    {
        if (message == null) return;
        if (!_typeIdToEntry.TryGetValue(message.TypeId, out var entry) || entry.Handler == null) return;
        entry.Handler(message);
    }

    // Note: legacy lobby message registration removed during Party migration
}
// Legacy lobby messages have been removed in favor of Party domain driven flows


