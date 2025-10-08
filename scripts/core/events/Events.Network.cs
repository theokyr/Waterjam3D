using System.Collections.Generic;
using Waterjam.Core.Services;
using Waterjam.Domain.Chat;

namespace Waterjam.Events;

// Server events

public record NetworkServerStartedEvent(int Port) : IGameEvent;

public record NetworkLocalServerStartedEvent() : IGameEvent;

public record NetworkServerTickEvent(ulong Tick) : IGameEvent;

public record NetworkClientConnectedEvent(long PeerId) : IGameEvent;

public record NetworkClientDisconnectedEvent(long PeerId) : IGameEvent;

// Client events

public record NetworkConnectedToServerEvent(long ClientId) : IGameEvent;

public record NetworkConnectionFailedEvent() : IGameEvent;

public record NetworkDisconnectedFromServerEvent() : IGameEvent;

public record NetworkInputReceivedEvent(long ClientId, InputPacket Input) : IGameEvent;

// Replication events

public record NetworkEntitySpawnedEvent(ulong EntityId, string EntityType, Godot.Vector3 Position) : IGameEvent;

public record NetworkEntityDespawnedEvent(ulong EntityId) : IGameEvent;

public record NetworkEntityStateUpdateEvent(ulong EntityId, Dictionary<string, object> ComponentData) : IGameEvent;
