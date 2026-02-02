

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
      InventoryComponent inventoryComponent = interactor.GetComponent<InventoryComponent>();
      if (inventoryComponent != null)
      {
         bool added = inventoryComponent.TryAddItem(item);
         if (added)
         {
            Destroy(gameObject);
         }
      }
   }
}