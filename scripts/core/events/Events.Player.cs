using Godot;

namespace Waterjam.Events;

public record PlayerSpawnedEvent(Node Player, Node SpawnPoint) : IGameEvent;

public record PlayerJumpedEvent() : IGameEvent;

// New event to request player spawning with more flexibility
public enum PlayerSpawnSource
{
    NewGame, // Normal spawn at level start
    SaveGame, // Restoring from save
    Respawn, // After death
    Teleport // Moving to a new location
}

public record PlayerSpawnRequestEvent(
    Node ParentSceneNode, // The node to add the player/camera to
    Vector3? Position = null, // Optional position override
    Vector3? Rotation = null, // Optional rotation override (Using Vector3 for Euler)
    PlayerSpawnSource SpawnSource = PlayerSpawnSource.NewGame,
    bool ForceCameraReset = false // Whether to force camera reinitialization
) : IGameEvent;