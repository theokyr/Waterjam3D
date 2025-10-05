using Godot;
using Waterjam.Core.Systems.Console;
using System.Collections.Generic;
using System.Linq;
using Waterjam.Events;
using Waterjam.Events;

namespace Waterjam.Core.Services;

public partial class UiService : BaseService,
    IGameEventHandler<GameInitializedEvent>,
    IGameEventHandler<PlayerSpawnedEvent>
{
    public static readonly string RootScenePath = "res://scenes/root.tscn";
    public static readonly string MainMenuScenePath = "res://scenes/ui/MainMenu.tscn";
    public static readonly string UiRootScenePath = "res://scenes/ui/UiRoot.tscn";

    private Control _uiRoot;
    private Control _loadingScreen;
    private Control _pauseMenu;
    private bool _mainMenuRequested;

    // Groups of UI elements for management
    private List<Control> _playerRelatedUI = new();
    private List<Control> _alwaysVisibleUI = new();

    public override void _Ready()
    {
        base._Ready();
        ConsoleSystem.Log("UiService Ready!", ConsoleChannel.UI);
    }

    public void OnGameEvent(GameInitializedEvent eventArgs)
    {
        if (eventArgs.Success)
        {
            // First, spawn the UiRoot
            SpawnUiRoot();

            // Then, dispatch event to show the main menu unless we're in a dev scene
            var current = GetTree().CurrentScene;
            var currentPath = current != null ? current.SceneFilePath : string.Empty;
            var isDevScene = !string.IsNullOrEmpty(currentPath) && currentPath.StartsWith("res://scenes/dev/", System.StringComparison.OrdinalIgnoreCase);
            if (!isDevScene)
            {
                if (_mainMenuRequested)
                {
                    return;
                }

                // If main menu already loaded (by name), skip to avoid duplicates
                var existsByName = GetTree().Root.GetChildren().Any(n => n.Name == "MainMenu");
                if (!existsByName)
                {
                    _mainMenuRequested = true;
                    ConsoleSystem.Log($"Dispatching SceneLoadEvent for {MainMenuScenePath}", ConsoleChannel.UI);
                    GameEvent.DispatchGlobal(new SceneLoadRequestedEvent(MainMenuScenePath, true));
                }
            }
            else
            {
                ConsoleSystem.Log("Skipping MainMenu load in dev scene", ConsoleChannel.UI);
            }
        }
        else
        {
            ConsoleSystem.LogErr("Game initialization failed. Unable to display main menu.", ConsoleChannel.UI);
        }
    }

    public void OnGameEvent(PlayerSpawnedEvent eventArgs)
    {
        // Show player-related UI elements when player is spawned
        ShowPlayerUI();
    }

    private void SpawnUiRoot()
    {
        if (_uiRoot != null && IsInstanceValid(_uiRoot))
        {
            ConsoleSystem.Log("UiRoot already exists, not spawning again.", ConsoleChannel.UI);
            return;
        }

        ConsoleSystem.Log("Spawning UiRoot", ConsoleChannel.UI);
        var uiRootScene = ResourceLoader.Load<PackedScene>(UiRootScenePath);
        _uiRoot = uiRootScene.Instantiate<Control>();
        _uiRoot.Name = "UiRoot";

        // Add to the root node using deferred call to avoid Godot errors
        var root = GetTree().Root;
        root.CallDeferred(Node.MethodName.AddChild, _uiRoot);

        // Use CallDeferred to ensure the node is properly added before we try to access its children
        CallDeferred(nameof(InitializeUiElements));
    }

    private void InitializeUiElements()
    {
        if (_uiRoot == null || !IsInstanceValid(_uiRoot))
        {
            ConsoleSystem.LogErr("UiRoot not valid during initialization", ConsoleChannel.UI);
            return;
        }

        // Get references to all UI elements
        // HUD may be injected dynamically based on settings
        _pauseMenu = _uiRoot.GetNodeOrNull<Control>("PauseMenu");
        _loadingScreen = _uiRoot.GetNodeOrNull<Control>("LoadingScreen");

        // Organize UI elements into functional groups
        _playerRelatedUI.Clear();
        _alwaysVisibleUI.Clear();

        // UI elements that should only be visible when player exists
        if (_pauseMenu != null) _playerRelatedUI.Add(_pauseMenu);

        // UI elements that should be always accessible
        if (_loadingScreen != null) _alwaysVisibleUI.Add(_loadingScreen);

        // Initially hide all player-related UI elements
        HidePlayerUI();

        // Ensure always-visible UI elements are properly configured
        foreach (var ui in _alwaysVisibleUI)
            if (ui != _loadingScreen) // Don't show loading screen by default
                ui.Visible = true;

        ConsoleSystem.Log("UiRoot initialized with all UI components", ConsoleChannel.UI);

        // Ensure correct HUD variant is present (will receive SettingsLoadedEvent on init)
        RequestSettings();
    }

    private void RequestSettings()
    {
        GameEvent.DispatchGlobal(new SettingsRequestedEvent());
    }

    private void ShowPlayerUI()
    {
        foreach (var ui in _playerRelatedUI)
            if (IsInstanceValid(ui))
            {
                // Special handling for screens that should remain hidden until explicitly shown
                if (ui == _pauseMenu)
                    // These screens should not be automatically shown, only made available
                    continue;

                // Show other player UI components like HUD
                ui.Visible = true;
            }

        ConsoleSystem.Log("Player UI elements shown", ConsoleChannel.UI);
    }

    private void HidePlayerUI()
    {
        foreach (var ui in _playerRelatedUI)
            if (IsInstanceValid(ui))
                ui.Visible = false;

        ConsoleSystem.Log("Player UI elements hidden", ConsoleChannel.UI);
    }

    public void HideAllPlayerUI()
    {
        HidePlayerUI();
    }

    // Loading screen event handlers
    public void OnGameEvent(LoadingScreenShowEvent eventArgs)
    {
        if (_loadingScreen != null && IsInstanceValid(_loadingScreen))
        {
            _loadingScreen.Visible = true;
            if (_loadingScreen.HasMethod("SetProgress")) _loadingScreen.Call("SetProgress", eventArgs.InitialProgress, eventArgs.Message);
        }
        else
        {
            ConsoleSystem.LogErr("Cannot show loading screen - not found in UiRoot", ConsoleChannel.UI);
        }
    }

    public void OnGameEvent(LoadingScreenUpdateEvent eventArgs)
    {
        if (_loadingScreen != null && IsInstanceValid(_loadingScreen) && _loadingScreen.Visible)
            if (_loadingScreen.HasMethod("SetProgress"))
                _loadingScreen.Call("SetProgress", eventArgs.Progress, eventArgs.Message);
    }

    public void OnGameEvent(LoadingScreenHideEvent eventArgs)
    {
        if (_loadingScreen != null && IsInstanceValid(_loadingScreen))
        {
            if (_loadingScreen.HasMethod("HideScreen"))
                _loadingScreen.Call("HideScreen");
            else
                _loadingScreen.Visible = false;
        }
    }
}