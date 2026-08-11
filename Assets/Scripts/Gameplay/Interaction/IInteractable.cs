/// <summary>
/// Interactables should always have a collider such that an Interactor can detect them using raycast 
/// </summary>
public interface IInteractable
{
    public bool CanInteract();
    public void Interact(Interactor interactor);
}