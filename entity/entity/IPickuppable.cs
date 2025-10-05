namespace WorldGame.Domain;

public interface IPickuppable
{
    /// <summary>
    /// Called when an entity attempts to pick up this object
    /// </summary>
    /// <param name="collector">The entity attempting to pick up the item</param>
    /// <returns>True if the pickup was successful</returns>
    bool OnPickup(Entity collector);

    /// <summary>
    /// Gets whether this entity can currently be picked up
    /// </summary>
    bool CanPickup { get; }

    /// <summary>
    /// Gets the display name of the item
    /// </summary>
    string ItemName { get; }

    /// <summary>
    /// Gets the maximum stack size for this item type
    /// </summary>
    int MaxStackSize { get; }
}