namespace Waterjam.Events;

public record SoundPlaySfxEvent(string StreamPath, float VolumeDb = -12.0f) : IGameEvent;

public record SoundPlayMusicEvent(string StreamPath, float VolumeDb = -12.0f) : IGameEvent;

public record SoundStopMusicEvent(string StreamPath, float VolumeDb = -12.0f, int Beat = 0) : IGameEvent;