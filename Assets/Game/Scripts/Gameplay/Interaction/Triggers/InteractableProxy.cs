using System.Linq;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Delegates interaction to another IInteractable component on a different GameObject.
    /// Useful for when you want to have an interactable that is not on the same GameObject as the collider that detects the interaction.
    ///
    /// Carries no netcode, and must not grow any. It is a redirect: it does nothing but hand the
    /// same press to the same interactor, on the same machine, one GameObject further away. The
    /// target owns its own replication — that is the IInteractable contract, and it is why
    /// DoorInteraction reached through a proxy behaves identically to one reached directly. A gate
    /// here would be a second, invisible authority test in front of the target's own, and the two
    /// would eventually disagree.
    /// </summary>
    public class InteractableProxy : MonoBehaviour, IInteractable
    {
        [SerializeField] Transform target;
        private IInteractable targetInteractable;

        private void Awake()
        {
            if (target == null)
            {
                Debug.LogWarning($"[InteractableProxy] target not assigned on {name}, searching children.", this);
                foreach (var c in GetComponentsInChildren<IInteractable>(true))
                {
                    if (c is not InteractableProxy)
                    {
                        targetInteractable = c;
                        return;
                    }
                }
                return;
            }
            targetInteractable = target.GetComponent<IInteractable>();
        }

        public bool CanInteract()
        {
            return targetInteractable != null && targetInteractable.CanInteract();
        }

        public void Interact(Interactor interactor)
        {
            targetInteractable?.Interact(interactor);
        }
    }
}
