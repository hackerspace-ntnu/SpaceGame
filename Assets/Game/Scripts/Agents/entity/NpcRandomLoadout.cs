// Hands an NPC one item, drawn at random from a list, and keeps every machine agreed on which.
//
// EntityInventoryComponent is local state: it is filled from `startingItems` in Awake on every
// machine, and nothing replicates it. That is fine while the list is fixed -- each peer builds the
// same bag -- and useless the moment the choice is random, because the server's roll and a
// client's roll disagree and the client draws a different gun in the NPC's hand, or none.
//
// So the roll happens once, on the server, and what replicates is not the roll but the RESULT:
// the id of whatever sits in the hand slot. A NetworkVariable rather than a message, because a
// late joiner has to see it too and change events do not replay. Clients mirror the slot from it,
// which also carries every LATER change to that slot -- a save restoring a different item, the
// loot table emptying the bag on death -- without this class knowing why the slot changed.
//
// Persistence: nothing of its own. The bag is EntityInventorySaveable's, and a restored bag simply
// wins: the roll only fills the slot when it is empty, and the restore lands afterwards through
// the same slot-changed path the roll uses. Caravan members are not saved at all
// (NpcSpawn.Create disowns them), so a group member's roll is seeded by its group (GroupMembership)
// instead: the same caravan comes back with the same guns after every refold. A hand-placed NPC
// still rolls at random.
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(EntityInventoryComponent))]
    public class NpcRandomLoadout : NetworkBehaviour
    {
        // Offsets the member index for the gun roll. The roster drew this member's prefab from the same
        // (seed, index) hash, so without it who you are and what you carry would come from one number.
        // Far above any plan index or rider index (GroupMembership.RiderIndexOffset), so a salted roll
        // never reuses the hash of another member's prefab draw either.
        private const int LoadoutSalt = 500000;

        [Tooltip("What this NPC may be carrying. One is picked at random when it spawns with an " +
                 "empty hand slot. Leave empty to carry nothing.")]
        [SerializeField] private InventoryItem[] candidates;

        [Tooltip("The inventory slot the pick goes into. Match EntityEquipmentController's " +
                 "starting slot so the item is drawn, not just carried.")]
        [SerializeField] private int slot;
        [Tooltip("Draw the pick once it lands in the slot. Off for a slot that is loot rather than " +
                 "a weapon -- the artifact a Clanker is carrying home -- so it stays in the bag and " +
                 "drops on death without ever replacing the gun in the hand.")]
        [SerializeField] private bool equipAfterRoll = true;

        // Server-written, everyone-read: the item id in `slot`, or empty for nothing.
        private readonly NetworkVariable<FixedString64Bytes> heldId = new(
            writePerm: NetworkVariableWritePermission.Server,
            readPerm: NetworkVariableReadPermission.Everyone);

        private EntityInventoryComponent inventory;
        private EntityEquipmentController equipment;

        private void Awake()
        {
            inventory = GetComponent<EntityInventoryComponent>();
            equipment = GetComponent<EntityEquipmentController>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                Roll();
                inventory.OnSlotChanged += PublishSlot;
                PublishSlot(slot, inventory.GetSlot(slot));
            }
            else
            {
                // Read once here as well as subscribing: a late joiner gets the current value
                // with the spawn and never sees a change event for it.
                heldId.OnValueChanged += MirrorSlot;
                Mirror(heldId.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (inventory != null) inventory.OnSlotChanged -= PublishSlot;
            heldId.OnValueChanged -= MirrorSlot;
        }

        // Offline there is no spawn and no peer to agree with; the roll still has to happen.
        private void Start()
        {
            if (!Network.IsNetworked) Roll();
        }

        private void Roll()
        {
            if (candidates == null || candidates.Length == 0) return;

            InventorySlot current = inventory.GetSlot(slot);
            if (current != null && !current.IsEmpty) return;

            // A group member draws from its group's seed, so a caravan that folds and re-spawns comes
            // back carrying the same guns. A hand-placed NPC has no group and still rolls freely.
            int index = TryGetComponent(out GroupMembership membership) && membership.Group != null
                ? RosterDraw.IndexFor(membership.Group.RosterSeed, membership.MemberIndex + LoadoutSalt, candidates.Length)
                : Random.Range(0, candidates.Length);

            InventoryItem pick = candidates[index];
            if (pick == null) return;

            inventory.RestoreSlot(slot, pick);
            if (equipAfterRoll && equipment != null) equipment.EquipSlot(slot);
        }

        private void PublishSlot(int index, InventorySlot changed)
        {
            if (index != slot) return;
            string id = changed != null && !changed.IsEmpty ? changed.Item.ID : string.Empty;
            heldId.Value = new FixedString64Bytes(id ?? string.Empty);
        }

        private void MirrorSlot(FixedString64Bytes previous, FixedString64Bytes next) => Mirror(next);

        private void Mirror(FixedString64Bytes value)
        {
            string id = value.ToString();
            InventoryItem item = null;

            if (!string.IsNullOrEmpty(id))
            {
                item = Registry<InventoryItem>.Get(id);
                if (item == null)
                {
                    Debug.LogWarning($"[NpcRandomLoadout] '{name}' is holding '{id}' on the server " +
                                     "but this machine has no such item registered; the hand stays " +
                                     "empty here. Play from Bootstrap so the item registry exists.", this);
                }
            }

            InventorySlot current = inventory.GetSlot(slot);
            if (current != null && current.Item == item) return;

            inventory.RestoreSlot(slot, item);
            if (equipAfterRoll && equipment != null) equipment.EquipSlot(slot);
        }
    }
}
