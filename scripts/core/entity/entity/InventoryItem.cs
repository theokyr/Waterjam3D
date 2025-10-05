using Godot;

namespace Waterjam.Domain;

public class InventoryItem
{
    public Entity ItemEntity { get; }
    public int Quantity { get; private set; }
    public IPickuppable PickupComponent => ItemEntity as IPickuppable;

    public InventoryItem(Entity item, int quantity = 1)
    {
        ItemEntity = item;
        Quantity = quantity;
    }

    public bool CanAddToStack(int amount)
    {
        return Quantity + amount <= PickupComponent.MaxStackSize;
    }

    public void AddToStack(int amount)
    {
        Quantity = Mathf.Min(Quantity + amount, PickupComponent.MaxStackSize);
    }

    public void RemoveFromStack(int amount)
    {
        Quantity = Mathf.Max(0, Quantity - amount);
    }
}