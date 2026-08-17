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

        private EquipItemSocket equipmentSocket;
        private IPlayerInventory inventory;

        private GameObject equippedItemObject;


        private void Awake()
        {
            // Always prefer the actual armature bone — the serialized handSocket is
            // only a manual override for rigs the auto-resolver can't handle.
            var resolved = ResolveHandSocket();
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

            equipmentSocket = new EquipItemSocket(handSocket);
        }

        private Transform ResolveHandSocket()
        {
            // Humanoid rig: ask the Animator for the actual bone Transform.
            var anim = GetComponentInChildren<Animator>(true);
            if (anim != null && anim.isHuman)
            {
                var bone = anim.GetBoneTransform(handBone);
                if (bone != null) return bone;
            }

            // Generic rig: substring-search the hierarchy by bone name.
            if (handBoneNameHints != null && handBoneNameHints.Length > 0)
            {
                var all = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    string n = all[i].name;
                    for (int h = 0; h < handBoneNameHints.Length; h++)
                    {
                        var hint = handBoneNameHints[h];
                        if (string.IsNullOrEmpty(hint)) continue;
                        if (n.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                            return all[i];
                    }
                }
            }

            return null;
        }

        private void Start()
        {
            var player = GetComponent<PlayerController>();
            inventory = player.PlayerInventory;

            inventory.OnSlotSelected += HandleEquip;
            inventory.OnSlotChanged += OnSlotChanged;
            inventory.OnItemDropped += OnItemDropped;

            player.Input.OnUsePressed += OnUse;

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
        
            // No network hop here on purpose. What is in a player's hands follows from which
            // hotbar slot is selected, and PlayerInventoryNetwork already replicates that as
            // server-owned state — so every machine reaches this method on its own and equips the
            // same thing. A second channel carrying the same decision only made non-owners rebuild
            // the item twice, and could disagree with the first.
            Equip(slot.Item);
        }


        public void Equip(InventoryItem item)
        {
            Unequip();

            if (item == null || item.itemPrefab == null)
            {
                Debug.LogError("EquipmentController.Equip: InventoryItem or itemPrefab is null!", this);
                return;
            }

            equippedItemObject = equipmentSocket.Equip(item.itemPrefab);

            if (equippedItemObject == null)
            {
                Debug.LogError($"EquipmentController.Equip: Failed to equip {item.name} - prefab instantiation failed!", this);
                return;
            }

            var usableItem = equippedItemObject.GetComponent<UsableItem>();
            if (usableItem)
            {
                usableItem.OnItemDepleted += ItemDepleted;
                usableItem.OnEquipped(gameObject);
            }
        }

        private void Unequip()
        {
            if (equippedItemObject)
            {
                var usable = equippedItemObject.GetComponent<UsableItem>();
                if (usable)
                {
                    usable.OnUnequipped(gameObject);
                    usable.OnItemDepleted -= ItemDepleted;
                }
            }

            equipmentSocket.Unequip();
            equippedItemObject = null;
        }

        private void ItemDepleted(UsableItem item)
        {
            item.OnItemDepleted -= ItemDepleted;
            inventory.TryRemoveItem(inventory.SelectedSlotIndex);
            Unequip();
        }

        // ─────────── Using the held item, across the network ───────────
        //
        // This is the only place an artifact is triggered, which is why it is the only place that
        // needs to know about the network. Every artifact — the eight that exist and every one
        // written after this — replicates because of these three methods, not because anyone
        // remembered to add a sync component to it.

        private void OnEnable()
        {
            this.NetOn(NetMsg.UseItem, OnUseRequested);
            this.NetOn(NetMsg.ItemUsed, OnItemUsedElsewhere);
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.UseItem, OnUseRequested);
            this.NetOff(NetMsg.ItemUsed, OnItemUsedElsewhere);
        }

        /// <summary>Owner pressed use.</summary>
        private void OnUse()
        {
            UsableItem usable = HeldItem();
            if (usable == null) return;

            // The owner describes the use — chiefly where they aimed, which is knowable only here.
            var arg = new NetArg { A = inventory?.SelectedSlotIndex ?? -1 };
            usable.OnRequestUse(ref arg);

            // Presented immediately, always, so no item ever feels like it is waiting for a reply.
            usable.PlayUse(gameObject, arg);

            // An owner-authoritative tool is ours to run, right now — its effect is this player's
            // own body, which already replicates through the transform they own.
            if (usable.Authority == UseAuthority.Owner)
                usable.TryUse(gameObject, arg);

            // Either way the server hears about it, because only the server can reach the peers.
            this.NetToServer(NetMsg.UseItem, arg);
        }

        /// <summary>Server side: run the effect if it is the server's to run, then tell the peers.</summary>
        private void OnUseRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;

            UsableItem usable = HeldItem();
            if (usable == null) return;

            // The slot the owner used must still be the slot they hold. Without this a stale
            // request that crossed a hotbar switch fires the wrong artifact on the server only.
            if (arg.A >= 0 && inventory != null && arg.A != inventory.SelectedSlotIndex) return;

            if (usable.Authority == UseAuthority.Server)
                usable.TryUse(gameObject, arg);

            // Everyone except the machine that already presented it locally.
            this.NetToOthers(NetMsg.ItemUsed, arg, except: sender);
        }

        /// <summary>Peer side: cosmetics only. The effect happened on the server.</summary>
        private void OnItemUsedElsewhere(in NetArg arg, ulong sender)
        {
            HeldItem()?.PlayUse(gameObject, arg);
        }

        private UsableItem HeldItem() =>
            equippedItemObject != null ? equippedItemObject.GetComponent<UsableItem>() : null;

        private void OnItemDropped(InventoryItem item)
        {
            GameServices.ItemDropService.DropItem(handSocket, item);
        }
    }
}
