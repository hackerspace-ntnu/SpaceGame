using System;
using System.Runtime.InteropServices;
using FMODUnity;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Items;
using SpaceGame.World;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Handle interaction with the ship, such as adding scrap to the ship.
    /// </summary>
    public class ShipInteraction : NetworkBehaviour, IInteractable
    {
        [SerializeField] private Transform ship;
        [SerializeField] private Ship ShipScript;

        [SerializeField] private InventoryItem scrapItem;

        [Header("Audio")]
        [SerializeField] private SfxId depositId = SfxId.ShipRepair;
        [SerializeField] private EventReference depositSound;

        public bool CanInteract()
        {
            return true;
        }

        public void Interact(Interactor interactor)
        {
            if (interactor == null) return;

            // Local, because ExecuteInteraction only ever runs on the server — a client handing over
            // scrap would otherwise hear nothing. Unlike RepairWorkstation there is no replicated
            // accept/reject channel here to hang it on, so this fires on the attempt rather than on
            // the outcome and a rejected deposit still makes a noise.
            Sfx.Play(depositId, transform.position, depositSound, GetInstanceID());

            Network.Execute(
                local: () => ExecuteInteraction(interactor),
                client: () => InteractorRelay.RequestFrom(interactor, InteractServerRpc));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void InteractServerRpc(NetworkObjectReference networkObjectReference)
        {
            if (!InteractorRelay.TryResolve(networkObjectReference, out Interactor interactor)) return;

            ExecuteInteraction(interactor);
        }

        private void ExecuteInteraction(Interactor interactor)
        {
            IPlayerInventory playerInventory = interactor.GetComponentInParent<IPlayerInventory>();
            if (playerInventory == null) return;

            InventorySlot inventorySlot = playerInventory.GetSelectedSlot();
            if (inventorySlot == null) return;

            InventoryItem inventoryItem = inventorySlot.Item;
            if (!inventoryItem)
            {
                return;
            }

            bool accepted = false;
            if (inventoryItem.ID == scrapItem.ID)
            {
                accepted = playerInventory.TryRemoveItem(playerInventory.SelectedSlotIndex);
            }

            if (accepted)
            {
                ShipScript.AddScrap();
            }
        }
    }
}
