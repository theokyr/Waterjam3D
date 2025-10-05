namespace Waterjam.Events;

public record DialogueScreenDisplayChangedEvent(bool display) : IGameEvent;

public record InventoryScreenDisplayChangedEvent(bool display) : IGameEvent;

public record DisplaySettingsEvent(bool display) : IGameEvent;

// UI settings changes
public record UiHudVariantChangedEvent(string Variant) : IGameEvent;

public record UiScanlinesIntensityChangedEvent(float Intensity) : IGameEvent;

// Loading screen events
public record LoadingScreenShowEvent(float InitialProgress = 0.1f, string Message = "Loading...") : IGameEvent;

public record LoadingScreenUpdateEvent(float Progress, string Message) : IGameEvent;

public record LoadingScreenHideEvent : IGameEvent;