using System;
using System.Runtime.InteropServices;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handle interaction with the ship, such as adding scrap to the ship.
/// </summary>
public class ShipInteraction : NetworkBehaviour, IInteractable
{
    [SerializeField] private Transform ship;
    [SerializeField] private Ship ShipScript;

    [SerializeField] private InventoryItem scrapItem;

    public bool CanInteract()
    {
        return true;
    }

    public void Interact(Interactor interactor)
    {
        Network.Execute(
            local: ()=> ExecuteInteraction(interactor),
            client: () => InteractServerRpc(interactor.GetComponent<NetworkObject>()));
    }

    [Rpc(SendTo.Server)]
    private void InteractServerRpc(NetworkObjectReference networkObjectReference)
    {
        networkObjectReference.TryGet(out NetworkObject networkObject);
        ExecuteInteraction(networkObject.GetComponent<Interactor>());
    }
    
    private void ExecuteInteraction(Interactor interactor)
    {
        IPlayerInventory playerInventory = interactor.GetComponent<IPlayerInventory>();
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
