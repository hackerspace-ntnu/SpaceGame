// Puts a resident's profession in its hands: the archetype's heldItem is drawn in the fist and its
// beltItems hang on the belt (BeltCarrier). The item its chore carries is kept in the bag too, out of sight
// until the errand picks it up (ResidentPresence puts it in the hand).
//
// The Raxy prefab is shared by every profession, so what a resident carries cannot live on the
// prefab; it is read off the archetype the settlement assigned. That assignment is serialized on the
// instance, so every machine reads the same one and builds the same bag with no message.
//
// Runs before EntityEquipmentController.Start (which draws slot 0), and only fills an EMPTY bag: a
// restored save writes the bag itself and must win.
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Agents.Residents
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Resident), typeof(EntityInventoryComponent))]
    public class ResidentCarry : MonoBehaviour
    {
        private void Start()
        {
            var resident = GetComponent<Resident>();
            var inventory = GetComponent<EntityInventoryComponent>();
            if (resident.archetype == null || !IsEmpty(inventory)) return;

            if (resident.archetype.heldItem != null) inventory.TryAddItem(resident.archetype.heldItem);
            foreach (InventoryItem item in resident.archetype.beltItems)
                if (item != null) inventory.TryAddItem(item);
            ChoreDefinition chore = resident.archetype.chore;
            if (chore != null && chore.carried != null) inventory.TryAddItem(chore.carried);
        }

        private static bool IsEmpty(EntityInventoryComponent inventory)
        {
            for (int i = 0; i < inventory.Size; i++)
                if (!inventory.GetSlot(i).IsEmpty) return false;
            return true;
        }
    }
}
