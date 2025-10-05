using Waterjam.Events;
using Godot;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Core.Services;

public partial class SettingsService : BaseService,
    IGameEventHandler<GameInitializedEvent>,
    IGameEventHandler<SettingsAppliedEvent>,
    IGameEventHandler<SettingsRequestedEvent>
{
    public const string SettingsFilePath = "user://settings.cfg";

    private ConfigFile _config;
    private WorldEnvironment _worldEnvironment;

    public override void _Ready()
    {
        base._Ready();
        _config = new ConfigFile();
        ConsoleSystem.Log("SettingsService Ready!", ConsoleChannel.System);
    }

    public void InitializeSettings()
    {
        LoadSettings();
        EnsureDefaultsPresent();
        ApplyAllSettings();
        ConsoleSystem.Log("Settings initialized and applied.", ConsoleChannel.System);

        GameEvent.DispatchGlobal(new SettingsLoadedEvent(GetAllSettings()));
    }

    private void LoadSettings()
    {
        var err = _config.Load(SettingsFilePath);
        if (err != Error.Ok)
        {
            ConsoleSystem.Log("Failed to load settings. Using defaults.", ConsoleChannel.System);
            SetDefaults();
        }
    }

    private void EnsureDefaultsPresent()
    {
        // Helper to add missing keys with defaults for already-loaded settings
        void EnsureDefaultValue(string section, string key, Variant value)
        {
            if (!_config.HasSectionKey(section, key))
            {
                _config.SetValue(section, key, value);
            }
        }

        // UI
        EnsureDefaultValue("ui", "hud_variant", "classic");
        EnsureDefaultValue("ui", "scanlines_intensity", 0.06f);
        EnsureDefaultValue("ui", "test_runner_ui_visible", false);

        // Optionally ensure other known defaults too (safe idempotent)
        EnsureDefaultValue("audio", "master_volume", 1.0f);
        EnsureDefaultValue("audio", "music_volume", 0.8f);
        EnsureDefaultValue("audio", "sfx_volume", 0.7f);
        EnsureDefaultValue("display", "resolution", "1920x1080");
        EnsureDefaultValue("display", "display_mode", 0);
        EnsureDefaultValue("display", "vsync", true);
        EnsureDefaultValue("display", "shadow_quality", 3);
        EnsureDefaultValue("display", "model_quality", 2);
        EnsureDefaultValue("display", "anti_aliasing", 0);
        EnsureDefaultValue("display", "bloom", 1);
        EnsureDefaultValue("display", "ssao", 0);
        EnsureDefaultValue("controls", "mouse_sensitivity", 1.0f);
        EnsureDefaultValue("controls", "invert_y", false);
        EnsureDefaultValue("accessibility", "reduce_flashing", false);
        EnsureDefaultValue("accessibility", "reduce_motion", false);
        EnsureDefaultValue("accessibility", "high_contrast", false);

        // Persist if any new defaults were added
        _config.Save(SettingsFilePath);
    }

    private void SetDefaults()
    {
        // Audio defaults
        _config.SetValue("audio", "master_volume", 1.0f);
        _config.SetValue("audio", "music_volume", 0.8f);
        _config.SetValue("audio", "sfx_volume", 0.7f);

        // Display defaults
        _config.SetValue("display", "resolution", "1920x1080");
        _config.SetValue("display", "display_mode", 0);
        _config.SetValue("display", "vsync", true);
        _config.SetValue("display", "shadow_quality", 3); // Medium
        _config.SetValue("display", "model_quality", 2); // High
        _config.SetValue("display", "anti_aliasing", 0); // Off
        _config.SetValue("display", "bloom", 1); // Low
        _config.SetValue("display", "ssao", 0); // Off

        // Control defaults
        _config.SetValue("controls", "mouse_sensitivity", 1.0f);
        _config.SetValue("controls", "invert_y", false);

        // Accessibility defaults
        _config.SetValue("accessibility", "reduce_flashing", false);
        _config.SetValue("accessibility", "reduce_motion", false);
        _config.SetValue("accessibility", "high_contrast", false);

        // UI defaults
        _config.SetValue("ui", "hud_variant", "classic"); // classic | v2
        _config.SetValue("ui", "scanlines_intensity", 0.06f);
        _config.SetValue("ui", "test_runner_ui_visible", false);
    }

    private Godot.Collections.Dictionary<string, Godot.Collections.Dictionary<string, Variant>> GetAllSettings()
    {
        var settings = new Godot.Collections.Dictionary<string, Godot.Collections.Dictionary<string, Variant>>
        {
            { "audio", new Godot.Collections.Dictionary<string, Variant>() },
            { "display", new Godot.Collections.Dictionary<string, Variant>() },
            { "controls", new Godot.Collections.Dictionary<string, Variant>() },
            { "accessibility", new Godot.Collections.Dictionary<string, Variant>() },
            { "ui", new Godot.Collections.Dictionary<string, Variant>() }
        };

        // Audio settings
        settings["audio"]["master_volume"] = _config.GetValue("audio", "master_volume", 1.0f);
        settings["audio"]["music_volume"] = _config.GetValue("audio", "music_volume", 0.8f);
        settings["audio"]["sfx_volume"] = _config.GetValue("audio", "sfx_volume", 0.7f);

        // Display settings
        settings["display"]["resolution"] = _config.GetValue("display", "resolution", "1920x1080");
        settings["display"]["display_mode"] = _config.GetValue("display", "display_mode", 0);
        settings["display"]["vsync"] = _config.GetValue("display", "vsync", true);
        settings["display"]["shadow_quality"] = _config.GetValue("display", "shadow_quality", 3);
        settings["display"]["model_quality"] = _config.GetValue("display", "model_quality", 2);
        settings["display"]["anti_aliasing"] = _config.GetValue("display", "anti_aliasing", 0);
        settings["display"]["bloom"] = _config.GetValue("display", "bloom", 1);
        settings["display"]["ssao"] = _config.GetValue("display", "ssao", 0);

        // Control settings
        settings["controls"]["mouse_sensitivity"] = _config.GetValue("controls", "mouse_sensitivity", 1.0f);
        settings["controls"]["invert_y"] = _config.GetValue("controls", "invert_y", false);

        // Accessibility settings
        settings["accessibility"]["reduce_flashing"] = _config.GetValue("accessibility", "reduce_flashing", false);
        settings["accessibility"]["reduce_motion"] = _config.GetValue("accessibility", "reduce_motion", false);
        settings["accessibility"]["high_contrast"] = _config.GetValue("accessibility", "high_contrast", false);

        // UI settings
        settings["ui"]["hud_variant"] = _config.GetValue("ui", "hud_variant", "classic");
        settings["ui"]["scanlines_intensity"] = _config.GetValue("ui", "scanlines_intensity", 0.06f);
        settings["ui"]["test_runner_ui_visible"] = _config.GetValue("ui", "test_runner_ui_visible", false);

        return settings;
    }

    private void ApplyAllSettings()
    {
        ApplyAudioSettings();
        ApplyDisplaySettings();
        ApplyControlSettings();
        ApplyAccessibilitySettings();
    }

    private void ApplySettings(Godot.Collections.Dictionary<string, Variant> settings)
    {
        var audioChanged = false;
        var displayChanged = false;
        var controlsChanged = false;
        var accessibilityChanged = false;
        var uiChanged = false;

        foreach (var kvp in settings)
        {
            var parts = kvp.Key.Split('/');
            if (parts.Length == 2)
                switch (parts[0])
                {
                    case "audio":
                        audioChanged = true;
                        break;
                    case "display":
                        displayChanged = true;
                        break;
                    case "controls":
                        controlsChanged = true;
                        break;
                    case "accessibility":
                        accessibilityChanged = true;
                        break;
                    case "ui":
                        uiChanged = true;
                        break;
                }
        }

        if (audioChanged) ApplyAudioSettings();
        if (displayChanged) ApplyDisplaySettings();
        if (controlsChanged) ApplyControlSettings();
        if (accessibilityChanged) ApplyAccessibilitySettings();
        if (uiChanged) ApplyUiSettings();
    }

    // Add the missing methods for Audio and Accessibility settings
    private void ApplyAudioSettings()
    {
        var masterVolume = (float)_config.GetValue("audio", "master_volume", 1.0f);
        var musicVolume = (float)_config.GetValue("audio", "music_volume", 0.8f);
        var sfxVolume = (float)_config.GetValue("audio", "sfx_volume", 0.7f);

        // Dispatch audio settings changed event
        GameEvent.DispatchGlobal(new AudioSettingsChangedEvent(musicVolume, sfxVolume));
    }

    private void ApplyAccessibilitySettings()
    {
        var reduceFlashing = (bool)_config.GetValue("accessibility", "reduce_flashing", false);
        var reduceMotion = (bool)_config.GetValue("accessibility", "reduce_motion", false);
        var highContrast = (bool)_config.GetValue("accessibility", "high_contrast", false);

        // Dispatch accessibility settings changed event
        GameEvent.DispatchGlobal(new AccessibilitySettingsChangedEvent(reduceFlashing, reduceMotion, highContrast));
    }

    private void ApplyDisplaySettings()
    {
        // Basic display settings
        var resolution = (string)_config.GetValue("display", "resolution", "1920x1080");
        var displayMode = (int)_config.GetValue("display", "display_mode", 0);
        var vsync = (bool)_config.GetValue("display", "vsync", true);

        // Apply resolution
        var parts = resolution.Split('x');
        if (parts.Length == 2)
        {
            var width = int.Parse(parts[0]);
            var height = int.Parse(parts[1]);
            DisplayServer.WindowSetSize(new Vector2I(width, height));
        }

        // Apply window mode
        switch (displayMode)
        {
            case 0:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                break;
            case 1:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                break;
            case 2:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                break;
        }

        // Apply VSync
        DisplayServer.WindowSetVsyncMode(vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);

        // Apply graphics quality settings
        ApplyGraphicsQualitySettings();
    }

    private void ApplyGraphicsQualitySettings()
    {
        var shadowQuality = (int)_config.GetValue("display", "shadow_quality", 3);
        var modelQuality = (int)_config.GetValue("display", "model_quality", 2);
        var antiAliasing = (int)_config.GetValue("display", "anti_aliasing", 0);
        var bloom = (int)_config.GetValue("display", "bloom", 1);
        var ssao = (int)_config.GetValue("display", "ssao", 0);

        // Find WorldEnvironment node in the current scene
        var currentScene = GetTree().CurrentScene;
        if (currentScene != null) _worldEnvironment = currentScene.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");

        // Apply shadow quality
        var mainLight = GetTree().CurrentScene?.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
        if (mainLight != null)
            switch (shadowQuality)
            {
                case 0: // Minimum
                    RenderingServer.DirectionalShadowAtlasSetSize(512, true);
                    mainLight.ShadowBias = 0.06f;
                    GetViewport().PositionalShadowAtlasSize = 0;
                    break;
                case 1: // Very Low
                    RenderingServer.DirectionalShadowAtlasSetSize(1024, true);
                    mainLight.ShadowBias = 0.04f;
                    GetViewport().PositionalShadowAtlasSize = 1024;
                    break;
                case 2: // Low
                    RenderingServer.DirectionalShadowAtlasSetSize(2048, true);
                    mainLight.ShadowBias = 0.03f;
                    GetViewport().PositionalShadowAtlasSize = 2048;
                    break;
                case 3: // Medium
                    RenderingServer.DirectionalShadowAtlasSetSize(4096, true);
                    mainLight.ShadowBias = 0.02f;
                    GetViewport().PositionalShadowAtlasSize = 4096;
                    break;
                case 4: // High
                    RenderingServer.DirectionalShadowAtlasSetSize(8192, true);
                    mainLight.ShadowBias = 0.01f;
                    GetViewport().PositionalShadowAtlasSize = 8192;
                    break;
                case 5: // Ultra
                    RenderingServer.DirectionalShadowAtlasSetSize(16384, true);
                    mainLight.ShadowBias = 0.005f;
                    GetViewport().PositionalShadowAtlasSize = 16384;
                    break;
            }

        // Apply model quality (LOD settings)
        switch (modelQuality)
        {
            case 0: // Low
                GetViewport().MeshLodThreshold = 4.0f;
                break;
            case 1: // Medium
                GetViewport().MeshLodThreshold = 2.0f;
                break;
            case 2: // High
                GetViewport().MeshLodThreshold = 1.0f;
                break;
            case 3: // Ultra
                GetViewport().MeshLodThreshold = 0.0f;
                break;
        }

        // Apply anti-aliasing settings
        switch (antiAliasing)
        {
            case 0: // Off
                GetViewport().Msaa3D = Viewport.Msaa.Disabled;
                GetViewport().UseTaa = false;
                GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                break;
            case 1: // TAA
                GetViewport().Msaa3D = Viewport.Msaa.Disabled;
                GetViewport().UseTaa = true;
                GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                break;
            case 2: // MSAA 2x
                GetViewport().Msaa3D = Viewport.Msaa.Msaa2X;
                GetViewport().UseTaa = false;
                GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                break;
            case 3: // MSAA 4x
                GetViewport().Msaa3D = Viewport.Msaa.Msaa4X;
                GetViewport().UseTaa = false;
                GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                break;
            case 4: // MSAA 8x
                GetViewport().Msaa3D = Viewport.Msaa.Msaa8X;
                GetViewport().UseTaa = false;
                GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                break;
        }

        // Apply post-processing effects if we have a WorldEnvironment
        if (_worldEnvironment?.Environment != null)
        {
            // Bloom settings
            switch (bloom)
            {
                case 0: // Off
                    _worldEnvironment.Environment.GlowEnabled = false;
                    break;
                case 1: // Low
                    _worldEnvironment.Environment.GlowEnabled = true;
                    RenderingServer.EnvironmentGlowSetUseBicubicUpscale(false);
                    break;
                case 2: // High
                    _worldEnvironment.Environment.GlowEnabled = true;
                    RenderingServer.EnvironmentGlowSetUseBicubicUpscale(true);
                    break;
            }

            // SSAO settings
            switch (ssao)
            {
                case 0: // Off
                    _worldEnvironment.Environment.SsaoEnabled = false;
                    break;
                case 1: // Low
                    _worldEnvironment.Environment.SsaoEnabled = true;
                    RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.Low, true, 0.5f, 2, 50, 300);
                    break;
                case 2: // Medium
                    _worldEnvironment.Environment.SsaoEnabled = true;
                    RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.Medium, true, 0.5f, 2, 50, 300);
                    break;
                case 3: // High
                    _worldEnvironment.Environment.SsaoEnabled = true;
                    RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.High, true, 0.5f, 2, 50, 300);
                    break;
            }
        }
    }

    private void ApplyControlSettings()
    {
        // TODO
        // var mouseSensitivity = (float)_config.GetValue("controls", "mouse_sensitivity", 1.0f);
        // var invertY = (bool)_config.GetValue("controls", "invert_y", false);
        //
        // // Dispatch event for control settings changes
        // // You'll need to create this event
        // GameEvent.DispatchGlobal(new ControlSettingsChangedEvent(mouseSensitivity, invertY));
    }

    public void OnGameEvent(GameInitializedEvent eventArgs)
    {
        InitializeSettings();
    }

    public void OnGameEvent(SettingsAppliedEvent eventArgs)
    {
        UpdateSettings(eventArgs.Settings);
        ApplySettings(eventArgs.Settings);
        SaveSettings();
    }

    public void OnGameEvent(SettingsRequestedEvent eventArgs)
    {
        var settings = GetAllSettings();
        GameEvent.DispatchGlobal(new SettingsLoadedEvent(settings));
    }

    private void SaveSettings()
    {
        var err = _config.Save(SettingsFilePath);
        if (err != Error.Ok) ConsoleSystem.LogErr($"[SettingsService] Failed to save settings.");
    }

    private void UpdateSettings(Godot.Collections.Dictionary<string, Variant> settings)
    {
        foreach (var kvp in settings)
        {
            var parts = kvp.Key.Split('/');
            if (parts.Length == 2) _config.SetValue(parts[0], parts[1], kvp.Value);
        }
    }

    private void ApplyUiSettings()
    {
        var variant = (string)_config.GetValue("ui", "hud_variant", "classic");
        GameEvent.DispatchGlobal(new UiHudVariantChangedEvent(variant));

        var intensity = (float)_config.GetValue("ui", "scanlines_intensity", 0.06f);
        GameEvent.DispatchGlobal(new UiScanlinesIntensityChangedEvent(intensity));
    }

    public bool IsTestRunnerUIVisible()
    {
        return (bool)_config.GetValue("ui", "test_runner_ui_visible", false);
    }
}