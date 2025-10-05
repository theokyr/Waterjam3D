using Godot;

namespace Waterjam.Events;

public record SettingsAppliedEvent(Godot.Collections.Dictionary<string, Variant> Settings) : IGameEvent;

public record AccessibilitySettingsChangedEvent(bool ReduceFlashing, bool ReduceScreenShake, bool HighContrastMode) : IGameEvent;

public record AudioSettingsChangedEvent(float MusicVolume, float SfxVolume) : IGameEvent;

public record SettingsRequestedEvent() : IGameEvent;

public record SettingsLoadedEvent(Godot.Collections.Dictionary<string, Godot.Collections.Dictionary<string, Variant>> Settings) : IGameEvent;