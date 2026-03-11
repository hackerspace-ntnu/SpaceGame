using System.Runtime.InteropServices;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handle interaction with the ship, such as adding scrap to the ship.
/// </summary>
public class ShipInteraction : NetworkBehaviour, IInteractable
{
    [SerializeField]
    private Transform ship;
    [SerializeField]
    private Ship ShipScript;
    
    [SerializeField] private InventoryItem scrapItem;
    
    public bool CanInteract()
    {
        return true;
    }
    public void Interact(Interactor interactor)
    {
        if (interactor.TryGetComponent<PlayerInventory>(out PlayerInventory playerInventory))
        {
            InventorySlot inventorySlot = playerInventory.GetSelectedSlot();
            if(inventorySlot == null) return;
            
            InventoryItem inventoryItem = inventorySlot.Item;
            if (!inventoryItem)
            {
                Debug.Log("no item held");
                return;
            }
            bool accepted = false;
            if (inventoryItem.ID == scrapItem.ID)
            {
                accepted = playerInventory.TryRemoveItem(playerInventory.SelectedSlotIndex);
            }
            if (accepted)
            {
                AddItemServerRpc(interactor.GetComponentInParent<NetworkObject>());
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void AddItemServerRpc(NetworkObjectReference playerNetworkObject)
    {
        ShipScript.AddScrap();
    }
}
