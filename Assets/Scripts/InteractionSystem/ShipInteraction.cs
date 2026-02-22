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
        RemoveItemServerRpc(interactor.GetComponentInParent<NetworkObject>());
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
            if (inventoryItem.itemId == scrapItem.itemId)
            {
                accepted = playerInventory.TryRemoveItem(playerInventory.selectedSlotIndex);
            }
            if (accepted)
            {
                Debug.Log("item removed from inventory");
                ShipScript.AddScrap();
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RemoveItemServerRpc(NetworkObjectReference playerNetworkObject)
    {
        if(!playerNetworkObject.TryGet(out NetworkObject itemNetworkObject)) return;
      
        PlayerInventory playerInventory = itemNetworkObject.GetComponentInParent<PlayerInventory>();
        if (!playerInventory) return;
        
        InventorySlot inventorySlot = playerInventory.GetSelectedSlot();
        if(inventorySlot == null) return;
        
        InventoryItem inventoryItem = inventorySlot.Item;
        if (!inventoryItem)
        {
            Debug.Log("no item held");
            return;
        }
        bool accepted = false;
        if (inventoryItem.itemId == scrapItem.itemId)
        {
            accepted = playerInventory.TryRemoveItem(playerInventory.selectedSlotIndex);
        }
        
        if (accepted)
        {
            Debug.Log("item removed from inventory");
            ShipScript.AddScrap();
        }
    }
}
