// What the crosshair finds on the hoist's hook: with the reactor core in the hands, Interact hangs it on.
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.World
{
    /// <summary>A trigger round the hook block, riding with it. Made by the builder at metre scale.</summary>
    [DisallowMultipleComponent]
    public sealed class WorkshopCraneHook : MonoBehaviour, IInteractable, IInteractionMoment, IInteractionReadout
    {
        [SerializeField] private WorkshopCrane crane;

#if UNITY_EDITOR
        public void Author(WorkshopCrane hoist) => crane = hoist;
#endif

        public bool CanInteract() => crane != null && crane.IsDriven && !crane.IsLoaded && LocalPlayerHoldsLoad();

        public void Interact(Interactor interactor)
        {
            if (!CanInteract()) return;
            if (!crane.WithinHangReach(interactor.transform))
            {
                PlayerHints.Show("workshop-crane", "Bring the hook closer: stand right by it to hang the core on", 3f);
                return;
            }
            crane.RequestHang(interactor);
        }

        public CharacterMoment InteractionMoment => CharacterMoment.None;

        public string Label => "Hoist hook";

        public string Prompt => CanInteract() ? "RMB: hang the reactor core on the hook" : string.Empty;

        public float? Value01 => null;

        public string ValueText => string.Empty;

        /// <summary>The core is the item in this machine's player's hands.</summary>
        private bool LocalPlayerHoldsLoad()
        {
            Transform local = GameplayMenuScope.LocalPlayerTransform;
            IPlayerInventory inventory = local != null ? local.GetComponentInParent<IPlayerInventory>() : null;
            if (inventory == null) return false;

            InventorySlot held = inventory.GetSlot(inventory.SelectedSlotIndex);
            return held != null && held.Item == crane.Load;
        }
    }
}
