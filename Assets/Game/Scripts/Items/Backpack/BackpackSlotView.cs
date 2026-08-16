using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// One item, sitting in one socket of an open pack, waiting to be looked at and taken.
    ///
    /// This is added by <see cref="BackpackObject"/> onto the display copy that
    /// <see cref="BackpackItemVisual"/> built — the same GameObject that carries the BoxCollider the
    /// interaction ray hits. That pairing is what makes the pick work: Interactor calls GetComponent
    /// on the collider it hit before it walks up to a parent, so an item in a pocket always answers
    /// for itself rather than handing the interaction to the pack behind it.
    /// </summary>
    public class BackpackSlotView : MonoBehaviour, IInteractable
    {
        private BackpackObject pack;
        private BackpackCompartment compartment;
        private int index = -1;

        public void Bind(BackpackObject owner, BackpackCompartment slotCompartment, int slotIndex)
        {
            pack = owner;
            compartment = slotCompartment;
            index = slotIndex;
        }

        public bool CanInteract()
        {
            // Strap items are visible on a worn pack too, but reaching one means the pack is off and
            // open — otherwise the crosshair would offer to unclip a bedroll off the player's own back.
            return pack != null && pack.IsOpen && index >= 0;
        }

        public void Interact(Interactor interactor)
        {
            if (!CanInteract() || interactor == null) return;

            // Same resolve PickupableItem uses, so a pack and a world pickup agree on what "the
            // player's inventory" means.
            IPlayerInventory hotbar = interactor.GetComponent<IPlayerInventory>();
            if (hotbar == null) return;

            if (pack.TryTakeToHotbar(compartment, index, hotbar)) return;

            // Deliberately not silent. A full hotbar and a broken interaction look identical from the
            // player's side, and this is the one refusal they will hit routinely.
            Debug.Log("Backpack: hotbar is full — drop or use something first.", this);
        }
    }
}
