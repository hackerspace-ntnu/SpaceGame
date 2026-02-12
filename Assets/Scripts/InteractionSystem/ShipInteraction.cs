using System.Runtime.InteropServices;
using UnityEngine;

public class ShipInteraction : MonoBehaviour, IInteractable
{
    [SerializeField]
    private Transform ship;
    [SerializeField]
    private Ship ShipScript;
    private bool accepted;
    
    public bool CanInteract()
    {
        return true;
    }
    public void Interact(Interactor interactor)
    {
        Debug.Log("interact");
        if (interactor.TryGetComponent<PlayerInventory>(out PlayerInventory playerInventory))
        {
            InventorySlot inventorySlot = playerInventory.GetSeletedSlot();
            InventoryItem inventoryItem = inventorySlot.Item;
            if (!inventoryItem)
            {
                Debug.Log("no item held");
                return;
            }
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
