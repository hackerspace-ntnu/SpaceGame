

using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Script to be attached to pickupable items in the world.
/// When interacted with, it will attempt to add the item to the player's inventory and destroy itself if successful.
/// </summary>
class PickupableItem : NetworkBehaviour, IInteractable
{
   [SerializeField] private InventoryItem item;


   public bool CanInteract()
   {
      return true;
   }

   public void Interact(Interactor interactor)
   {
      PlayerInventory inventory = interactor.GetComponentInParent<PlayerInventory>();
      if (!inventory) return;
      bool added = inventory.TryAddItem(item);
      if (added)
      {
         PickupServerRpc();
      }
   }

   [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
   private void PickupServerRpc()
   {
      NetworkObject.Despawn();
   }
}
