using WorldGame.Domain;

namespace WorldGame.Events;

public record GameInitializedEvent(bool Success) : IGameEvent;

public record NewGameStartedEvent(string LevelScenePath = "res://scenes/dev/dev_citygen.tscn") : IGameEvent;

public record CharacterDamagedEvent(Entity Attacker = null, Entity Victim = null, float Damage = 0.0f) : IGameEvent;