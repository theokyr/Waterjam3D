using Waterjam.Events;
using Godot;
using System;
using Waterjam.Core.Services;
using Waterjam.Core.Services.Modular;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Game.Services;

public partial class GameService : BaseService,
    IGameEventHandler<NewGameStartedEvent>,
    IGameEventHandler<QuitRequestedEvent>,
    IGameEventHandler<PlayerSpawnRequestEvent>
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

    public void OnGameEvent(QuitRequestedEvent eventArgs)
    {
        QuitGame();
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

    public void OnGameEvent(PlayerSpawnRequestEvent eventArgs)
    {
        try
        {
            if (eventArgs.ParentSceneNode == null || !IsInstanceValid(eventArgs.ParentSceneNode))
            {
                ConsoleSystem.LogErr("[GameService] Invalid parent scene node for player spawn", ConsoleChannel.Game);
                return;
            }

            var packed = ResourceLoader.Load<PackedScene>("res://scenes/Player.tscn");
            if (packed == null)
            {
                ConsoleSystem.LogErr("[GameService] Failed to load player scene at res://scenes/Player.tscn", ConsoleChannel.Game);
                return;
            }

            var playerNode = packed.Instantiate<Node>();
            if (playerNode == null)
            {
                ConsoleSystem.LogErr("[GameService] Failed to instantiate player scene", ConsoleChannel.Game);
                return;
            }

            // Add to the requested parent
            eventArgs.ParentSceneNode.AddChild(playerNode);

            // Apply optional transform if provided
            if (playerNode is Node3D node3D && eventArgs.Position.HasValue)
            {
                node3D.GlobalPosition = eventArgs.Position.Value;
            }

            GameEvent.DispatchGlobal(new PlayerSpawnedEvent(playerNode, eventArgs.ParentSceneNode));
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[GameService] Exception while spawning player: {ex.Message}", ConsoleChannel.Game);
        }
    }
}