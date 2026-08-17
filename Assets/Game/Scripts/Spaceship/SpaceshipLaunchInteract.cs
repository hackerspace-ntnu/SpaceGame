using UnityEngine;

/// <summary>
/// Interact script to trigger spaceship launch
/// Implements IInteractable for the player interaction system
/// Attach to a GameObject with a Collider to create an interaction point
/// </summary>
public class SpaceshipLaunchInteract : MonoBehaviour, IInteractable
{
    [SerializeField] private SpaceshipManager targetSpaceship;
    [SerializeField] private bool hasBeenUsed = false;

    public bool CanInteract()
    {
        // Can only interact if spaceship is available and hasn't been used
        return targetSpaceship != null && !hasBeenUsed;
    }

    public void Interact(Interactor interactor)
    {
        if (targetSpaceship == null)
        {
            Debug.LogError("SpaceshipLaunchInteract: targetSpaceship not assigned!");
            return;
        }

        hasBeenUsed = true;
        targetSpaceship.BeginFlight();
        
        Debug.Log("Spaceship launched!");
        
        // Optional: Disable this interact point after use
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Reset the interaction (for testing or respawning)
    /// </summary>
    public void ResetInteraction()
    {
        hasBeenUsed = false;
        gameObject.SetActive(true);
    }
}
