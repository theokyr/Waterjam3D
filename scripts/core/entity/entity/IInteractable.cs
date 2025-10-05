using Godot;

namespace Waterjam.Domain;

public interface IInteractable
{
    /// <summary>
    /// Called when an entity interacts with this object
    /// </summary>
    /// <param name="interactor">The entity initiating the interaction</param>
    /// <returns>True if the interaction was successful</returns>
    bool Interact(Entity interactor);

    /// <summary>
    /// Gets whether this entity can currently be interacted with
    /// </summary>
    bool CanInteract { get; }

    /// <summary>
    /// Gets the interaction prompt text to display
    /// </summary>
    string InteractionPrompt { get; }
}