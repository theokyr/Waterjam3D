using Waterjam.Events;
using Waterjam.Core;
using Godot;
using Waterjam.Core.Services;
using Waterjam.Core.Services.Network;
using Waterjam.Core.Systems.Console;
using Waterjam.Game.Services;
using Waterjam.Game.Services.Party;
using System;
using System.Linq;
using GodotSteam;

namespace Waterjam.UI;

public partial class MainMenu : Control,
	IGameEventHandler<LobbyCreatedEvent>, IGameEventHandler<DisplaySettingsEvent>, IGameEventHandler<UiShowPartyScreenEvent>, IGameEventHandler<UiShowMainMenuEvent>, IGameEventHandler<UiShowLobbyScreenEvent>
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
        InitializePartyChatPanel();
        // PartyBar is now embedded in the scene, no need to mount programmatically
        _isInitialized = true;
    }
    
    private void InitializePartyChatPanel()
    {
        try
        {
            // Add party chat panel to the main menu (it will show/hide itself based on party status)
            var chatPanel = new Waterjam.UI.Components.PartyChatPanel();
            AddChild(chatPanel);
            ConsoleSystem.Log("[MainMenu] Party chat panel initialized", ConsoleChannel.UI);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogWarn($"[MainMenu] Failed to initialize party chat panel: {ex.Message}", ConsoleChannel.UI);
        }
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

        SetupButton("NewGameButton", OnStartGamePressed);
        SetupButton("OptionsButton", OnOptionsButtonPressed);
        SetupButton("QuitButton", OnQuitButtonPressed);
        
        // Hide multiplayer button - we have a unified flow now
        var multiplayerButton = _menuButtons.GetNodeOrNull<Button>("MultiplayerButton");
        if (multiplayerButton != null)
        {
            multiplayerButton.Visible = false;
        }
        
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

    private void OnStartGamePressed()
    {
        OnButtonPressed();
        
        // Check if we have a party (we always should, even if solo)
        var partyService = GetNodeOrNull<PartyService>("/root/PartyService");
        if (partyService == null)
        {
            // Fallback to simple solo game start
            GameEvent.DispatchGlobal(new NewGameStartedEvent("res://scenes/dev/dev.tscn"));
            CallDeferred(Node.MethodName.QueueFree);
            return;
        }
        
        var currentParty = partyService.GetCurrentPlayerParty();
        var localPlayerId = partyService.GetLocalPlayerId();
        
        // If no party exists, auto-create one (user is always in a party)
        if (currentParty == null && !string.IsNullOrEmpty(localPlayerId))
        {
            AutoCreateSoloParty(partyService, localPlayerId);
            // Wait for party creation, then continue
            GetTree().CreateTimer(0.5f).Timeout += () => OnStartGamePressed();
            return;
        }
        
        // Check if we're the party leader
        bool isLeader = currentParty?.LeaderPlayerId == localPlayerId;
        
        // Start the unified game flow
        StartUnifiedGameFlow(isLeader);
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

    private void AutoCreateSoloParty(PartyService partyService, string localPlayerId)
    {
        try
        {
            string displayName = "My Party";
            if (PlatformService.IsSteamInitialized)
            {
                try
                {
                    var personaName = Steam.GetPersonaName();
                    if (!string.IsNullOrWhiteSpace(personaName))
                    {
                        displayName = $"{personaName}'s Party";
                    }
                }
                catch { }
            }
            
            ConsoleSystem.Log($"[MainMenu] Auto-creating solo party: {displayName}", ConsoleChannel.UI);
            GameEvent.DispatchGlobal(new CreatePartyRequestEvent(displayName, 8));
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[MainMenu] Failed to auto-create party: {ex.Message}", ConsoleChannel.UI);
        }
    }

    private void StartUnifiedGameFlow(bool isLeader)
    {
        try
        {
            var partyService = GetNodeOrNull<PartyService>("/root/PartyService");
            var currentParty = partyService?.GetCurrentPlayerParty();
            var localPlayerId = partyService?.GetLocalPlayerId();
            
            // If solo (only us in party), just start the game immediately with default settings
            if (currentParty == null || currentParty.Members.Count <= 1)
            {
                ConsoleSystem.Log("[MainMenu] Solo play - starting game with default settings", ConsoleChannel.UI);
                GameEvent.DispatchGlobal(new NewGameStartedEvent("res://scenes/dev/dev.tscn"));
                CallDeferred(Node.MethodName.QueueFree);
                return;
            }
            
            // Multiplayer flow - show lobby settings panel
            ConsoleSystem.Log($"[MainMenu] Multiplayer flow - party has {currentParty.Members.Count} members, leader: {isLeader}", ConsoleChannel.UI);
            
                
            // Initialize networking
            if (isLeader)
            {
                InitializeNetworkingAsLeader(partyService);
                
                // Signal all party members to show lobby UI via Steam lobby data
                var steamLobbyId = partyService.GetCurrentSteamLobbyId();
                if (steamLobbyId != 0 && PlatformService.IsSteamInitialized)
                {
                    try
                    {
                        Steam.SetLobbyData(steamLobbyId, "game_starting", "true");
                        Steam.SetLobbyData(steamLobbyId, "game_leader", localPlayerId);
                        Steam.SetLobbyData(steamLobbyId, "game_lobby_id", currentParty.PartyId);
                        ConsoleSystem.Log("[MainMenu] Notified party members that game is starting", ConsoleChannel.UI);
                    }
                    catch (Exception ex)
                    {
                        ConsoleSystem.LogWarn($"[MainMenu] Failed to notify party members: {ex.Message}", ConsoleChannel.UI);
                    }
                }
            }
            else
            {
                // Non-leader: connect to the host
                InitializeNetworkingAsClient(partyService);
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[MainMenu] Failed to start unified game flow: {ex.Message}", ConsoleChannel.UI);
            ShowErrorDialog("Game Start Failed", $"Failed to start game: {ex.Message}");
        }
    }
    
    private void InitializeNetworkingAsClient(PartyService partyService)
    {
        try
        {
            var networkService = GetNodeOrNull<NetworkService>("/root/NetworkService");
            if (networkService == null) return;
            
            if (!PlatformService.IsSteamInitialized)
            {
                ConsoleSystem.LogWarn("[MainMenu] Steam not initialized, cannot connect", ConsoleChannel.Network);
                return;
            }
            
            // Get the party leader (who is the host)
            var currentParty = partyService.GetCurrentPlayerParty();
            if (currentParty == null) return;
            
            var leaderSteamId = currentParty.LeaderPlayerId;
            if (string.IsNullOrEmpty(leaderSteamId))
            {
                ConsoleSystem.LogErr("[MainMenu] Party has no leader, cannot connect", ConsoleChannel.Network);
                return;
            }
            
            // Disconnect any existing session
            if (networkService.Mode != NetworkMode.None)
            {
                networkService.Disconnect();
            }
            
            // Switch to Steam backend using reflection (same pattern as leader)
            try
            {
                var reflectedConfig = networkService.GetType().GetField("_config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (reflectedConfig != null)
                {
                    var config = reflectedConfig.GetValue(networkService) as NetworkConfig;
                    if (config != null && config.Backend != NetworkBackend.Steam)
                    {
                        config.Backend = NetworkBackend.Steam;
                        var initMethod = networkService.GetType().GetMethod("InitializeAdapter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        initMethod?.Invoke(networkService, null);
                        ConsoleSystem.Log("[MainMenu] Switched to Steam networking backend (client)", ConsoleChannel.Network);
                    }
                }
            }
            catch (Exception ex)
            {
                ConsoleSystem.LogWarn($"[MainMenu] Failed to switch to Steam backend: {ex.Message}", ConsoleChannel.Network);
            }
            
            // Connect to the host via Steam
            ConsoleSystem.Log($"[MainMenu] Connecting to party leader {leaderSteamId} via Steam P2P", ConsoleChannel.Network);
            networkService.ConnectToServer(leaderSteamId, 0);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[MainMenu] Failed to connect to host: {ex.Message}", ConsoleChannel.Network);
        }
    }
    
    private void InitializeNetworkingAsLeader(PartyService partyService)
    {
        try
        {
            var networkService = GetNodeOrNull<NetworkService>("/root/NetworkService");
            if (networkService == null) return;
            
            // Ensure we're using Steam backend
            if (!PlatformService.IsSteamInitialized)
            {
                ConsoleSystem.LogWarn("[MainMenu] Steam not initialized, using offline mode", ConsoleChannel.Network);
                return;
            }
            
            // Disconnect any existing session
            if (networkService.Mode != NetworkMode.None)
            {
                networkService.Disconnect();
            }
            
            // Configure to reuse the party's Steam lobby
            var steamLobbyId = partyService.GetCurrentSteamLobbyId();
            if (steamLobbyId != 0)
            {
                ConsoleSystem.Log($"[MainMenu] Reusing party Steam lobby {steamLobbyId} for networking", ConsoleChannel.Network);
                networkService.ConfigureSteamLobbyReuse(steamLobbyId);
            }
            
            // Start server (will reuse existing lobby)
            networkService.StartServer(0);
            ConsoleSystem.Log("[MainMenu] Started network server as party leader", ConsoleChannel.Network);

			// Broadcast game launch to party via Steam lobby data and start the game locally
			try
			{
				var scenePath = "res://scenes/dev/dev.tscn";
				var localPlayerId = partyService.GetLocalPlayerId();
				var lobbyId = partyService.GetCurrentSteamLobbyId();
				if (lobbyId != 0 && PlatformService.IsSteamInitialized)
				{
					Steam.SetLobbyData(lobbyId, "game_launched", "true");
					Steam.SetLobbyData(lobbyId, "game_scene_path", scenePath);
					Steam.SetLobbyData(lobbyId, "game_leader", localPlayerId);
					ConsoleSystem.Log($"[MainMenu] Set Steam lobby game_launched=true, scene={scenePath}", ConsoleChannel.Network);
				}
				// Server triggers scene load and RPC to clients
				GameEvent.DispatchGlobal(new NewGameStartedEvent(scenePath));
				ConsoleSystem.Log("[MainMenu] Dispatched NewGameStartedEvent for multiplayer start", ConsoleChannel.Network);
			}
			catch (System.Exception ex)
			{
				ConsoleSystem.LogWarn($"[MainMenu] Failed to broadcast game start: {ex.Message}", ConsoleChannel.Network);
			}
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[MainMenu] Failed to initialize networking: {ex.Message}", ConsoleChannel.Network);
        }
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
        // New flow: check if we're in a party and trigger the unified flow
        var partyService = GetNodeOrNull<PartyService>("/root/PartyService");
        if (partyService != null)
        {
            var currentParty = partyService.GetCurrentPlayerParty();
            var localPlayerId = partyService.GetLocalPlayerId();
            bool isLeader = currentParty?.LeaderPlayerId == localPlayerId;
            
            if (currentParty != null && currentParty.Members.Count > 1)
            {
                ConsoleSystem.Log($"[MainMenu] Showing lobby screen for party member (leader: {isLeader})", ConsoleChannel.UI);
                StartUnifiedGameFlow(isLeader);
                return;
            }
        }
    }

    public void OnGameEvent(DisplaySettingsEvent eventArgs)
    {
        if (_isInitialized)
            ShowSettings();
    }

	public void OnGameEvent(LobbyCreatedEvent eventArgs)
	{
		// When a lobby is created, this is handled by the Steam lobby data update callback
		// No need to manually navigate here as the leader sends navigation messages
		// This event is mainly for logging and debugging purposes
		var partyService = GetNodeOrNull<PartyService>("/root/PartyService");
		if (partyService != null)
		{
			var localPlayerId = partyService.GetLocalPlayerId();
			if (localPlayerId == eventArgs.LeaderPlayerId)
			{
				ConsoleSystem.Log($"[MainMenu] Lobby created by local player {localPlayerId}", ConsoleChannel.UI);
			}
			else
			{
				ConsoleSystem.Log($"[MainMenu] Lobby created by leader {eventArgs.LeaderPlayerId}, waiting for navigation message", ConsoleChannel.UI);
			}
		}
	}

    public override void _ExitTree()
    {
        if (_settings != null)
            _settings.QueueFree();
    }
}