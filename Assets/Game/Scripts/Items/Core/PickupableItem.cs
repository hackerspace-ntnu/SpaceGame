using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
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

          // Hotbar first, then the pack. Without the overflow a four-slot hotbar means the backpack
          // never fills from the world, and the only way to put anything in it is the inspector.
          if (!added)
          {
             BackpackController backpack = interactor.GetComponent<BackpackController>();
             if (backpack != null && backpack.Pack != null)
                added = backpack.Pack.Container.TryAddToMain(item, out _);
          }

          if (added)
          {
             GameServices.World.Despawn(gameObject);
          }
       }
    }
}
