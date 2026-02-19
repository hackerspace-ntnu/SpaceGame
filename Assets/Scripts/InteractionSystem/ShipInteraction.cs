using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Handle interaction with the ship, such as adding scrap to the ship.
/// </summary>
public class ShipInteraction : MonoBehaviour, IInteractable
{
    [SerializeField]
    private Transform ship;
    [SerializeField]
    private Ship ShipScript;
    
    public bool CanInteract()
    {
        return true;
    }
    public void Interact(Interactor interactor)
    {
        if (interactor.TryGetComponent<PlayerInventory>(out PlayerInventory playerInventory))
        {
            InventorySlot inventorySlot = playerInventory.GetSeletedSlot();
            if(inventorySlot == null) return;
            
            InventoryItem inventoryItem = inventorySlot.Item;
            if (!inventoryItem)
            {
                Debug.Log("no item held");
                return;
            }
            bool accepted = false;
            if (inventoryItem.itemId == ItemId.Scrap)
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
}
