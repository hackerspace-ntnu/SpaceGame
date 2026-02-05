

using UnityEngine;

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