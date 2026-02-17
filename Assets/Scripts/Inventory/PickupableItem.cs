

using UnityEngine;

/// <summary>
/// Script to be attached to pickupable items in the world.
/// When interacted with, it will attempt to add the item to the player's inventory and destroy itself if successful.
/// </summary>
class PickupableItem : MonoBehaviour, IInteractable
{
   [SerializeField] private InventoryItem item;


   public bool CanInteract()
   {
      return true;
   }

   public void Interact(Interactor interactor)
   {
      InventoryComponent inventoryComponent = interactor.GetComponentInParent<InventoryComponent>();
      if (!inventoryComponent) return;
      
      bool added = inventoryComponent.TryAddItem(item);
      if (added)
      {
         Destroy(transform.parent.gameObject);
      }
   }
}