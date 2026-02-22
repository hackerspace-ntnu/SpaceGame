

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
      PickupServerRpc(interactor.GetComponentInParent<NetworkObject>());
   }

   [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
   private void PickupServerRpc(NetworkObjectReference playerNetworkObject)
   {
      if(!playerNetworkObject.TryGet(out NetworkObject itemNetworkObject)) return;
      
      PlayerInventory inventory = itemNetworkObject.GetComponentInParent<PlayerInventory>();
      if (!inventory) return;
      
      
      bool added = inventory.TryAddItem(item);
      if (!added) return;
      
      NetworkObject.Despawn();
   }
}
