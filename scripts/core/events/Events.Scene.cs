using Waterjam.Events;

namespace Waterjam.Events;

public record SceneLoadRequestedEvent(string ScenePath, bool Additive = false) : IGameEvent;

public record SceneLoadEvent(string ScenePath, bool Additive = false) : IGameEvent;

public record SceneDestroyRequestedEvent(string ScenePath) : IGameEvent;

public record SceneDestroyEvent(string ScenePath) : IGameEvent;