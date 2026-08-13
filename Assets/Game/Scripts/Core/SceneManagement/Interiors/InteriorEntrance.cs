using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Core
{
    /// <summary>
    /// Place on a door (or any interactable) in the exterior. On interact, sends the player to the
    /// configured interior scene via InteriorManager.
    ///
    /// Respects InteriorManager's post-exit lockout: when the player leaves an interior they are
    /// teleported back exactly where they entered — i.e. on top of this entrance. Without the
    /// lockout check a walk-in entrance would re-fire the same frame and yo-yo them straight back.
    /// </summary>
    public class InteriorEntrance : MonoBehaviour, IInteractable
    {
        [SerializeField] private InteriorScene targetInterior;

        public void Initialize(InteriorScene def) => targetInterior = def;

        public bool CanInteract() => targetInterior != null && InteriorManager.Instance != null;

        public void Interact(Interactor interactor)
        {
            if (interactor == null) return;
            var player = interactor.gameObject;

            // Just exited an interior and still standing in the entrance volume — ignore.
            if (InteriorManager.Instance.IsEntranceLockedOut(player)) return;

            InteriorManager.Instance.EnterInterior(player, targetInterior);
        }
    }
}
