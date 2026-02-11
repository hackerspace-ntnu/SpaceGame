using System.Runtime.InteropServices;
using UnityEngine;

public class ShipInteraction : MonoBehaviour, IInteractable
{
    [SerializeField]
    private Transform ship;
    private bool accepted;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public bool CanInteract()
    {
        return true;
    }
    public void Interact(Interactor interactor)
    {
        Debug.Log("interact");
        if (interactor.TryGetComponent<PlayerInventory>(out PlayerInventory playerInventory))
        {
            InventorySlot inventorySlot = playerInventory.GetSlot(playerInventory.selectedSlotIndex);
            InventoryItem inventoryItem = inventorySlot.Item;
            if (inventoryItem.itemId == ItemId.scrap)
            {
                bool accepted = playerInventory.TryRemoveItem(playerInventory.selectedSlotIndex);
            }
            if (accepted)
            {
                Debug.Log("item added");
            }
        }
    }
}
