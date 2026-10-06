// Decides WHEN an NPC uses the artifact it is carrying. EntityEquipmentController does the using.
//
// It drives an actual InventoryItem out of the NPC's own bag, which means the thing being fired is
// a real prefab the player can loot and fire themselves. The triggers, cadence, aim and facing are
// ItemUseModuleBase's; this says only which item — the one in the hand, drawn from slotIndex.
using UnityEngine;

namespace SpaceGame.Agents
{
    public class NpcItemUseModule : ItemUseModuleBase
    {
        [Header("What to use")]
        [Tooltip("Which inventory slot holds the artifact. The module equips it before using, so " +
                 "two of these on one NPC gives it a weapon it swaps to when the range suits.")]
        [SerializeField] private int slotIndex = 0;

        [Tooltip("Equip this slot when the trigger fires. Off means only use it if it already " +
                 "happens to be in hand.")]
        [SerializeField] private bool equipBeforeUse = true;

        private EntityEquipmentController equipment;

        protected override INpcItemUser User => equipment;

        protected override void Awake()
        {
            base.Awake();
            equipment = GetComponent<EntityEquipmentController>();

            if (equipment == null)
            {
                Debug.LogWarning($"{name}: NpcItemUseModule needs an EntityEquipmentController on " +
                                 "the same GameObject to hold anything. It will do nothing.", this);
            }
        }

        public override string ModuleDescription =>
            "Uses an artifact from this NPC's own EntityInventoryComponent — the same UsableItem " +
            "prefab the player would equip.\n\n" +
            "• slotIndex — which inventory slot to use; equipped automatically\n" +
            "• trigger — TargetInRange (guns), WhenHurt (stims), OnInterval (tools)\n" +
            "• minRange / maxRange — the band it fires in; maxRange also widens target acquisition\n" +
            "• leadSeconds / spreadDegrees — aim quality. Spread of 0 reads as scripted, not skilled.\n" +
            "• Claims the FACING channel, so the NPC keeps its gun on target while other modules walk it\n\n" +
            "Add two of these with different slots and ranges to give an NPC a weapon it swaps.";

        protected override bool PrepareUse()
        {
            if (!equipBeforeUse) return equipment.HasItem;

            if (equipment.EquippedSlotIndex != slotIndex)
                equipment.EquipSlot(slotIndex);

            return equipment.HasItem;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            slotIndex = Mathf.Max(0, slotIndex);
        }
    }
}
