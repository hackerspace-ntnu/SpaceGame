/// <summary>
/// An interactable with a second, opposite action bound to Use (left click / gamepad west)
/// alongside the usual Interact (E).
///
/// For controls that run both ways — pay out rope vs. haul it in, raise vs. lower, open vs.
/// close — a single toggle button is the wrong shape: the player has to guess which way the
/// next press will go. Two buttons make the direction explicit.
///
/// Implement alongside <see cref="IInteractable"/>; <see cref="Interactor"/> raycasts for both
/// through the same path, so a secondary action reaches whatever the crosshair is already on.
/// </summary>
public interface ISecondaryInteractable
{
    /// <summary>Whether the secondary action is available right now.</summary>
    public bool CanSecondaryInteract();

    /// <summary>The opposite of <see cref="IInteractable.Interact"/>.</summary>
    public void SecondaryInteract(Interactor interactor);
}
