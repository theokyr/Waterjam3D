using Waterjam.Events;
using Godot;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Domain;

/// <summary>
/// Represents a character entity in the game, extending from the base Entity class.
/// This class can receive damage events and manage its health accordingly.
/// </summary>
public abstract partial class CharacterEntity : Entity, IGameEventHandler<CharacterDamagedEvent>
{
    public CharacterBody3D CharacterBody { get; protected set; }

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
}