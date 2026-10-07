using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// What the crew are carrying, for steps that point at gear: does anybody have this item, and where on the ship's walls is
    /// one lying? Read off what already replicates (the hotbar's item ids, each pack's and wall's contents), so every machine
    /// answers the same.
    /// </summary>
    public static class CrewGear
    {
        /// <summary>Is <paramref name="item"/> in any crew member's hotbar or pack?</summary>
        public static bool AnyoneCarries(ObjectiveWorld world, InventoryItem item)
        {
            if (item == null) return false;

            foreach (PlayerIdentity member in world.Crew)
                if (member != null && Carries(member.gameObject, item)) return true;

            return false;
        }

        /// <summary>Is <paramref name="item"/> in <paramref name="body"/>'s hotbar or anywhere on its pack?</summary>
        public static bool Carries(GameObject body, InventoryItem item)
        {
            if (body == null || item == null) return false;

            if (body.TryGetComponent(out PlayerController controller) && controller.PlayerInventory != null)
            {
                IPlayerInventory hotbar = controller.PlayerInventory;
                for (int i = 0; i < hotbar.GetInventorySize(); i++)
                {
                    InventorySlot slot = hotbar.GetSlot(i);
                    if (slot != null && slot.Item == item) return true;
                }
            }

            PackContainer pack = PackOf(body);
            return pack != null && pack.Holds(item.ID);
        }

        /// <summary>The middle of the first wall on <paramref name="ship"/> holding <paramref name="item"/>.</summary>
        public static bool TryFindOnWall(Component ship, InventoryItem item, out Vector3 position)
        {
            position = default;
            if (ship == null || item == null) return false;

            foreach (WallInventory wall in ship.GetComponentsInChildren<WallInventory>(true))
            {
                if (!wall.Holds(item.ID)) continue;

                position = wall.TryGetComponent(out Collider face) ? face.bounds.center : wall.transform.position;
                return true;
            }

            return false;
        }

        /// <summary>The pack <paramref name="body"/> is wearing, if any.</summary>
        public static PackContainer PackOf(GameObject body)
        {
            BackpackController backpack = body != null ? body.GetComponentInChildren<BackpackController>(true) : null;
            return backpack != null ? backpack.Pack : null;
        }
    }
}
