// What the crosshair finds on a gear lying in one of a GearDoor's slots: on a peg, or on the ground in the pile.
// The interact button (RMB) picks it up, the same button that picks up every other loose thing.
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.World
{
    /// <summary>The pick-up box over one slot of a <see cref="GearDoor"/>. Made by the door; enabled while a gear lies there.</summary>
    [DisallowMultipleComponent]
    public sealed class GearDoorGrip : MonoBehaviour, IInteractable, IInteractionMoment, IInteractionReadout
    {
        private GearDoor door;
        private int slot;

        /// <summary>The trigger the crosshair finds, sized to whichever gear lies in the slot.</summary>
        public BoxCollider Collider { get; private set; }

        public void Bind(GearDoor owner, int index)
        {
            door = owner;
            slot = index;
            Collider = gameObject.AddComponent<BoxCollider>();
            Collider.isTrigger = true;
            Collider.enabled = false;
        }

        public bool CanInteract() => door != null && door.CanTake(slot);

        public void Interact(Interactor interactor) => door.RequestTake(slot, interactor);

        public CharacterMoment InteractionMoment => CharacterMoment.PickedUp;

        public string Label
        {
            get
            {
                int kind = door != null ? door.KindIn(slot) : -1;
                return kind < 0 ? string.Empty : door.KindAt(kind).item.itemName;
            }
        }

        // The resolver's own words, so the crosshair never names a different button from the one the press uses.
        public string Prompt => CanInteract() ? InteractionPromptResolver.RetrievePrompt : string.Empty;

        public float? Value01 => null;

        public string ValueText => string.Empty;
    }
}
