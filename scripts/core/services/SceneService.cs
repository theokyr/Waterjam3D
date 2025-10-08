using System.Collections.Generic;
using System.Threading.Tasks;
using Waterjam.Events;
using Godot;
using System.Linq;
using System;
using Waterjam.Core.Services;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Game.Services;

public partial class SceneService : BaseService,
    IGameEventHandler<SceneLoadRequestedEvent>,
    IGameEventHandler<SceneDestroyRequestedEvent>
{
    private readonly HashSet<string> PROTECTED_ROOTS = new()
    {
        "UiRoot",
        "GameSystems",
        "Root" // The main game root node
    };

    private Node _currentScene;
    private Dictionary<string, Node> _loadedScenes = new();

    public override void _Ready()
    {
        base._Ready();
        _currentScene = GetTree().CurrentScene;
    }

    public void OnGameEvent(SceneLoadRequestedEvent eventArgs)
    {
        // Prevent duplicate additive loads of UI scenes like MainMenu
        if (_loadedScenes.ContainsKey(eventArgs.ScenePath))
        {
            ConsoleSystem.LogErr($"Scene {eventArgs.ScenePath} already loaded", ConsoleChannel.System);
            return;
        }

        // If attempting to load MainMenu additively and a node with same name exists under root, skip
        if (string.Equals(eventArgs.ScenePath, UiService.MainMenuScenePath, StringComparison.OrdinalIgnoreCase))
        {
            var existing = GetTree().Root.GetChildren().FirstOrDefault(n => n.SceneFilePath == UiService.MainMenuScenePath);
            if (existing != null)
            {
                ConsoleSystem.LogWarn("MainMenu already present; skipping duplicate load", ConsoleChannel.UI);
                return;
            }
        }

        // Update loading screen using events
        GameEvent.DispatchGlobal(new LoadingScreenUpdateEvent(0.4f, $"Loading scene: {eventArgs.ScenePath}..."));

        // If loading the same scene that is already current, still notify so dependent systems can initialize/spawn
        if (!eventArgs.Additive && _currentScene != null && _currentScene.SceneFilePath == eventArgs.ScenePath)
        {
            ConsoleSystem.LogWarn($"Scene {eventArgs.ScenePath} already current; skipping load", ConsoleChannel.System);
            // Fire SceneLoadEvent + spawn flow anyway to keep systems consistent in dev scenes
            CallDeferred(nameof(NotifySceneLoaded), eventArgs.ScenePath, eventArgs.Additive);
            return;
        }

        var sceneToLoad = ResourceLoader.Load<PackedScene>(eventArgs.ScenePath)?.Instantiate();
        if (sceneToLoad == null)
        {
            ConsoleSystem.LogErr($"Scene {eventArgs.ScenePath} not found", ConsoleChannel.System);
            return;
        }

        var root = GetTree().Root;

        if (!eventArgs.Additive)
        {
            CallDeferred(nameof(DeferredSceneSwitch), root, sceneToLoad, eventArgs.ScenePath);
        }
        else
        {
            root.CallDeferred(Node.MethodName.AddChild, sceneToLoad);
            _loadedScenes[eventArgs.ScenePath] = sceneToLoad;
        }

        // Dispatch SceneLoadEvent to inform other systems that scene has loaded
        CallDeferred(nameof(NotifySceneLoaded), eventArgs.ScenePath, eventArgs.Additive);
    }

    private void NotifySceneLoaded(string scenePath, bool additive)
    {
        // Update loading progress using events
        GameEvent.DispatchGlobal(new LoadingScreenUpdateEvent(0.7f, "Initializing world..."));

        // Delay slightly to ensure scene is fully initialized
        GetTree().CreateTimer(0.1f).Timeout += () =>
        {
            GameEvent.DispatchGlobal(new SceneLoadEvent(scenePath, additive));

            // Get the currently loaded scene node
            var parentNode = additive && _loadedScenes.ContainsKey(scenePath) ? _loadedScenes[scenePath] : _currentScene;

            // Update loading screen and spawn player only for gameplay scenes (not main menu, root, or UI screens)
            var isUiScene = scenePath.StartsWith("res://scenes/ui/", StringComparison.OrdinalIgnoreCase);
            if (!isUiScene
                && !string.Equals(scenePath, UiService.MainMenuScenePath, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(scenePath, UiService.RootScenePath, StringComparison.OrdinalIgnoreCase))
            {
                if (parentNode != null && IsInstanceValid(parentNode) && parentNode.IsInsideTree())
                {
                    GameEvent.DispatchGlobal(new LoadingScreenUpdateEvent(0.9f, "Spawning player..."));
                    GameEvent.DispatchGlobal(new PlayerSpawnRequestEvent(parentNode));
                }
                else
                {
                    ConsoleSystem.LogWarn("[SceneService] Skipping player spawn: parent scene node invalid or not in tree", ConsoleChannel.Game);
                }
            }

            // Hide loading screen after a delay
            GetTree().CreateTimer(1.0f).Timeout += () => { GameEvent.DispatchGlobal(new LoadingScreenHideEvent()); };
        };
    }

    private void DeferredSceneSwitch(Node root, Node newScene, string scenePath)
    {
        // Clean up any previously tracked additive scenes (UI overlays loaded via SceneService)
        if (_loadedScenes.Count > 0)
        {
            foreach (var kv in _loadedScenes.ToList())
            {
                var scene = kv.Value;
                if (IsInstanceValid(scene))
                {
                    if (scene.GetParent() == root)
                        root.RemoveChild(scene);
                    scene.QueueFree();
                }
            }
            _loadedScenes.Clear();
        }

        if (_currentScene != null && _currentScene.IsInsideTree())
        {
            root.RemoveChild(_currentScene);
            _currentScene.QueueFree();
        }

        root.AddChild(newScene);
        GetTree().CurrentScene = newScene;
        _currentScene = newScene;
        _loadedScenes[scenePath] = newScene;
    }

    public async Task<Node> LoadSceneAsync(string scenePath, bool additive = false, bool forceReload = false)
    {
        // If we're forcing a reload, ensure current scene is unloaded first
        if (forceReload && !additive) await UnloadCurrentScene();

        var sceneToLoad = ResourceLoader.Load<PackedScene>(scenePath)?.Instantiate();
        if (sceneToLoad == null)
        {
            ConsoleSystem.LogErr($"[SceneService] Scene {scenePath} not found");
            return null;
        }

        var root = GetTree().Root;

        if (!additive)
        {
            // Add new scene first
            root.AddChild(sceneToLoad);
            GetTree().CurrentScene = sceneToLoad;
            _currentScene = sceneToLoad;
        }
        else
        {
            root.AddChild(sceneToLoad);
            _loadedScenes[scenePath] = sceneToLoad;
        }

        // Determine the parent node for the player
        var parentNodeForPlayer = additive ? sceneToLoad : _currentScene;

        // Give scene time to initialize
        await ToSignal(GetTree().CreateTimer(0.2f), "timeout");

        // Notify that the scene is loaded
        GameEvent.DispatchGlobal(new SceneLoadEvent(scenePath, additive));

        // Spawn player within the loaded scene only if not main menu or root
        if (!string.Equals(scenePath, UiService.MainMenuScenePath, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scenePath, UiService.RootScenePath, StringComparison.OrdinalIgnoreCase))
        {
            if (parentNodeForPlayer != null && IsInstanceValid(parentNodeForPlayer) && parentNodeForPlayer.IsInsideTree())
            {
                GameEvent.DispatchGlobal(new PlayerSpawnRequestEvent(parentNodeForPlayer));
            }
            else
            {
                ConsoleSystem.LogWarn("[SceneService] Skipping player spawn in LoadSceneAsync: parent scene node invalid or not in tree", ConsoleChannel.Game);
            }
        }

        return sceneToLoad;
    }

    public async Task UnloadCurrentScene()
    {
        if (_currentScene != null)
        {
            ConsoleSystem.Log("Unloading current scene", ConsoleChannel.Game);

            try
            {
                var tree = GetTree();
                if (tree == null)
                {
                    ConsoleSystem.LogErr("Scene tree is null during unload", ConsoleChannel.Game);
                    return;
                }

                // Remove the current scene instance only; do not touch autoloads or other root children
                // Defer removal of current scene to avoid 'parent busy' errors
                var root = tree.Root;
                if (_currentScene.IsInsideTree())
                    root.CallDeferred(Node.MethodName.RemoveChild, _currentScene);

                if (IsInstanceValid(_currentScene) && !_currentScene.IsQueuedForDeletion())
                    _currentScene.CallDeferred(Node.MethodName.QueueFree);

                // Wait a frame to ensure the scene is freed
                await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

                _currentScene = null;
            }
            catch (Exception ex)
            {
                ConsoleSystem.LogErr($"Error during scene unload: {ex.Message}", ConsoleChannel.Game);
                _currentScene = null;
            }
        }

        // Clean up any additive-loaded scenes we are tracking (but never autoloads)
        if (_loadedScenes.Count > 0)
        {
            foreach (var scene in _loadedScenes.Values.ToList())
            {
                if (IsInstanceValid(scene) && !scene.IsQueuedForDeletion())
                {
                    var root = GetTree().Root;
                    // Ensure the scene is detached before freeing
                    if (scene.GetParent() == root)
                        root.CallDeferred(Node.MethodName.RemoveChild, scene);
                    scene.CallDeferred(Node.MethodName.QueueFree);
                }
            }

            _loadedScenes.Clear();
        }

        // Do not sweep arbitrary root children here; autoloads and test harness live at root.
    }

    private bool IsInstanceValid(Node node)
    {
        try
        {
            return node != null && !node.IsQueuedForDeletion() && node.GetTree() != null;
        }
        catch
        {
            return false;
        }
    }

    public void OnGameEvent(SceneDestroyRequestedEvent eventArgs)
    {
        if (!_loadedScenes.ContainsKey(eventArgs.ScenePath)) return;

        var sceneToDestroy = _loadedScenes[eventArgs.ScenePath];
        sceneToDestroy.QueueFree();
        _loadedScenes.Remove(eventArgs.ScenePath);

        // Notify that the scene was destroyed
        GameEvent.DispatchGlobal(new SceneDestroyEvent(eventArgs.ScenePath));
    }
}