using FMODUnity;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Audio;
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

       [Header("Audio")]
       [SerializeField] private SfxId pickupId = SfxId.InteractPickup;
       [SerializeField] private EventReference pickupSound;

       public bool CanInteract()
       {
          return true;
       }

       public void Interact(Interactor interactor)
       {
          if (interactor == null) return;

          // Played here rather than inside Pickup, which only ever runs on the server — a remote
          // client would pick the item up and hear nothing at all.
          //
          // The cost is that a pickup refused for a full inventory still clicks. That is the better
          // side to err on: the sound is feedback that the interact registered, and holding it back
          // for a server round trip is exactly the lag UsableItem.PlayUse exists to avoid.
          Sfx.Play(pickupId, transform.position, pickupSound, GetInstanceID());

          Network.Execute(
             local: () => Pickup(interactor),
             client: () => RequestPickup(interactor));
       }

       /// <summary>
       /// Client side: name the body doing the picking up, then ask.
       ///
       /// GetComponentInParent, not GetComponent. An Interactor sits wherever the prefab puts it —
       /// on the camera rig on this project's player — and a plain GetComponent on that child
       /// returns null, which turns into a default NetworkObjectReference the server resolves to
       /// nothing. The pickup then failed for clients only, silently and always.
       /// </summary>
       private void RequestPickup(Interactor interactor)
       {
          NetworkObject body = interactor.GetComponentInParent<NetworkObject>();
          if (body == null)
          {
             Debug.LogError($"[Pickup] '{interactor.name}' is not part of a NetworkObject, so the " +
                            "server cannot be told who is picking this up.", interactor);
             return;
          }

          RequestPickupServerRpc(body);
       }

       [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
       private void RequestPickupServerRpc(NetworkObjectReference interactorRef)
       {
          if (!interactorRef.TryGet(out NetworkObject player)) return;

          Interactor interactor = player.GetComponentInChildren<Interactor>(true);
          if (interactor != null) Pickup(interactor);
       }

       private void Pickup(Interactor interactor)
       {
          // One item, one taker. Two players reaching the same crate on the same frame both land
          // here on the server, and without this the second one is handed a copy of an item that
          // has already been despawned — free duplication, and a despawn call on a dead object.
          if (Network.IsNetworked && !IsSpawned) return;

          IPlayerInventory inventory = interactor.GetComponentInParent<IPlayerInventory>();
          if (inventory == null) return;
          bool added = inventory.TryAddItem(item);

          // Hotbar first, then the pack. Without the overflow a four-slot hotbar means the backpack
          // never fills from the world, and the only way to put anything in it is the inspector.
          if (!added)
          {
             BackpackController backpack = interactor.GetComponentInParent<BackpackController>();
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
