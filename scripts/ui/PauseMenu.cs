using Godot;
using Waterjam.Core.Systems.Console;
using Waterjam.Events;
using Waterjam.Game.Services;

namespace Waterjam.UI;

public partial class PauseMenu : Control,
    IGameEventHandler<PlayerSpawnedEvent>,
    IGameEventHandler<DisplaySettingsEvent>
{
    private GameService _gameService;
    private VBoxContainer _menuButtons;
    private Settings _settings;
    private bool _canPause;
    private bool _isInitialized;
    private bool _isSaveLoadInProgress;

    private const string SettingsScenePath = "res://scenes/ui/Settings.tscn";

    public override void _Ready()
    {
        _gameService = GetNode<GameService>("/root/GameService");
        InitializePauseMenu();
        Hide();
        _isInitialized = true;
    }

    public void Refresh()
    {
        if (!Visible) return;

        _isSaveLoadInProgress = false;

        if (_menuButtons != null)
            foreach (var child in _menuButtons.GetChildren())
                if (child is Button button)
                    button.Disabled = false;
    }

    private void InitializePauseMenu()
    {
        _menuButtons = GetNode<VBoxContainer>("CenterContainer/PausePanel/MarginContainer/PauseMenuButtonsContainer");
        if (_menuButtons == null)
        {
            ConsoleSystem.LogErr("PauseMenuButtonsContainer not found!", ConsoleChannel.UI);
            return;
        }

        SetupButton("ResumeButton", OnResumeButtonPressed);
        SetupButton("OptionsButton", OnOptionsButtonPressed);
        SetupButton("QuitButton", OnQuitButtonPressed);
    }

    private void SetupButton(string name, System.Action callback)
    {
        var button = _menuButtons.GetNode<Button>(name);
        button.Pressed += callback;
        button.MouseEntered += OnButtonHover;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_canPause) return;

        if (@event.IsActionPressed("ui_pause"))
        {
            if (!Visible)
                ShowPauseMenu();
            else
                HidePauseMenu();
            GetViewport().SetInputAsHandled();
        }
    }

    private void ShowPauseMenu()
    {
        Show();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().Paused = true;
        _menuButtons.GetNode<Button>("ResumeButton")?.GrabFocus();
    }

    private void HidePauseMenu()
    {
        if (_isSaveLoadInProgress) return;

        if (_settings != null)
        {
            HideSettings();
            return;
        }

        Hide();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        GetTree().Paused = false;
    }

    private void OnButtonHover()
    {
        GameEvent.DispatchGlobal(new SoundPlaySfxEvent("res://resources/sounds/hover.wav"));
    }

    private void OnButtonPressed()
    {
        GameEvent.DispatchGlobal(new SoundPlaySfxEvent("res://resources/sounds/click.wav"));
    }

    private void OnResumeButtonPressed()
    {
        OnButtonPressed();
        HidePauseMenu();
    }

    private void OnSaveButtonPressed()
    {
        if (_isSaveLoadInProgress) return;

        OnButtonPressed();
    }

    private void OnLoadButtonPressed()
    {
        if (_isSaveLoadInProgress) return;

        OnButtonPressed();
    }

    private void OnOptionsButtonPressed()
    {
        OnButtonPressed();
        ShowSettings();
    }

    private void OnQuitButtonPressed()
    {
        OnButtonPressed();
        _gameService?.QuitGame();
        Hide();
    }

    private void ShowSettings()
    {
        var settingsScene = GD.Load<PackedScene>(SettingsScenePath);
        _settings = settingsScene.Instantiate<Settings>();
        AddChild(_settings);
        _settings.BackButtonPressed += OnSettingsBackButtonPressed;
        _menuButtons.Visible = false;
    }

    private void HideSettings()
    {
        if (_settings != null)
        {
            _settings.QueueFree();
            _settings = null;
            _menuButtons.Visible = true;
        }
    }

    private void OnSettingsBackButtonPressed()
    {
        HideSettings();
    }

    public void OnGameEvent(PlayerSpawnedEvent eventArgs)
    {
        _canPause = true;
    }

    public void OnGameEvent(DisplaySettingsEvent eventArgs)
    {
        if (_isInitialized)
            ShowSettings();
    }

    public override void _ExitTree()
    {
        if (_settings != null)
            _settings.QueueFree();
    }
}