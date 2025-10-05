namespace WorldGame.Domain;

/// <summary>
/// Defines the basic behavior for objects that can take damage.
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Applies damage to the object, reducing its health or similar property.
    /// </summary>
    /// <param name="damage">The amount of damage to apply.</param>
    void TakeDamage(float damage);

    /// <summary>
    /// Gets the current health of the object.
    /// </summary>
    /// <returns>The current health value.</returns>
    float GetHealth();

    /// <summary>
    /// Gets the maximum health of the object.
    /// </summary>
    /// <returns>The maximum health value.</returns>
    float GetMaxHealth();
}