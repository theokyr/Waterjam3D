using Waterjam.Events;
using Godot;
using System;
using Waterjam.Core.Services;
using Waterjam.Core.Services.Modular;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Game.Services;

public partial class GameService : BaseService,
    IGameEventHandler<NewGameStartedEvent>
{
    public const string RootScene = "res://scenes/root.tscn";

    public override void _Ready()
    {
        base._Ready();
        InitializeGame();
    }

    private void InitializeGame()
    {
        ConsoleSystem.Log("Let there be light!", ConsoleChannel.Game);
        GameEvent.DispatchGlobal(new GameInitializedEvent(true));

        var currentScene = GetTree().CurrentScene;
        if (currentScene != null && currentScene.SceneFilePath != "res://scenes/root.tscn")
        {
            ConsoleSystem.Log("Current scene is not root. Starting new game immediately.", ConsoleChannel.Game);
            StartNewGame();
        }
    }

    public async void StartNewGame()
    {
        ConsoleSystem.Log("Starting new game!", ConsoleChannel.Game);
        try
        {
            var registry = SystemRegistry.Instance;
            if (registry != null)
            {
                // Ensure core gameplay systems are running for New Game
                // await registry.LoadSystemAsync("script_engine");
            }
        }
        catch
        {
            /* best-effort */
        }

        GameEvent.DispatchGlobal(new NewGameStartedEvent());
    }

    public void QuitGame()
    {
        ConsoleSystem.Log("Quitting game!", ConsoleChannel.Game);
        GetTree().Quit();
    }

    public void OnGameEvent(NewGameStartedEvent eventArgs)
    {
        GameEvent.DispatchGlobal(new SceneLoadRequestedEvent(eventArgs.LevelScenePath));
    }

    public void OnGameEvent(PlayerSpawnedEvent eventArgs)
    {
        // Game is fully loaded and player is spawned
        ConsoleSystem.Log("Player spawned successfully - game ready!", ConsoleChannel.Game);
    }
}