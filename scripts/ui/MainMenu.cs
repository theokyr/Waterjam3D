using Waterjam.Events;
using Waterjam.Core;
using Godot;
using Waterjam.Core.Services;
using Waterjam.Core.Systems.Console;
using Waterjam.Game.Services;
using Waterjam.Game.Services.Party;
using Waterjam.Game.Services.Lobby;
using System.Linq;

namespace Waterjam.UI;

public partial class MainMenu : Control, IGameEventHandler<DisplaySettingsEvent>, IGameEventHandler<UiShowPartyScreenEvent>, IGameEventHandler<UiShowMainMenuEvent>, IGameEventHandler<UiShowLobbyScreenEvent>
{
    private GameService _gameService;
    private VBoxContainer _menuButtons;
    private Control _topRight;
    private Settings _settings;
    private bool _isInitialized;

    private const string SettingsScenePath = "res://scenes/ui/Settings.tscn";
    private const string LobbyScenePath = "res://scenes/ui/LobbyUI.tscn";
    private const string PartyScenePath = "res://scenes/ui/PartyUI.tscn";

    public override void _Ready()
    {
        _gameService = GetNode<GameService>("/root/GameService");
        InitializeMainMenu();
        MountPartyBar();
        _isInitialized = true;
    }

    private void InitializeMainMenu()
    {
        _menuButtons = GetNode<VBoxContainer>("CenterContainer/MainMenuButtonsContainer");
        _topRight = GetNodeOrNull<Control>("TopRight");
        if (_menuButtons == null)
        {
            ConsoleSystem.LogErr("MainMenuButtonsContainer not found!", ConsoleChannel.UI);
            return;
        }

        SetupButton("NewGameButton", OnStartButtonPressed);
        SetupButton("MultiplayerButton", OnMultiplayerButtonPressed);
        SetupButton("OptionsButton", OnOptionsButtonPressed);
        SetupButton("QuitButton", OnQuitButtonPressed);
        _menuButtons.GetNode<Button>("NewGameButton")?.GrabFocus();
    }

    private void MountPartyBar()
    {
        if (_topRight == null) return;
        // Avoid duplicates if re-entering
        if (_topRight.GetNodeOrNull("PartyBar") != null) return;

        var partyBar = new Waterjam.UI.Components.PartyBar();
        _topRight.AddChild(partyBar);
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
        GameEvent.DispatchGlobal(new SceneLoadRequestedEvent("res://scenes/dev/dev.tscn"));
        // Hide self to reveal scene immediately
        CallDeferred(Node.MethodName.QueueFree);
    }

    private void OnMultiplayerButtonPressed()
    {
        OnButtonPressed();
        ShowLobbyUI();
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

    private void ShowMultiplayerUI()
    {
        var partyScene = GD.Load<PackedScene>(PartyScenePath);
        var partyUI = partyScene.Instantiate<PartyUI>();
        AddChild(partyUI);

        _menuButtons.Visible = false;
    }

    private void ShowLobbyUI()
    {
        var lobbyScene = GD.Load<PackedScene>("res://scenes/ui/LobbyUI.tscn");
        var lobbyUI = lobbyScene.Instantiate<LobbyUI>();
        AddChild(lobbyUI);
        _menuButtons.Visible = false;
    }

    private void OnMultiplayerUIBackPressed()
    {
        // Hide current multiplayer UI and show main menu buttons
        var currentUI = GetChildren().OfType<Control>().FirstOrDefault(child =>
            child != _menuButtons.GetParent().GetParent() && child != _settings && child.Name != "TopRight" && child.Name != "Background" && child.Name != "CenterContainer");
        if (currentUI != null)
        {
            currentUI.QueueFree();
        }

        _menuButtons.Visible = true;
        _menuButtons.GetNode<Button>("MultiplayerButton")?.GrabFocus();
    }

    public void OnGameEvent(UiShowPartyScreenEvent eventArgs)
    {
        ShowMultiplayerUI();
    }

    public void OnGameEvent(UiShowMainMenuEvent eventArgs)
    {
        OnMultiplayerUIBackPressed();
    }

    public void OnGameEvent(UiShowLobbyScreenEvent eventArgs)
    {
        ShowLobbyUI();
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