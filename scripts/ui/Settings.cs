using Waterjam.Events;
using Godot;

namespace Waterjam.UI;

public partial class Settings : PanelContainer, IGameEventHandler<SettingsLoadedEvent>
{
    private const string SETTINGS_GROUP_DISPLAY = "display";
    private const string SETTINGS_GROUP_AUDIO = "audio";
    private const string SETTINGS_GROUP_ACCESSIBILITY = "accessibility";
    private const string SETTINGS_GROUP_CONTROLS = "controls";
    private const string SETTINGS_GROUP_GAME = "game";
    private const string SETTINGS_GROUP_UI = "ui";

    // UI Elements
    private OptionButton resolutionOption;
    private OptionButton displayModeOption;
    private OptionButton vSyncOption;
    private OptionButton shadowQualityOption;
    private OptionButton modelQualityOption;
    private OptionButton antiAliasingOption;
    private OptionButton bloomOption;
    private OptionButton ssaoOption;
    private HSlider masterVolumeSlider;
    private HSlider musicVolumeSlider;
    private HSlider sfxVolumeSlider;
    private HSlider mouseSensitivitySlider;
    private CheckBox invertYCheckBox;
    private CheckBox reduceFlashingCheckBox;
    private CheckBox reduceMotionCheckBox;
    private CheckBox highContrastCheckBox;
    private OptionButton hudVariantOption;
    private HSlider scanlinesIntensitySlider;

    [Signal]
    public delegate void BackButtonPressedEventHandler();

    public override void _Ready()
    {
        InitializeReferences();
        SetupControlEvents();
        InitializeOptions();
        GameEvent.DispatchGlobal(new SettingsRequestedEvent());
    }

    private void InitializeReferences()
    {
        resolutionOption = GetNode<OptionButton>("%ResolutionOption");
        displayModeOption = GetNode<OptionButton>("%DisplayModeOption");
        vSyncOption = GetNode<OptionButton>("%VSyncOption");
        shadowQualityOption = GetNode<OptionButton>("%ShadowQualityOption");
        modelQualityOption = GetNode<OptionButton>("%ModelQualityOption");
        antiAliasingOption = GetNode<OptionButton>("%AntiAliasingOption");
        bloomOption = GetNode<OptionButton>("%BloomOption");
        ssaoOption = GetNode<OptionButton>("%SSAOOption");
        masterVolumeSlider = GetNode<HSlider>("%MasterVolumeSlider");
        musicVolumeSlider = GetNode<HSlider>("%MusicVolumeSlider");
        sfxVolumeSlider = GetNode<HSlider>("%SFXVolumeSlider");
        mouseSensitivitySlider = GetNode<HSlider>("%MouseSensitivitySlider");
        invertYCheckBox = GetNode<CheckBox>("%InvertYCheckBox");
        reduceFlashingCheckBox = GetNode<CheckBox>("%ReduceFlashingCheckBox");
        reduceMotionCheckBox = GetNode<CheckBox>("%ReduceMotionCheckBox");
        highContrastCheckBox = GetNode<CheckBox>("%HighContrastCheckBox");
        hudVariantOption = GetNodeOrNull<OptionButton>("%HudVariantOption");
        scanlinesIntensitySlider = GetNodeOrNull<HSlider>("%ScanlinesIntensitySlider");
    }

    private void InitializeOptions()
    {
        // Initialize resolution options
        resolutionOption.Clear();
        resolutionOption.AddItem("1280x720");
        resolutionOption.AddItem("1920x1080");
        resolutionOption.AddItem("2560x1440");
        resolutionOption.AddItem("3840x2160");

        // HUD variants
        if (hudVariantOption != null)
        {
            hudVariantOption.Clear();
            hudVariantOption.AddItem("Classic"); // value: classic
            hudVariantOption.AddItem("V2"); // value: v2
        }
    }

    private void SetupControlEvents()
    {
        // Display settings
        resolutionOption.ItemSelected += index => ApplyVideoSetting("resolution", GetResolutionFromIndex((int)index));
        displayModeOption.ItemSelected += index => ApplyVideoSetting("display_mode", index);
        vSyncOption.ItemSelected += index => ApplyVideoSetting("vsync", index);

        // Graphics quality settings
        shadowQualityOption.ItemSelected += index => ApplyVideoSetting("shadow_quality", index);
        modelQualityOption.ItemSelected += index => ApplyVideoSetting("model_quality", index);
        antiAliasingOption.ItemSelected += index => ApplyVideoSetting("anti_aliasing", index);
        bloomOption.ItemSelected += index => ApplyVideoSetting("bloom", index);
        ssaoOption.ItemSelected += index => ApplyVideoSetting("ssao", index);

        // Connect preset buttons
        var presetSection = GetNode<VBoxContainer>("MarginContainer/Content/TabContainer/Video/VideoSettings/PresetSection");
        var presets = presetSection.GetNode<HBoxContainer>("Presets");

        presets.GetNode<Button>("VeryLowPreset").Pressed += OnVeryLowPresetPressed;
        presets.GetNode<Button>("LowPreset").Pressed += OnLowPresetPressed;
        presets.GetNode<Button>("MediumPreset").Pressed += OnMediumPresetPressed;
        presets.GetNode<Button>("HighPreset").Pressed += OnHighPresetPressed;
        presets.GetNode<Button>("UltraPreset").Pressed += OnUltraPresetPressed;

        // Audio settings
        masterVolumeSlider.ValueChanged += value => ApplyAudioSetting("master_volume", value);
        musicVolumeSlider.ValueChanged += value => ApplyAudioSetting("music_volume", value);
        sfxVolumeSlider.ValueChanged += value => ApplyAudioSetting("sfx_volume", value);

        // Control settings
        mouseSensitivitySlider.ValueChanged += value => ApplyControlSetting("mouse_sensitivity", value);
        invertYCheckBox.Toggled += pressed => ApplyControlSetting("invert_y", pressed);

        // Accessibility settings
        reduceFlashingCheckBox.Toggled += pressed => ApplyAccessibilitySetting("reduce_flashing", pressed);
        reduceMotionCheckBox.Toggled += pressed => ApplyAccessibilitySetting("reduce_motion", pressed);
        highContrastCheckBox.Toggled += pressed => ApplyAccessibilitySetting("high_contrast", pressed);

        // UI settings
        if (hudVariantOption != null)
        {
            hudVariantOption.ItemSelected += index =>
            {
                var variant = index == 1 ? "v2" : "classic";
                ApplyUiSetting("hud_variant", variant);
            };
        }

        // Back button
        var backButton = GetNode<Button>("MarginContainer/Content/BackButton");
        backButton.Pressed += OnBackButtonPressed;
        // UI settings
        if (hudVariantOption != null)
            hudVariantOption.ItemSelected += index => ApplyUiSetting("hud_variant", ((int)index) == 1 ? "v2" : "classic");
        if (scanlinesIntensitySlider != null)
            scanlinesIntensitySlider.ValueChanged += value => ApplyUiSetting("scanlines_intensity", (float)value);
    }

    private string GetResolutionFromIndex(int index)
    {
        return resolutionOption.GetItemText(index);
    }

    private void ApplyVideoSetting(string key, Variant value)
    {
        var settings = new Godot.Collections.Dictionary<string, Variant> { { $"{SETTINGS_GROUP_DISPLAY}/{key}", value } };
        GameEvent.DispatchGlobal(new SettingsAppliedEvent(settings));
    }

    private void ApplyAudioSetting(string key, Variant value)
    {
        var settings = new Godot.Collections.Dictionary<string, Variant> { { $"{SETTINGS_GROUP_AUDIO}/{key}", value } };
        GameEvent.DispatchGlobal(new SettingsAppliedEvent(settings));
    }

    private void ApplyControlSetting(string key, Variant value)
    {
        var settings = new Godot.Collections.Dictionary<string, Variant> { { $"{SETTINGS_GROUP_CONTROLS}/{key}", value } };
        GameEvent.DispatchGlobal(new SettingsAppliedEvent(settings));
    }

    private void ApplyAccessibilitySetting(string key, Variant value)
    {
        var settings = new Godot.Collections.Dictionary<string, Variant> { { $"{SETTINGS_GROUP_ACCESSIBILITY}/{key}", value } };
        GameEvent.DispatchGlobal(new SettingsAppliedEvent(settings));
    }

    private void ApplyUiSetting(string key, Variant value)
    {
        var settings = new Godot.Collections.Dictionary<string, Variant> { { $"{SETTINGS_GROUP_UI}/{key}", value } };
        GameEvent.DispatchGlobal(new SettingsAppliedEvent(settings));
    }

    public void OnGameEvent(SettingsLoadedEvent eventArgs)
    {
        LoadSettings(eventArgs.Settings);
    }

    private void LoadSettings(Godot.Collections.Dictionary<string, Godot.Collections.Dictionary<string, Variant>> settings)
    {
        // Load Display Settings
        if (settings.TryGetValue(SETTINGS_GROUP_DISPLAY, out var displaySettings))
        {
            if (displaySettings.TryGetValue("resolution", out var resolution))
            {
                var resolutionStr = (string)resolution;
                for (var i = 0; i < resolutionOption.ItemCount; i++)
                    if (resolutionOption.GetItemText(i) == resolutionStr)
                    {
                        resolutionOption.Selected = i;
                        break;
                    }
            }

            if (displaySettings.TryGetValue("display_mode", out var displayMode))
                displayModeOption.Selected = (int)displayMode;

            if (displaySettings.TryGetValue("vsync", out var vsync))
                vSyncOption.Selected = (int)vsync;

            if (displaySettings.TryGetValue("shadow_quality", out var shadowQuality))
                shadowQualityOption.Selected = (int)shadowQuality;

            if (displaySettings.TryGetValue("model_quality", out var modelQuality))
                modelQualityOption.Selected = (int)modelQuality;

            if (displaySettings.TryGetValue("anti_aliasing", out var antiAliasing))
                antiAliasingOption.Selected = (int)antiAliasing;

            if (displaySettings.TryGetValue("bloom", out var bloom))
                bloomOption.Selected = (int)bloom;

            if (displaySettings.TryGetValue("ssao", out var ssao))
                ssaoOption.Selected = (int)ssao;
        }

        // Load Audio Settings
        if (settings.TryGetValue(SETTINGS_GROUP_AUDIO, out var audioSettings))
        {
            if (audioSettings.TryGetValue("master_volume", out var masterVolume))
                masterVolumeSlider.Value = (float)masterVolume;

            if (audioSettings.TryGetValue("music_volume", out var musicVolume))
                musicVolumeSlider.Value = (float)musicVolume;

            if (audioSettings.TryGetValue("sfx_volume", out var sfxVolume))
                sfxVolumeSlider.Value = (float)sfxVolume;
        }

        // Load Control Settings
        if (settings.TryGetValue(SETTINGS_GROUP_CONTROLS, out var controlSettings))
        {
            if (controlSettings.TryGetValue("mouse_sensitivity", out var mouseSensitivity))
                mouseSensitivitySlider.Value = (float)mouseSensitivity;

            if (controlSettings.TryGetValue("invert_y", out var invertY))
                invertYCheckBox.ButtonPressed = (bool)invertY;
        }

        // Load Accessibility Settings
        if (settings.TryGetValue(SETTINGS_GROUP_ACCESSIBILITY, out var accessibilitySettings))
        {
            if (accessibilitySettings.TryGetValue("reduce_flashing", out var reduceFlashing))
                reduceFlashingCheckBox.ButtonPressed = (bool)reduceFlashing;

            if (accessibilitySettings.TryGetValue("reduce_motion", out var reduceMotion))
                reduceMotionCheckBox.ButtonPressed = (bool)reduceMotion;

            if (accessibilitySettings.TryGetValue("high_contrast", out var highContrast))
                highContrastCheckBox.ButtonPressed = (bool)highContrast;
        }

        // Load UI Settings
        if (settings.TryGetValue(SETTINGS_GROUP_UI, out var uiSettings))
        {
            if (uiSettings.TryGetValue("hud_variant", out var hudVariant) && hudVariantOption != null)
            {
                var val = (string)hudVariant;
                hudVariantOption.Selected = val == "v2" ? 1 : 0;
            }

            if (uiSettings.TryGetValue("scanlines_intensity", out var scanlinesIntensity) && scanlinesIntensitySlider != null)
            {
                scanlinesIntensitySlider.Value = (float)scanlinesIntensity;
            }
        }
    }

    private void OnBackButtonPressed()
    {
        EmitSignal(SignalName.BackButtonPressed);
    }

    // Quality Preset Handlers
    public void OnVeryLowPresetPressed()
    {
        ApplyPreset(0); // Very Low
    }

    public void OnLowPresetPressed()
    {
        ApplyPreset(1); // Low
    }

    public void OnMediumPresetPressed()
    {
        ApplyPreset(2); // Medium
    }

    public void OnHighPresetPressed()
    {
        ApplyPreset(3); // High
    }

    public void OnUltraPresetPressed()
    {
        ApplyPreset(4); // Ultra
    }

    private void ApplyPreset(int preset)
    {
        var settings = new Godot.Collections.Dictionary<string, Variant>();

        // Define preset configurations
        var presetConfigs = new System.Collections.Generic.Dictionary<string, int>();

        switch (preset)
        {
            case 0: // Very Low
                presetConfigs["shadow_quality"] = 0;
                presetConfigs["model_quality"] = 0;
                presetConfigs["anti_aliasing"] = 0;
                presetConfigs["bloom"] = 0;
                presetConfigs["ssao"] = 0;
                break;

            case 1: // Low
                presetConfigs["shadow_quality"] = 1;
                presetConfigs["model_quality"] = 0;
                presetConfigs["anti_aliasing"] = 1;
                presetConfigs["bloom"] = 0;
                presetConfigs["ssao"] = 0;
                break;

            case 2: // Medium
                presetConfigs["shadow_quality"] = 2;
                presetConfigs["model_quality"] = 1;
                presetConfigs["anti_aliasing"] = 2;
                presetConfigs["bloom"] = 1;
                presetConfigs["ssao"] = 1;
                break;

            case 3: // High
                presetConfigs["shadow_quality"] = 3;
                presetConfigs["model_quality"] = 2;
                presetConfigs["anti_aliasing"] = 3;
                presetConfigs["bloom"] = 2;
                presetConfigs["ssao"] = 2;
                break;

            case 4: // Ultra
                presetConfigs["shadow_quality"] = 5;
                presetConfigs["model_quality"] = 3;
                presetConfigs["anti_aliasing"] = 4;
                presetConfigs["bloom"] = 2;
                presetConfigs["ssao"] = 3;
                break;
        }

        // Update UI to reflect new settings
        foreach (var config in presetConfigs)
        {
            switch (config.Key)
            {
                case "shadow_quality":
                    shadowQualityOption.Selected = config.Value;
                    break;
                case "model_quality":
                    modelQualityOption.Selected = config.Value;
                    break;
                case "anti_aliasing":
                    antiAliasingOption.Selected = config.Value;
                    break;
                case "bloom":
                    bloomOption.Selected = config.Value;
                    break;
                case "ssao":
                    ssaoOption.Selected = config.Value;
                    break;
            }

            // Add to settings dictionary with proper group prefix
            settings[$"display/{config.Key}"] = config.Value;
        }

        // Dispatch a single event with all changed settings
        GameEvent.DispatchGlobal(new SettingsAppliedEvent(settings));
    }
}