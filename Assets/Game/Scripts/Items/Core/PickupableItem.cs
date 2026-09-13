using System.Collections.Generic;
using FMODUnity;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Items
{
    /// <summary>
    /// Script to be attached to pickupable items in the world.
    /// When interacted with, it will attempt to add the item to the player's inventory and destroy itself if successful.
    ///
    /// <para>
    /// Also the scanner's default contact: loose salvage is what the item scanner exists to find,
    /// so every pickup registers itself rather than waiting for somebody to remember a
    /// <see cref="ScanBeacon"/>. Registration is tied to enable/disable, which is what makes it
    /// correct under world streaming — a chunk unloading disables its contents and its contacts
    /// leave the registry with them.
    /// </para>
    /// <para>
    /// <b>And the custodian of the item's <see cref="ItemState"/> while it lies there.</b> A hotbar
    /// slot holds that bag for an item somebody is carrying, and there was nowhere for it to live
    /// between a drop and the next pickup — so everything an item had become was thrown away by
    /// putting it on the ground, and only a supply charge survived, hand-threaded through the whole
    /// drop path. The bag rides this component instead, verbatim and uninterpreted, and goes back
    /// into whichever slot the item lands in.
    /// </para>
    /// <para>
    /// <b>Uninterpreted is the load-bearing word.</b> Nothing here hands the bag to the item's own
    /// components: a lot of what an item remembers is about the person HOLDING it — a grapple's
    /// anchor, a lasso's catch — and applying that to an object lying in the sand would have a
    /// dropped grappling hook resume a swing with nobody on the end of it. An item in the world is
    /// not being held, so nothing about it is in effect. The one exception is a reservoir's charge,
    /// which is about the object rather than the holder and which the player reads off a gauge on
    /// the sand; <c>PlayerDropService</c> paints that one key onto the instance at the drop, and the
    /// instance's own live reading is what goes back into the slot below.
    /// </para>
    /// <para>
    /// It lives here rather than on a component of its own because the bag has to be present on a
    /// RESTORED item too — <c>SaveableEntity.Restore</c> hands a payload only to a saver that
    /// already exists, so a saver added at the drop would be captured faithfully and dropped
    /// silently on the way back in. This component is on every item prefab by construction (it is
    /// what <c>SaveablePolicy</c> reads as "this is a pickup"), so it is always there to be handed
    /// the record.
    /// </para>
    /// </summary>
    public class PickupableItem : NetworkBehaviour, IInteractable, IScanTarget, IInteractionReadout, ISaveable
    {
       [SerializeField] private InventoryItem item;

       /// <summary>
       /// The saver key for what a dropped item remembers. Written into save files — never rename.
       /// </summary>
       public const string StateKey = "itemstate";

       /// <summary>
       /// What this instance was carrying when it was put down, or null for an item at its authored
       /// defaults — which is every item that has never been in anybody's hotbar.
       /// </summary>
       private ItemState carried;

       [Header("Audio")]
       [SerializeField] private SfxId pickupId = SfxId.InteractPickup;
       [SerializeField] private EventReference pickupSound;

       public bool CanInteract()
       {
          return true;
       }

       // ── IInteractionReadout ──────────────────────────────────────────────

       /// <summary>
       /// The item's own name, which is the only thing about a pickup worth saying.
       ///
       /// <para>
       /// Without this every loose object in the world read "Pickupable Item" — the humanised type
       /// name, which is what <c>InteractionPromptResolver</c> falls back to when nothing more
       /// specific answers. Salvage is the most common interactable in the game and the one whose
       /// identity matters most, since deciding whether to spend a hotbar slot on it is the whole
       /// interaction. The name was already being written for the scanner
       /// (<see cref="ScanLabel"/>); nothing read it at the crosshair.
       /// </para>
       /// </summary>
       public string Label => ScanLabel;

       public string Prompt => "RMB: pick up";

       /// <summary>Nothing continuous about a pickup: it is there or it is in your hand.</summary>
       public float? Value01 => null;

       public string ValueText => string.Empty;

       // ── IScanTarget ──────────────────────────────────────────────────────────

       public bool IsScannable => isActiveAndEnabled;
       public Vector3 ScanPosition => transform.position;
       public ScanClass ScanClass => ScanClass.Item;
       public string ScanLabel => item != null ? item.itemName : name;

       private void OnEnable() => ScannerRegistry.Register(this);
       private void OnDisable() => ScannerRegistry.Unregister(this);

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

          // What this particular object holds, if anything -- read off the world instance, not off
          // the item asset, because two tanks lying side by side are the same asset at different
          // fills. SupplyCharge.None for the great majority of items, which hold nothing.
          SupplyReservoir supply = SupplyReservoir.On(gameObject);
          float charge = supply != null ? supply.Charge : SupplyCharge.None;

          bool added = inventory.TryAddItem(item, out int landed);

          if (added && landed >= 0) GiveBackTo(inventory, landed, charge);

          // Hotbar first, then the pack. Without the overflow a four-slot hotbar means the backpack
          // never fills from the world, and the only way to put anything in it is the inspector.
          //
          // TryStow is first-fit across the pack's surfaces, so a pickup goes wherever it fits
          // rather than into a numbered slot. It can refuse: the limit is surface area now, and a
          // 1.35 m staff needs a diagonal that a loaded pack may not have left.
          if (!added)
          {
             BackpackController backpack = interactor.GetComponentInParent<BackpackController>();
             if (backpack != null && backpack.Pack != null)
             {
                WarnAboutStateThePackCannotHold();
                added = backpack.Pack.TryStow(item, charge);
             }
          }

          if (added)
          {
             GameServices.World.Despawn(gameObject);
          }
       }

       /// <summary>
       /// Hand the slot this item landed in everything the object had been carrying.
       ///
       /// <para>
       /// After the add and never before it: assigning a slot's item clears its state, so a bag
       /// written first is wiped by the very assignment it was written for.
       /// </para>
       /// <para>
       /// The carried bag first and the live charge second, in that order, because they can
       /// disagree and the object is the later word. The bag is what the slot said at the moment of
       /// the drop; the charge is what the reservoir on this instance reads NOW — which is what a
       /// tank picked out of a chunk it was authored in has, and what a tank whose gauge changed
       /// while it lay there has. That is also why a charge is not special-cased out of the bag: it
       /// travels in the bag like every other key, and this is the one key with a second, fresher
       /// source.
       /// </para>
       /// </summary>
       private void GiveBackTo(IPlayerInventory inventory, int index, float charge)
       {
          InventorySlot slot = inventory.GetSlot(index);
          if (slot == null || slot.IsEmpty) return;

          // A copy, not the bag itself: this object is despawned rather than destroyed on the spot,
          // and a slot holding a live reference into a dying component is a bug waiting for a frame.
          if (carried != null) slot.State = new ItemState(carried.Raw);

          if (charge >= 0f)
          {
             slot.State ??= new ItemState();
             SupplyCharge.Write(slot.State, charge);
          }

          // The bag does not replicate, so anything in it the owning client can SEE has to be
          // pushed after the fact. Today the charge is the only such key; the call is made for the
          // whole bag so the next one does not have to remember.
          if (slot.State != null) inventory.PublishSlotCharges();
       }

       /// <summary>
       /// Say so when an item goes into the backpack carrying state the pack has nowhere to put.
       ///
       /// <para>
       /// A pack placement stores an item id and one charge byte and nothing else, so every other
       /// key in the bag is lost at this step. Widening a placement is <c>Backpack</c>'s call and
       /// not this file's — but the loss must not be silent. A creature disappearing out of a
       /// canister with a clean console is exactly the failure the bag exists to prevent, and the
       /// player has a way out that only a message can tell them about: free a hotbar slot first.
       /// </para>
       /// </summary>
       private void WarnAboutStateThePackCannotHold()
       {
          if (carried == null) return;

          List<string> lost = null;

          foreach (KeyValuePair<string, string> entry in carried.Raw)
          {
             if (entry.Key == SupplyCharge.StateKey) continue;
             (lost ??= new List<string>()).Add(entry.Key);
          }

          if (lost == null) return;

          Debug.LogWarning($"[Pickup] '{ScanLabel}' is going into the backpack, which stores an item " +
                           "id and a charge and nothing else — so " + string.Join(", ", lost) +
                           " is being dropped. Free a hotbar slot and pick it up again to keep it.",
                           this);
       }

       // ── What a dropped item remembers ────────────────────────────────────────

       /// <summary>
       /// Give this world instance the bag its slot was holding. Called by the drop service, on the
       /// server, immediately after the spawn.
       /// </summary>
       public void Remember(ItemState state)
       {
          carried = state == null || state.IsEmpty ? null : new ItemState(state.Raw);
       }

       /// <summary>
       /// The payload. A public-field struct rather than the bare dictionary, because
       /// <c>StateBag.Set</c> drops a key whose value is not an object.
       /// </summary>
       public struct State
       {
          public Dictionary<string, string> values;
       }

       public string SaveKey => StateKey;

       public object CaptureState() =>
          carried == null || carried.IsEmpty ? null : new State { values = carried.Copy() };

       public void RestoreState(JObject state)
       {
          // Null is a value: it is what CaptureState writes for an item at its defaults, and a
          // saver is required to put itself back to those rather than keep what it happens to hold.
          if (state == null)
          {
             carried = null;
             return;
          }

          carried = GearSaveCodec.ReadBag(state[nameof(State.values)] as JObject);

          // A record under this key that decodes to nothing is a record that NAMED per-instance
          // state and lost it — a bag written by a build whose shape this one cannot read, or a
          // truncated file. Loud, because the whole point of this component is that what is inside
          // a dropped thing does not disappear quietly.
          if (carried == null)
          {
             Debug.LogError($"[Save] '{name}' has an '{StateKey}' record whose contents could not be " +
                            "read, so whatever this item was carrying is gone. The record is " +
                            $"{state}", this);
          }
       }
    }
}
