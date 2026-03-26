

using Unity.Netcode;
using Unity.VisualScripting;
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
      Network.Execute(
         local: () => Pickup(interactor),
         client: () => RequestPickupServerRpc(interactor.GetComponent<NetworkObject>()));
   }

   [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
   private void RequestPickupServerRpc(NetworkObjectReference interactorRef)
   {
      if (!interactorRef.TryGet(out NetworkObject player)) return;
      Pickup(player.GetComponent<Interactor>());
   }

   private void Pickup(Interactor interactor)
   {
      IPlayerInventory inventory = interactor.GetComponent<IPlayerInventory>();
      if (inventory == null) return;
      bool added = inventory.TryAddItem(item);
      if (added)
      {
         GameServices.World.Despawn(gameObject);
      }
   }
}
