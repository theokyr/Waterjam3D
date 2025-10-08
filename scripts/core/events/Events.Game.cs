using Waterjam.Domain;
using Waterjam.Core.Systems.Console;
using Godot;

namespace Waterjam.Events;

public record GameInitializedEvent(bool Success) : IGameEvent;

public record NewGameStartedEvent(string LevelScenePath = "res://scenes/dev/dev.tscn") : IGameEvent;

public record QuitRequestedEvent() : IGameEvent;

public record CharacterDamagedEvent(Entity Attacker = null, Entity Victim = null, float Damage = 0.0f) : IGameEvent;

public record ConsoleCommandRegisteredEvent(ConsoleCommand Command) : IGameEvent;

public record ConsoleCommandUnregisteredEvent(ConsoleCommand Command) : IGameEvent;

public record ConsoleMessageLoggedEvent(ConsoleMessage Message) : IGameEvent;

public record ConsoleHistoryClearedEvent() : IGameEvent;

// Network Player Events
public record NetworkPlayerSpawnedEvent(long PeerId, Vector3 Position, string PlayerName) : IGameEvent;

public record NetworkPlayerRemovedEvent(long PeerId) : IGameEvent;

public record NetworkPlayerMovedEvent(long PeerId, Vector3 Position, Vector3 Velocity) : IGameEvent;