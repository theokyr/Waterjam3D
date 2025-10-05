using Waterjam.Domain;
using Waterjam.Core.Systems.Console;
using Godot;

namespace Waterjam.Events;

public record GameInitializedEvent(bool Success) : IGameEvent;

public record NewGameStartedEvent(string LevelScenePath = "res://scenes/dev/dev_citygen.tscn") : IGameEvent;

public record CharacterDamagedEvent(Entity Attacker = null, Entity Victim = null, float Damage = 0.0f) : IGameEvent;

public record ConsoleCommandRegisteredEvent(ConsoleCommand Command) : IGameEvent;

public record ConsoleCommandUnregisteredEvent(ConsoleCommand Command) : IGameEvent;

public record ConsoleMessageLoggedEvent(ConsoleMessage Message) : IGameEvent;

public record ConsoleHistoryClearedEvent() : IGameEvent;