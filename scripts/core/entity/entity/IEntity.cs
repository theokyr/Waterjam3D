namespace Waterjam.Domain;

public interface IEntity
{
    public float Health { get; }

    public float MaxHealth { get; }

    public void TakeDamage(float damage);

    public void Die(Entity killer = null);
}