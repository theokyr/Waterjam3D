using Godot;

namespace Waterjam.Events;

public record AnimationChangedEvent(
    string ActorId,
    string Animation,
    bool Loop = true,
    bool? FlipX = null,
    float? Speed = null
) : IGameEvent;


