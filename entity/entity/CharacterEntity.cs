using System.Collections.Generic;
using WorldGame.Events;
using Godot;
using WorldGame.Domain.Actions;
using WorldGame.Domain.Save;
using WorldGame.Game.Save;
using WorldGame.Domain.States;
using WorldGame.Game.Systems.Console;

namespace WorldGame.Domain;

/// <summary>
/// Represents a character entity in the game, extending from the base Entity class.
/// This class can receive damage events and manage its health accordingly.
/// </summary>
public abstract partial class CharacterEntity : Entity, IGameEventHandler<CharacterDamagedEvent>, IActionExecutor, ISaveable
{
    public CharacterBody3D CharacterBody { get; protected set; }
    public StateMachineComponent StateMachine { get; private set; }

    public override void _Ready()
    {
        base._Ready();

        // Attempt to find the CharacterBody3D node by common name, then by type as fallback
        CharacterBody = GetNodeOrNull<CharacterBody3D>("CharacterBody3D");
        if (CharacterBody == null)
        {
            foreach (var child in GetChildren())
            {
                if (child is CharacterBody3D cb)
                {
                    CharacterBody = cb;
                    break;
                }
            }
        }

        if (CharacterBody == null)
            ConsoleSystem.LogErr($"[{Name ?? GetType().Name}] CharacterBody3D not found; navigation and physics will be disabled.", ConsoleChannel.Error);
        else
            ConsoleSystem.Log($"[{Name ?? GetType().Name}] CharacterBody3D found and assigned.", ConsoleChannel.Debug);

        // Add state machine component if not already present
        if (GetNodeOrNull<StateMachineComponent>("StateMachine") == null)
        {
            StateMachine = new StateMachineComponent();
            StateMachine.Name = "StateMachine";
            AddChild(StateMachine);
        }
        else
        {
            StateMachine = GetNode<StateMachineComponent>("StateMachine");
        }
    }

    /// <summary>
    /// Handles the CharacterDamagedEvent, applying damage to the character and triggering death if health reaches zero.
    /// </summary>
    /// <param name="eventArgs">The event arguments containing information about the damage dealt.</param>
    public void OnGameEvent(CharacterDamagedEvent eventArgs)
    {
        if (eventArgs.Victim != this) return;

        ConsoleSystem.Log($"[CharacterDamagedEvent] Attacker: {eventArgs.Attacker?.Name ?? "None"}, victim: {eventArgs.Victim.Name}, damage: {eventArgs.Damage}", ConsoleChannel.Entity);

        if (Health - eventArgs.Damage <= 0.0f)
        {
            ConsoleSystem.Log($"[CharacterDamagedEvent] '{eventArgs.Attacker?.Name ?? "None"}' killed '{eventArgs.Victim.Name}'!", ConsoleChannel.Entity);
            Health = 0.0f;
            Die(eventArgs.Attacker);
        }
        else
        {
            TakeDamage(eventArgs.Damage);
        }
    }

    public abstract bool CanExecuteAction(IAction action);

    public virtual bool TryExecuteAction(IAction action)
    {
        if (!action.CanExecute(this) || !CanExecuteAction(action))
            return false;

        action.Execute(this);
        return true;
    }

    /// <summary>
    /// Enters a state for this character
    /// </summary>
    /// <param name="stateId">The ID of the state to enter</param>
    /// <returns>True if the state was entered, false otherwise</returns>
    public bool EnterState(string stateId)
    {
        return StateMachine?.EnterState(stateId) ?? false;
    }

    /// <summary>
    /// Exits a state for this character
    /// </summary>
    /// <param name="stateId">The ID of the state to exit</param>
    /// <returns>True if the state was exited, false otherwise</returns>
    public bool ExitState(string stateId)
    {
        return StateMachine?.ExitState(stateId) ?? false;
    }

    /// <summary>
    /// Checks if a state is active for this character
    /// </summary>
    /// <param name="stateId">The ID of the state to check</param>
    /// <returns>True if the state is active, false otherwise</returns>
    public bool IsInState(string stateId)
    {
        return StateMachine?.IsStateActive(stateId) ?? false;
    }

    public override Dictionary<string, object> Save()
    {
        var data = base.Save();
        data["DisplayName"] = Name;
        data["IsNavigating"] = false; // Default value
        return data;
    }

    public override void Load(Dictionary<string, object> data)
    {
        base.Load(data);
        if (data.TryGetValue("DisplayName", out var displayName))
            Name = (string)displayName;
    }
}