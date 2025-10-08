using Waterjam.Events;
using Waterjam.Core;
using Godot;
using Waterjam.Core.Services;
using Waterjam.Core.Services.Network;
using Waterjam.Core.Systems.Console;
using Waterjam.Game.Services;
using Waterjam.Game.Services.Party;
using Waterjam.Game.Services.Lobby;
using System.Linq;
using Waterjam.Domain.Lobby;
using GodotSteam;

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
    private const string PartyBarScenePath = "res://scenes/ui/PartyBar.tscn";
    private const string PartyScenePath = "res://scenes/ui/PartyUI.tscn";

    public override void _Ready()
    {
        _gameService = GetNode<GameService>("/root/GameService");
        InitializeMainMenu();
        // PartyBar is now embedded in the scene, no need to mount programmatically
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
        // Start new game via event so GameService owns the flow
        GameEvent.DispatchGlobal(new NewGameStartedEvent("res://scenes/dev/dev.tscn"));
        // Hide self; UiService will manage further UI
        CallDeferred(Node.MethodName.QueueFree);
    }

    private void OnMultiplayerButtonPressed()
    {
        OnButtonPressed();
		StartMultiplayerFlow();
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

	private void StartMultiplayerFlow()
	{
		// Ensure Platform/Steam readiness
		var platformService = GetNodeOrNull<PlatformService>("/root/PlatformService");
		if (platformService == null || !PlatformService.IsSteamInitialized)
		{
			ShowErrorDialog("Multiplayer Unavailable", "Steam is not initialized. Please start Steam and restart the game.");
			return;
		}

		// Resolve services
		var partyService = GetNodeOrNull<PartyService>("/root/PartyService");
		var lobbyService = GetNodeOrNull<LobbyService>("/root/LobbyService");
		var networkService = GetNodeOrNull<NetworkService>("/root/NetworkService");
		if (partyService == null || lobbyService == null || networkService == null)
		{
			ShowErrorDialog("Multiplayer Unavailable", "Required services are missing. Please try again.");
			return;
		}

		// Ensure LobbyService has a local player ID (align with PartyService)
		var localPlayerId = partyService.GetLocalPlayerId();
		if (string.IsNullOrEmpty(localPlayerId))
		{
			ShowErrorDialog("Multiplayer Unavailable", "Player identity not ready yet. Please wait a moment and try again.");
			return;
		}
		lobbyService.SetLocalPlayerId(localPlayerId);

		// Create default lobby with sensible name and settings
		string personaName = null;
		try { personaName = Steam.GetPersonaName(); } catch { }
		var displayName = !string.IsNullOrWhiteSpace(personaName) ? $"{personaName}'s Lobby" : "My Lobby";
		var settings = new LobbySettings();

		// Switch NetworkService to Steam backend and start server (creates Steam lobby)
		ConsoleSystem.Log("[MainMenu] Starting multiplayer: creating Steam lobby...", ConsoleChannel.UI);
		try
		{
			// Disconnect any existing local server connection first
			if (networkService.Mode != NetworkMode.None)
			{
				ConsoleSystem.Log("[MainMenu] Disconnecting existing network session", ConsoleChannel.Network);
				networkService.Disconnect();
			}

			// Switch to Steam networking backend
			var reflectedConfig = networkService.GetType().GetField("_config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			if (reflectedConfig != null)
			{
				var config = reflectedConfig.GetValue(networkService) as NetworkConfig;
				if (config != null && config.Backend != NetworkBackend.Steam)
				{
					config.Backend = NetworkBackend.Steam;
					// Trigger adapter re-initialization
					var initMethod = networkService.GetType().GetMethod("InitializeAdapter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
					initMethod?.Invoke(networkService, null);
					ConsoleSystem.Log("[MainMenu] Switched to Steam networking backend", ConsoleChannel.Network);
				}
			}

			// Start the Steam lobby server
			var started = networkService.StartServer(0);
			if (!started)
			{
				ShowErrorDialog("Multiplayer Failed", "Failed to create Steam lobby. Please try again.");
				return;
			}
		}
		catch (System.Exception ex)
		{
			ConsoleSystem.LogErr($"[MainMenu] Failed to start Steam lobby: {ex.Message}", ConsoleChannel.Network);
			ShowErrorDialog("Multiplayer Failed", $"Failed to create Steam lobby: {ex.Message}");
			return;
		}

		// Create the game lobby (this manages players and settings)
		GameEvent.DispatchGlobal(new CreateLobbyRequestEvent(displayName, settings));

		// Navigate to Lobby UI
		ShowLobbyUI();
	}

	private void ShowErrorDialog(string title, string message)
	{
		var dialog = new AcceptDialog();
		dialog.Title = title;
		dialog.DialogText = message;
		dialog.AddCancelButton("Close");
		AddChild(dialog);
		dialog.PopupCentered();
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