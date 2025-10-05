using System;
using Godot;
using System.Collections.Generic;

#pragma warning disable CS0108

namespace Waterjam.Domain;

/// <summary>
/// Represents a generic entity within the game world. This class provides basic functionality
/// such as managing health and handling damage. It serves as a base class for other, more specific entities.
/// </summary>
public partial class Entity : Node3D, IDamageable, IEntity, IDisposable
{
    protected bool isDisposed;

    /// <summary>
    /// Current health value of the entity.
    /// </summary>
    [Export]
    public float Health { get; set; }

    /// <summary>
    /// Maximum health value of the entity.
    /// </summary>
    [Export]
    public float MaxHealth { get; set; }

    /// <summary>
    /// Maximum distance at which this entity can be interacted with
    /// </summary>
    [Export]
    public float InteractionRange { get; set; } = 2.0f;

    /// <summary>
    /// Applies damage to the entity, reducing its health by the given amount.
    /// </summary>
    /// <param name="damage">The amount of damage to apply.</param>
    public void TakeDamage(float damage)
    {
        // Implementation for taking damage (to be expanded in derived classes).
        return;
    }

    /// <summary>
    /// Gets the current health value of the entity.
    /// </summary>
    /// <returns>The current health value.</returns>
    public float GetHealth()
    {
        return Health;
    }

    /// <summary>
    /// Gets the maximum health value of the entity.
    /// </summary>
    /// <returns>The maximum health value.</returns>
    public float GetMaxHealth()
    {
        return MaxHealth;
    }

    /// <summary>
    /// Handles the entity's death logic. This method can be overridden in derived classes to provide specific behavior.
    /// </summary>
    /// <param name="killer">The entity responsible for killing this entity, if applicable.</param>
    public virtual void Die(Entity killer = null)
    {
        // Implementation for handling death (to be expanded in derived classes).
        return;
    }

    /// <summary>
    /// Checks if this entity is within interaction range of another entity
    /// </summary>
    public bool IsWithinRange(Entity other)
    {
        if (other == null) return false;

        // Calculate distance between entities
        var distance = GlobalPosition.DistanceTo(other.GlobalPosition);
        var inRange = distance <= InteractionRange;

        return inRange;
    }

    public override void _ExitTree()
    {
        Dispose();
        base._ExitTree();
    }

    public new void Dispose()
    {
        if (!isDisposed) isDisposed = true;
        // Add any entity-specific cleanup here
    }

    public override void _Ready()
    {
        base._Ready();
        AddToGroup("Entities");
    }

    public virtual Dictionary<string, object> Save()
    {
        return new Dictionary<string, object>
        {
            ["Health"] = Health,
            ["MaxHealth"] = MaxHealth,
            ["InteractionRange"] = InteractionRange,
            ["Position"] = GlobalPosition,
            ["Rotation"] = GlobalRotation
        };
    }

    public virtual void Load(Dictionary<string, object> data)
    {
        if (data.TryGetValue("Health", out var health))
            Health = (float)health;
        if (data.TryGetValue("MaxHealth", out var maxHealth))
            MaxHealth = (float)maxHealth;
        if (data.TryGetValue("InteractionRange", out var range))
            InteractionRange = (float)range;
        if (data.TryGetValue("Position", out var pos))
            GlobalPosition = (Vector3)pos;
        if (data.TryGetValue("Rotation", out var rot))
            GlobalRotation = (Vector3)rot;
    }
}