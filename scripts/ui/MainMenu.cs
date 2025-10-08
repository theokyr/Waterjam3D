using Waterjam.Events;
using Waterjam.Core;
using Godot;
using Waterjam.Core.Services;
using Waterjam.Core.Systems.Console;
using Waterjam.Game.Services;

namespace Waterjam.UI;

public partial class MainMenu : Control, IGameEventHandler<DisplaySettingsEvent>
{
    private GameService _gameService;
    private VBoxContainer _menuButtons;
    private Settings _settings;
    private bool _isInitialized;

    private const string SettingsScenePath = "res://scenes/ui/Settings.tscn";

    public override void _Ready()
    {
        _gameService = GetNode<GameService>("/root/GameService");
        InitializeMainMenu();
        _isInitialized = true;
    }

    private void InitializeMainMenu()
    {
        _menuButtons = GetNode<VBoxContainer>("CenterContainer/MainMenuButtonsContainer");
        if (_menuButtons == null)
        {
            ConsoleSystem.LogErr("MainMenuButtonsContainer not found!", ConsoleChannel.UI);
            return;
        }

        SetupButton("NewGameButton", OnStartButtonPressed);
        SetupButton("OptionsButton", OnOptionsButtonPressed);
        SetupButton("QuitButton", OnQuitButtonPressed);
        _menuButtons.GetNode<Button>("NewGameButton")?.GrabFocus();
    }

    private void SetupButton(string name, System.Action callback)
    {
        var button = _menuButtons.GetNode<Button>(name);
        button.Pressed += callback;
        button.MouseEntered += OnButtonHover;
    }

    private void OnButtonHover()
    {
        GameEvent.DispatchGlobal(new SoundPlaySfxEvent("res://resources/sounds/hover.wav"));
    }

    private void OnButtonPressed()
    {
        GameEvent.DispatchGlobal(new SoundPlaySfxEvent("res://resources/sounds/click.wav"));
    }

    private void OnStartButtonPressed()
    {
        OnButtonPressed();
        // Load dev scene per request
        GameEvent.DispatchGlobal(new SceneLoadRequestedEvent("res://scenes/DeadlockMovementTestEnvironment.tscn"));
        // Hide self to reveal scene immediately
        CallDeferred(Node.MethodName.QueueFree);
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