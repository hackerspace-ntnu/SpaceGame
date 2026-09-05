using System;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    public class EquipmentController : MonoBehaviour
    {
        [Tooltip("Where equipped items are parented. If left empty, the controller resolves it automatically: humanoid rigs use Animator.GetBoneTransform(handBone); generic rigs fall back to a name search using handBoneNameHints.")]
        [SerializeField] private Transform handSocket;
        [Tooltip("Which hand bone to use when auto-resolving handSocket from a humanoid rig.")]
        [SerializeField] private HumanBodyBones handBone = HumanBodyBones.RightHand;
        [Tooltip("Substring hints used when auto-resolving handSocket on a non-humanoid rig (case-insensitive). The first child Transform whose name contains any of these wins.")]
        [SerializeField] private string[] handBoneNameHints = { "RightHand", "Hand_R", "R_Hand", "hand.R" };

        [Header("Off hand")]
        [Tooltip("Bone used for items whose ItemGrip says they are held in the other hand. Leave the hints empty to keep everything in the main hand.")]
        [SerializeField] private HumanBodyBones offHandBone = HumanBodyBones.LeftHand;
        [SerializeField] private string[] offHandBoneNameHints = { "LeftHand", "Hand_L", "L_Hand", "hand.L" };

        [Header("Grip")]
        [Tooltip("Optional child of the hand bone whose local position and rotation replace the grip frame this controller works out from the rig's finger bones. Only needed for a rig the derivation gets wrong.")]
        [SerializeField] private Transform gripFrameOverride;

        [Tooltip("Multiplies the size every held item is scaled to. 1 means an item's ItemGrip.holdSize is taken literally, which is what you want unless this character is deliberately outsized.")]
        [SerializeField] private float holdScaleMultiplier = 1f;

        private EquipItemSocket equipmentSocket;
        private EquipItemSocket offHandSocket;
        private EquipItemSocket activeSocket;
        private IPlayerInventory inventory;

        private GameObject equippedItemObject;

        /// <summary>
        /// Which hotbar slot the held object came out of, or -1.
        ///
        /// Needed because unequipping has to put the item's state back where it came from, and by
        /// the time <see cref="Unequip"/> runs the selection has usually already moved on — the
        /// inventory raises OnSlotSelected with the NEW slot, and equipping the new item is what
        /// unequips the old one.
        /// </summary>
        private int equippedSlotIndex = -1;

        /// <summary>
        /// The hand's trigger: the Use button on whatever the selected hotbar slot holds. The
        /// pipeline itself — request, present, authority, broadcast, hold stream — lives in
        /// <see cref="UseChannel"/>, shared with the worn gear's three triggers.
        /// </summary>
        private UseChannel hand;

        /// <summary>
        /// The main hand's grip rotation, in that hand bone's own space.
        ///
        /// <para>
        /// Identity when there is no main hand, which is the right answer for a character with no
        /// rig to speak of: aim the hand at the target and accept that the item sits however the
        /// prefab sits.
        /// </para>
        /// </summary>
        public Quaternion MainHandGripLocalRotation =>
            equipmentSocket != null ? equipmentSocket.FrameLocalRotation : Quaternion.identity;

        /// <summary>The off hand's grip rotation in its own bone's space; identity without an off hand.</summary>
        public Quaternion OffHandGripLocalRotation =>
            offHandSocket != null ? offHandSocket.FrameLocalRotation : Quaternion.identity;

        /// <summary>The grip rotation for either hand — see <see cref="MainHandGripLocalRotation"/>.</summary>
        public Quaternion GripLocalRotation(ItemGrip.Hand hand) =>
            hand == ItemGrip.Hand.Left ? OffHandGripLocalRotation : MainHandGripLocalRotation;

        private Animator rigAnimator;

        private void Awake()
        {
            rigAnimator = GetComponentInChildren<Animator>(true);

            // Always prefer the actual armature bone — the serialized handSocket is
            // only a manual override for rigs the auto-resolver can't handle.
            var resolved = BoneResolver.Resolve(rigAnimator, transform, handBone, handBoneNameHints);
            if (resolved != null)
            {
                handSocket = resolved;
            }
            else if (handSocket == null)
            {
                Debug.LogError("EquipmentController: could not resolve a hand bone. Assign handSocket manually or add hints in handBoneNameHints.", this);
            }
            else
            {
                Debug.LogWarning("EquipmentController: hand bone auto-resolve failed; falling back to the serialized handSocket Transform.", this);
            }

            equipmentSocket = NewSocket(ItemGrip.Hand.Right);

            // The off hand is genuinely optional. A rig without one, or a character that only ever
            // holds things in the main hand, just gets a null here and every item goes to the main
            // socket — no warning, because nothing is wrong.
            offHandSocket = NewSocket(ItemGrip.Hand.Left);

            hand = new UseChannel(this, GearArea.Hotbar,
                                  () => GearRef.Hotbar(inventory?.SelectedSlotIndex ?? -1),
                                  HeldItem);
        }

        /// <summary>
        /// A fresh socket on one hand, with the grip frame derived from the rig's own anatomy.
        ///
        /// <para>
        /// Derived rather than serialized on purpose: a hand bone's rotation is whatever the FBX
        /// exporter wrote, it changes when the rig is re-exported, and it differs between every
        /// character. Reading the fingers instead gives a frame that means the same thing on all
        /// of them, so an item tuned once is tuned everywhere.
        /// </para>
        /// <para>
        /// Public because a worn gauntlet sits on the same bone as a held item and needs a socket
        /// of its own — one bone, two things on it — seated in the same frame so one set of
        /// <see cref="ItemGrip"/> offsets serves both. Null when the rig has no such hand.
        /// </para>
        /// </summary>
        public EquipItemSocket NewSocket(ItemGrip.Hand which)
        {
            bool isRightHand = which == ItemGrip.Hand.Right;
            Transform bone = isRightHand
                ? handSocket
                : BoneResolver.Resolve(rigAnimator, transform, offHandBone, offHandBoneNameHints);

            if (bone == null) return null;
            if (!isRightHand && bone == handSocket) return null;

            HandGripFrame frame;

            if (gripFrameOverride != null && isRightHand && gripFrameOverride.IsChildOf(bone))
            {
                frame = new HandGripFrame(gripFrameOverride.localPosition,
                                          gripFrameOverride.localRotation,
                                          gripFrameOverride.localPosition.magnitude,
                                          "serialized gripFrameOverride");
            }
            else
            {
                frame = HandGripFrame.Derive(rigAnimator, bone, isRightHand);
            }

            return new EquipItemSocket(bone, frame, holdScaleMultiplier);
        }

        private void Start()
        {
            var player = GetComponent<PlayerController>();
            inventory = player.PlayerInventory;

            inventory.OnSlotSelected += HandleEquip;
            inventory.OnSlotChanged += OnSlotChanged;
            inventory.OnItemDropped += OnItemDropped;

            player.Input.OnUsePressed += OnUse;
            player.Input.OnUseReleased += OnUseRelease;

            // Equip whatever is already selected, rather than waiting for the next change.
            //
            // On a client, another player's body spawns with their hotbar already populated and no
            // change event to come — the selection was made before this machine had a copy of them
            // to tell. Pulled here instead of pushed from the inventory because OnNetworkSpawn
            // runs before this Start, so anything pushed then would arrive before there was
            // anyone to hear it.
            HandleEquip(inventory.GetSelectedSlot());
        }

        private void OnSlotChanged(int index, InventorySlot slot)
        {
            if (inventory.SelectedSlotIndex != index) return;
            HandleEquip(slot);
        }

        private void HandleEquip(InventorySlot slot)
        {
            if (slot == null || slot.IsEmpty)
            {
                Unequip();
                return;
            }

            // A gauntlet or a pack lying in the hotbar is storage, not a thing to hold. Selecting
            // it empties the hands — the HUD marks the tile so the player knows why — and the item
            // only ever comes alive in the body slot it was made for.
            if (!BodySlotRules.HandEquips(slot.Item.equipKind))
            {
                Unequip();
                return;
            }

            // No network hop here on purpose. What is in a player's hands follows from which
            // hotbar slot is selected, and PlayerInventoryNetwork already replicates that as
            // server-owned state — so every machine reaches this method on its own and equips the
            // same thing. A second channel carrying the same decision only made non-owners rebuild
            // the item twice, and could disagree with the first.
            Equip(slot.Item, slot);
        }


        public void Equip(InventoryItem item) => Equip(item, null);

        /// <summary>
        /// Puts an item in the hand, and — when it came from a hotbar slot — hands it back whatever
        /// that slot remembers about it. See <see cref="ItemState"/>.
        /// </summary>
        public void Equip(InventoryItem item, InventorySlot slot)
        {
            Unequip();

            if (item == null || item.itemPrefab == null)
            {
                Debug.LogError("EquipmentController.Equip: InventoryItem or itemPrefab is null!", this);
                return;
            }

            activeSocket = SocketFor(item.itemPrefab);
            equippedItemObject = activeSocket != null ? activeSocket.Equip(item.itemPrefab) : null;

            if (equippedItemObject == null)
            {
                Debug.LogError($"EquipmentController.Equip: Failed to equip {item.name} - prefab instantiation failed!", this);
                return;
            }

            equippedSlotIndex = slot?.Index ?? -1;

            var usableItem = equippedItemObject.GetComponent<UsableItem>();
            if (usableItem)
            {
                usableItem.OnItemDepleted += ItemDepleted;
                usableItem.OnEquipped(gameObject);

                // AFTER OnEquipped, deliberately. Several items reset themselves there — the item
                // scanner switches its own power off, a weapon's OnEnable has just refilled its
                // magazine — so a restore that ran first would be overwritten by the very act of
                // picking the item up.
                if (usableItem is IItemStateCarrier carrier)
                    carrier.RestoreItemState(slot?.State);
            }
        }

        /// <summary>
        /// Copy the held item's live state back into the slot it came from.
        ///
        /// <para>
        /// Called on unequip, and again whenever the slot has to be accurate without the item being
        /// put away — a save, chiefly. Without the second caller, saving while holding a half-empty
        /// gun would store whatever the slot last heard, which is the state the gun was in when it
        /// was last put down.
        /// </para>
        /// </summary>
        public void WriteBackHeldItemState()
        {
            if (equippedItemObject == null || inventory == null) return;
            if (equippedItemObject.GetComponent<UsableItem>() is not IItemStateCarrier carrier) return;

            InventorySlot slot = inventory.GetSlot(equippedSlotIndex);
            if (slot == null || slot.IsEmpty) return;

            var state = new ItemState();
            carrier.CaptureItemState(state);

            // An empty bag is stored as no bag: "at its defaults" is the common case and should not
            // put a dictionary in the save file for every slot in the game.
            slot.State = state.IsEmpty ? null : state;
        }

        /// <summary>
        /// Hand the currently held item the state its slot now holds.
        ///
        /// <para>
        /// For a load, and only for a load. Restoring the hotbar equips the selected item as a side
        /// effect of assigning the selection, which happens BEFORE the saver can put the per-slot
        /// bags back — so the item that ends up in the hand is the one item restored without its
        /// state. This is the second pass that fixes that.
        /// </para>
        /// </summary>
        public void ReapplyHeldItemState()
        {
            if (equippedItemObject == null || inventory == null) return;
            if (equippedItemObject.GetComponent<UsableItem>() is not IItemStateCarrier carrier) return;

            InventorySlot slot = inventory.GetSlot(equippedSlotIndex);
            if (slot == null) return;

            carrier.RestoreItemState(slot.State);
        }

        /// <summary>The item currently in the hand, or null. For savers and tests.</summary>
        public UsableItem HeldUsable => HeldItem();

        /// <summary>
        /// Trigger the held item as though the use button had been pressed.
        ///
        /// The seam <c>MultiplayerAutotest</c> fires through, because the button itself cannot be
        /// pressed from there: <c>PlayerInputManager.OnUsePressed</c> is a C# event, and only the
        /// class that declares one may raise it. Everything from <see cref="UsableItem.OnRequestUse"/>
        /// onwards is the real path — the request, the local present, the hop to the server and the
        /// broadcast to the peers — so what this leaves untested is exactly one thing: that the Use
        /// action is still bound to <see cref="OnUse"/>.
        /// </summary>
        public void UseHeldItem() => OnUse();

        private void Unequip()
        {
            // Before anything is destroyed: the slot has to keep what this instance became, or
            // switching hotbar slot and back would refill the magazine and the charges.
            WriteBackHeldItemState();

            // Before anything is cleared, while HeldItem() still answers with the item that is
            // actually burning. Putting a beam away has to put the beam out.
            hand?.EndHold(send: true);

            if (equippedItemObject)
            {
                var usable = equippedItemObject.GetComponent<UsableItem>();
                if (usable)
                {
                    usable.OnUnequipped(gameObject);
                    usable.OnItemDepleted -= ItemDepleted;
                }
            }

            // Whichever hand it went into. Unequipping only the main socket would leave an off-hand
            // item welded to the wrist forever, since nothing else ever destroys it.
            equipmentSocket?.Unequip();
            offHandSocket?.Unequip();
            activeSocket = null;
            equippedItemObject = null;
            equippedSlotIndex = -1;
        }

        /// <summary>Which hand this prefab asked for, falling back to the main one.</summary>
        private EquipItemSocket SocketFor(GameObject prefab)
        {
            var grip = prefab.GetComponent<ItemGrip>();
            if (grip != null && grip.HeldIn == ItemGrip.Hand.Left && offHandSocket != null)
                return offHandSocket;

            return equipmentSocket;
        }

        private void ItemDepleted(UsableItem item)
        {
            item.OnItemDepleted -= ItemDepleted;
            inventory.TryRemoveItem(inventory.SelectedSlotIndex);
            Unequip();
        }

        // ─────────── Using the held item, across the network ───────────
        //
        // The pipeline is UseChannel's. This component owns the hand's channel, registers for the
        // four use messages on the player's relay and forwards the hotbar's share of them; the
        // worn gear's controller does the same for its three channels on the same relay.

        private void OnEnable()
        {
            this.NetOn(NetMsg.UseItem, OnUseRequested);
            this.NetOn(NetMsg.ItemUsed, OnItemUsedElsewhere);
            this.NetOn(NetMsg.UseItemHold, OnHoldRequested);
            this.NetOn(NetMsg.ItemUseHeld, OnItemHeldElsewhere);
        }

        private void OnDisable()
        {
            // Locally only. This runs during death and during teardown, and a message sent into
            // either is at best ignored and at worst sent through a half-shut channel — so the
            // beam is dropped here and the SERVER is left to notice the stream stopped. That is
            // what the item's own hold timeout is for, and it is the same thing that covers a
            // player who disconnects mid-beam, which no amount of sending here could.
            hand?.EndHold(send: false);

            this.NetOff(NetMsg.UseItem, OnUseRequested);
            this.NetOff(NetMsg.ItemUsed, OnItemUsedElsewhere);
            this.NetOff(NetMsg.UseItemHold, OnHoldRequested);
            this.NetOff(NetMsg.ItemUseHeld, OnItemHeldElsewhere);
        }

        /// <summary>Owner pressed use.</summary>
        private void OnUse()
        {
            // The crosshair is on a gear wall the player can act on. There, Use is the wall's verb
            // — "put this down", or "take that off" — not "fire it". WallAimController is already
            // sending the stow or the take off the same press, and both happening would fire a
            // staff point-blank into the wall the player is stowing it on, or into the shelf they
            // are lifting a crate off.
            //
            // Asked here rather than solved by subscription order, which Unity does not promise:
            // two handlers on one event either both run or run in whichever order they were added,
            // and neither is something to hang a weapon discharge on.
            if (WallAimController.Aiming != null) return;

            hand?.Press();
        }

        private void OnUseRelease() => hand?.Release();

        private void Update() => hand?.Tick(Time.time);

        private void OnUseRequested(in NetArg arg, ulong sender)
        {
            if (hand != null && hand.Owns(arg.A)) hand.OnUseRequested(arg, sender);
        }

        private void OnItemUsedElsewhere(in NetArg arg, ulong sender)
        {
            if (hand != null && hand.Owns(arg.A)) hand.OnUsedElsewhere(arg, sender);
        }

        private void OnHoldRequested(in NetArg arg, ulong sender)
        {
            if (hand != null && hand.Owns(arg.A)) hand.OnHoldRequested(arg, sender);
        }

        private void OnItemHeldElsewhere(in NetArg arg, ulong sender)
        {
            if (hand != null && hand.Owns(arg.A)) hand.OnHeldElsewhere(arg, sender);
        }

        private UsableItem HeldItem() =>
            equippedItemObject != null ? equippedItemObject.GetComponent<UsableItem>() : null;

        private void OnItemDropped(InventoryItem item, float charge)
        {
            GameServices.ItemDropService.DropItem(handSocket, item, charge);
        }

        private void OnValidate()
        {
            holdScaleMultiplier = Mathf.Max(0.01f, holdScaleMultiplier);
        }
    }
}
