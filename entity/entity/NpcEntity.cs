using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using WorldGame.Domain.Actions;
using WorldGame.Domain.States;
using WorldGame.Game.Systems.Dialogue;
using WorldGame.Domain.Scripting;
using WorldGame.Events;
using WorldGame.Player;
using WorldGame.Game.Systems.Console;
using WorldGame.Domain.Save;
using WorldGame.Game.Save;
using Godot.Collections;

namespace WorldGame.Domain;

public partial class NpcEntity : CharacterEntity, IInteractable, ISaveable
{
    public enum NpcType
    {
        Generic,
        Pedestrian,
        Vendor,
        Quest,
        Enemy
    }

    [Export]
    public NpcType Type { get; set; } = NpcType.Generic;

    [Export]
    public string DisplayName { get; set; } = "NPC";

    [Export]
    public Vector3 HomePosition { get; set; }

    [Export]
    public float MovementSpeed { get; set; } = 3.0f;

    [Export]
    public float ArrivalThreshold { get; set; } = 0.5f;

    [Export]
    public string DefaultDialogueId { get; set; }

    [Export]
    public float DespawnDistance { get; set; } = 15.0f;

    [Export]
    public float RotationSpeed { get; set; } = 5.0f;

    [Export]
    public bool IsPendingCleanup { get; private set; } = false;

    public NavigationAgent3D NavigationAgent { get; private set; }
    public Vector3? CurrentNavigationTarget { get; set; }
    protected Queue<IAction> ActionQueue { get; private set; } = new();
    protected IAction CurrentAction { get; private set; }
    private bool isNavigating;
    private double debugTimer = 0;
    private const double DEBUG_INTERVAL = 1.0;

    private DialogueSystem dialogueSystem;

    protected bool shouldCheckDistance;
    protected PlayerEntity playerEntity;

    private Vector3? targetRotation;

    public bool CanInteract => !string.IsNullOrEmpty(DefaultDialogueId) && !IsInState("Talking"); // Prevent interacting while already talking
    public string InteractionPrompt => $"{DisplayName} - Press E to talk";

    [Signal]
    public delegate void NavigationFinishedEventHandler();

    public override void _Ready()
    {
        base._Ready(); // This ensures CharacterEntity._Ready runs, including StateMachine initialization

        AddToGroup("Interactible");

        // Correct path due to scene restructure: NavigationAgent is now under CharacterBody3D
        NavigationAgent = GetNodeOrNull<NavigationAgent3D>("CharacterBody3D/NavigationAgent3D");

        if (NavigationAgent == null)
        {
            ConsoleSystem.LogErr($"[{DisplayName}] NavigationAgent3D node not found at path 'CharacterBody3D/NavigationAgent3D'!", ConsoleChannel.Npc);
        }
        else
        {
            NavigationAgent.PathDesiredDistance = 0.5f;
            NavigationAgent.TargetDesiredDistance = 0.5f;
            NavigationAgent.PathMaxDistance = 3.0f;
            NavigationAgent.AvoidanceEnabled = true;
            NavigationAgent.MaxSpeed = MovementSpeed;

            NavigationAgent.VelocityComputed += OnVelocityComputed;
            NavigationAgent.TargetReached += OnTargetReached;
            NavigationAgent.PathChanged += OnPathChanged;

            ConsoleSystem.Log($"[{DisplayName}] NavigationAgent configured.", ConsoleChannel.Npc);
        }

        // CharacterBody is now handled by the base CharacterEntity class
        if (CharacterBody == null)
            ConsoleSystem.LogErr($"[{DisplayName}] CharacterBody3D not found by base class, navigation will be disabled", ConsoleChannel.Npc);

        dialogueSystem = GetNode<DialogueSystem>("/root/GameSystems/DialogueSystem");
        if (dialogueSystem == null)
            ConsoleSystem.LogErr($"[{DisplayName}] DialogueSystem not found at /root/GameSystems/DialogueSystem", ConsoleChannel.Npc);

        // State machine should be initialized by the base class
        if (StateMachine == null)
        {
            ConsoleSystem.LogErr($"[{DisplayName}] StateMachine node not found or not initialized by base class", ConsoleChannel.Npc);
        }
        else
        {
            // Ensure Idle state is entered if no other state is active
            if (StateMachine.ActiveStates.Count == 0 && StateMachine.GetState("Idle") != null)
            {
                ConsoleSystem.Log($"[{DisplayName}] No active state, entering Idle state", ConsoleChannel.Npc);
                StateMachine.EnterState("Idle");
            }
            /* // Commented out potentially premature warning
            else if (StateMachine.ActiveStates.Count == 0)
            {
                ConsoleSystem.LogWarn($"[{DisplayName}] No active state and Idle state not registered yet.", ConsoleChannel.Npc); // Reverted to LogWarn
            }
            */
        }

        // Add distance check timer (defer add to avoid parent-busy errors)
        var distanceCheckTimer = new Timer
        {
            WaitTime = 1.0,
            OneShot = false,
            Autostart = true,
            ProcessCallback = Timer.TimerProcessCallback.Idle
        };
        distanceCheckTimer.Timeout += CheckDistanceToPlayer;
        CallDeferred(Node.MethodName.AddChild, distanceCheckTimer);
    }

    public bool Interact(Entity interactor)
    {
        if (!CanInteract)
        {
            ConsoleSystem.LogErr($"[{DisplayName}] Can't interact! Dialogue ID: {DefaultDialogueId}, In Talking State: {IsInState("Talking")}", ConsoleChannel.Npc);
            return false;
        }

        ConsoleSystem.Log($"[{DisplayName}] Starting interaction with {interactor.Name}", ConsoleChannel.Npc);
        StartDialogue(interactor);

        // Enter Talking state to prevent movement
        EnterState("Talking");

        return true;
    }

    public async void StartDialogue(Entity player)
    {
        try
        {
            if (string.IsNullOrEmpty(DefaultDialogueId))
            {
                ConsoleSystem.LogErr($"[{DisplayName}] No default dialogue set", ConsoleChannel.Npc);
                return;
            }

            if (dialogueSystem == null)
            {
                ConsoleSystem.LogErr($"[{DisplayName}] DialogueSystem reference is null", ConsoleChannel.Npc);
                return;
            }

            ConsoleSystem.Log($"[{DisplayName}] Starting dialogue '{DefaultDialogueId}' with player", ConsoleChannel.Npc);
            var startNode = await dialogueSystem.StartDialogue(DefaultDialogueId, this, player);

            if (startNode == null)
                ConsoleSystem.LogErr($"[{DisplayName}] Failed to start dialogue: {DefaultDialogueId}", ConsoleChannel.Npc);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[{DisplayName}] Error starting dialogue: {ex.Message}", ConsoleChannel.Npc);
            ConsoleSystem.LogErr($"Stack trace: {ex.StackTrace}", ConsoleChannel.Npc);
        }
    }

    private void OnTargetReached()
    {
        ConsoleSystem.Log($"[{DisplayName}] NavigationAgent target reached", ConsoleChannel.Npc);
        HandleNavigationCompleted();
    }

    private void OnPathChanged()
    {
        // ConsoleSystem.Log($"[{DisplayName}] Path changed", ConsoleChannel.Npc);
    }

    public IEnumerable<IAction> GetAvailableActions()
    {
        yield return new WalkToAction(HomePosition);
    }

    public override bool CanExecuteAction(IAction action)
    {
        return action.ActionId switch
        {
            ActionType.WalkTo => NavigationAgent != null && CharacterBody != null,
            ActionType.Interact => !string.IsNullOrEmpty(DefaultDialogueId),
            _ => false // Default: cannot execute unknown actions
        };
    }

    public void QueueAction(IAction action)
    {
        if (CanExecuteAction(action)) // Use the overridden CanExecuteAction
        {
            ActionQueue.Enqueue(action);
            ConsoleSystem.Log($"[{DisplayName}] Queued action: {action.ActionId}", ConsoleChannel.Npc);
        }
        else
        {
            ConsoleSystem.LogWarn($"[{DisplayName}] Cannot execute action: {action.ActionId}", ConsoleChannel.Npc); // Reverted to LogWarn
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta); // Process base Entity and CharacterEntity logic

        if (CurrentAction == null && ActionQueue.TryDequeue(out var nextAction))
        {
            CurrentAction = nextAction;
            isNavigating = CurrentAction.ActionId == ActionType.WalkTo;
            if (isNavigating && NavigationAgent != null)
            {
                // Execute the WalkToAction which sets the target position
                CurrentAction.Execute(this);
                ConsoleSystem.Log($"[{DisplayName}] Started navigation action to: {NavigationAgent.TargetPosition}", ConsoleChannel.Npc);
            }
            else if (!isNavigating)
            {
                CurrentAction.Execute(this); // Execute non-navigation actions immediately
                CurrentAction = null; // Non-navigation actions are instant for now
                ConsoleSystem.Log($"[{DisplayName}] Executed non-navigation action: {nextAction.ActionId}", ConsoleChannel.Npc);
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta); // Process base Entity and CharacterEntity physics

        // Handle rotation if we have a target
        if (targetRotation.HasValue)
        {
            var currentRot = Rotation;
            var targetRot = targetRotation.Value;

            // Interpolate rotation
            var newRotY = Mathf.LerpAngle(currentRot.Y, targetRot.Y, (float)delta * RotationSpeed);
            Rotation = new Vector3(currentRot.X, newRotY, currentRot.Z);

            // Clear target if we're close enough
            if (Mathf.Abs(Mathf.AngleDifference(newRotY, targetRot.Y)) < 0.01f) targetRotation = null;
        }

        // Navigation agent handles movement via OnVelocityComputed, so no manual movement here.
        // We just need to ensure MoveAndSlide is called if using CharacterBody3D.
        if (CharacterBody != null)
        {
            // Apply gravity if not handled by the agent or state
            if (!CharacterBody.IsOnFloor() && !isNavigating) // Only apply gravity if not navigating (agent handles Y velocity)
            {
                var currentVelocity = CharacterBody.Velocity;
                currentVelocity.Y -= 9.8f * (float)delta; // Basic gravity
                CharacterBody.Velocity = currentVelocity;
            }

            CharacterBody.MoveAndSlide();

            // Debug Log
            // ConsoleSystem.Log($"[{DisplayName}] PhysicsProcess - Velocity: {CharacterBody.Velocity}, IsNavigating: {isNavigating}", ConsoleChannel.Debug);
        }
    }

    private void HandleNavigationCompleted()
    {
        if (CharacterBody == null || NavigationAgent == null)
        {
            ConsoleSystem.LogErr($"[{DisplayName}] Cannot complete navigation - components are null", ConsoleChannel.Npc);
            isNavigating = false;
            CurrentAction = null;
            return;
        }

        ConsoleSystem.Log($"[{DisplayName}] Navigation completed!", ConsoleChannel.Npc);
        ConsoleSystem.Log($"[{DisplayName}] Final Position: {CharacterBody.GlobalPosition}", ConsoleChannel.Npc);
        ConsoleSystem.Log($"[{DisplayName}] Target Position: {NavigationAgent.TargetPosition}", ConsoleChannel.Npc);

        CharacterBody.Velocity = Vector3.Zero; // Stop movement
        isNavigating = false;
        CurrentAction = null;
        EmitSignal(SignalName.NavigationFinished);

        // Optionally transition to Idle state after navigation
        EnterState("Idle");
    }

    private void OnVelocityComputed(Vector3 safeVelocity)
    {
        if (CharacterBody == null)
            // ConsoleSystem.LogErr($"[{DisplayName}] Cannot compute velocity - CharacterBody3D is null", ConsoleChannel.Npc);
            return;

        if (!isNavigating) return; // Only apply agent velocity when navigating

        // Debug Log
        ConsoleSystem.Log($"[{DisplayName}] OnVelocityComputed - SafeVelocity: {safeVelocity}", ConsoleChannel.Debug);

        CharacterBody.Velocity = safeVelocity;
        // MoveAndSlide is called in _PhysicsProcess
    }

    public void EnableDistanceCleanup(PlayerEntity player)
    {
        shouldCheckDistance = true;
        playerEntity = player;
        IsPendingCleanup = true;
        ConsoleSystem.Log($"[{DisplayName}] Distance-based cleanup enabled", ConsoleChannel.Npc);
    }

    public void EnableDistanceCleanup(PlayerEntity player, float customDespawnDistance)
    {
        shouldCheckDistance = true;
        playerEntity = player;
        IsPendingCleanup = true;
        if (customDespawnDistance > 0) DespawnDistance = customDespawnDistance;
        ConsoleSystem.Log($"[{DisplayName}] Distance-based cleanup enabled with distance: {DespawnDistance}m", ConsoleChannel.Npc);
    }

    private void CheckDistanceToPlayer()
    {
        if (!shouldCheckDistance || playerEntity == null || !IsInstanceValid(playerEntity))
            return;

        var distance = GlobalPosition.DistanceTo(playerEntity.GlobalPosition);
        if (distance > DespawnDistance)
        {
            ConsoleSystem.Log($"[{DisplayName}] Too far from player ({distance:F1}m), despawning", ConsoleChannel.Npc);
            QueueFree(); // QueueFree handles cleanup and removal from tree
        }
    }

    // Implement ISaveable interface
    public override System.Collections.Generic.Dictionary<string, object> Save()
    {
        var data = base.Save(); // Gets Health, MaxHealth, Position, Rotation from Entity
        data["Type"] = (int)Type;
        data["DisplayName"] = DisplayName;
        data["DefaultDialogueId"] = DefaultDialogueId;
        data["MovementSpeed"] = MovementSpeed;
        data["HomePosition"] = HomePosition;
        data["IsNavigating"] = isNavigating;
        data["IsPendingCleanup"] = IsPendingCleanup;
        data["shouldCheckDistance"] = shouldCheckDistance;

        // Save StateMachine state
        if (StateMachine != null)
        {
            var activeStateIds = StateMachine.ActiveStates.Select(s => s.Id).ToList();
            data["ActiveStates"] = activeStateIds;
        }

        // If navigating, save the target position
        if (isNavigating && NavigationAgent != null) data["TargetPosition"] = NavigationAgent.TargetPosition;

        return data;
    }

    public override void Load(System.Collections.Generic.Dictionary<string, object> data)
    {
        base.Load(data); // Loads Health, MaxHealth, Position, Rotation from Entity

        if (data.TryGetValue("Type", out var type))
            Type = (NpcType)(int)Convert.ToInt64(type); // JSON numbers often deserialize as long
        if (data.TryGetValue("DisplayName", out var displayName))
            DisplayName = (string)displayName;
        if (data.TryGetValue("DefaultDialogueId", out var dialogueId))
            DefaultDialogueId = (string)dialogueId;
        if (data.TryGetValue("MovementSpeed", out var movementSpeed))
            MovementSpeed = Convert.ToSingle(movementSpeed);
        if (data.TryGetValue("HomePosition", out var homePosition))
            HomePosition = JsonHelper.ToVector3((string)homePosition); // Assuming Vector3 is saved as string "(x, y, z)"
        if (data.TryGetValue("IsNavigating", out var isNav))
            isNavigating = (bool)isNav;
        if (data.TryGetValue("IsPendingCleanup", out var cleanup))
            IsPendingCleanup = (bool)cleanup;
        if (data.TryGetValue("shouldCheckDistance", out var check))
            shouldCheckDistance = (bool)check;

        // Restore StateMachine state - needs to happen after states are registered
        if (data.TryGetValue("ActiveStates", out var activeStatesObj) && activeStatesObj is List<object> activeStateIdsRaw)
        {
            var activeStateIds = activeStateIdsRaw.Cast<string>().ToList();
            // Defer state restoration until states are registered
            var godotArray = new Array<string>(activeStateIds);
            CallDeferred(nameof(RestoreStates), godotArray);
        }

        // If we were navigating, restore the target position
        if (isNavigating && data.TryGetValue("TargetPosition", out var targetPosObj) && targetPosObj is string targetPos)
        {
            var targetVector = JsonHelper.ToVector3(targetPos);
            // Use CallDeferred to ensure NavigationAgent is ready
            CallDeferred(nameof(SetNavigationTargetDeferred), targetVector);
        }
    }

    private void SetNavigationTargetDeferred(Vector3 targetPosition)
    {
        if (NavigationAgent != null)
        {
            NavigationAgent.TargetPosition = targetPosition;
            ConsoleSystem.Log($"[{DisplayName}] Restored navigation target to {targetPosition}", ConsoleChannel.Npc);
        }
        else
        {
            ConsoleSystem.LogErr($"[{DisplayName}] Failed to restore navigation target - NavigationAgent is null", ConsoleChannel.Npc);
        }
    }

    private void RestoreStates(Array<string> stateIds)
    {
        if (StateMachine == null)
        {
            ConsoleSystem.LogErr($"[{DisplayName}] Cannot restore states - StateMachine is null", ConsoleChannel.Npc);
            return;
        }

        // Exit any current states first
        foreach (var activeState in StateMachine.ActiveStates.ToList()) StateMachine.ExitState(activeState.Id);

        // Enter the saved states
        foreach (var stateId in stateIds)
            if (!StateMachine.EnterState(stateId))
                ConsoleSystem.LogErr($"[{DisplayName}] Failed to restore state: {stateId}", ConsoleChannel.Npc);

        ConsoleSystem.Log($"[{DisplayName}] Restored states: {string.Join(", ", stateIds)}", ConsoleChannel.Npc);
    }

    public void SetFacing(Vector3 direction)
    {
        if (direction != Vector3.Zero) targetRotation = new Vector3(0, Mathf.Atan2(direction.X, direction.Z), 0);
    }

    public override void _ExitTree()
    {
        // Unsubscribe from NavigationAgent signals to prevent errors after freeing
        if (NavigationAgent != null)
        {
            if (NavigationAgent.IsConnected(NavigationAgent3D.SignalName.VelocityComputed, Callable.From<Vector3>(OnVelocityComputed)))
                NavigationAgent.VelocityComputed -= OnVelocityComputed;
            if (NavigationAgent.IsConnected(NavigationAgent3D.SignalName.TargetReached, Callable.From(OnTargetReached)))
                NavigationAgent.TargetReached -= OnTargetReached;
            if (NavigationAgent.IsConnected(NavigationAgent3D.SignalName.PathChanged, Callable.From(OnPathChanged)))
                NavigationAgent.PathChanged -= OnPathChanged;
        }

        // Clean up dialogue references if DialogueSystem exists
        if (dialogueSystem != null && IsInstanceValid(dialogueSystem))
            if (dialogueSystem.HasMethod("CleanupDialoguesForEntity"))
            {
                dialogueSystem.Call("CleanupDialoguesForEntity", this);
                ConsoleSystem.Log($"[{DisplayName}] Cleaned up dialogue references", ConsoleChannel.Npc);
            }

        base._ExitTree(); // Call base class cleanup
    }
}

// Helper class for JSON deserialization
public static class JsonHelper
{
    public static Vector3 ToVector3(string vectorString)
    {
        try
        {
            vectorString = vectorString.Trim('(', ')');
            var parts = vectorString.Split(',');
            if (parts.Length == 3)
            {
                var x = float.Parse(parts[0].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                var y = float.Parse(parts[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                var z = float.Parse(parts[2].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                return new Vector3(x, y, z);
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Error parsing Vector3 string '{vectorString}': {ex.Message}", ConsoleChannel.Error);
        }

        return Vector3.Zero;
    }
}