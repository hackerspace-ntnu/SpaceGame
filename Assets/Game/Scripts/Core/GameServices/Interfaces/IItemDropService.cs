using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Core
{
    public interface IItemDropService
    {
        void DropItem(Transform origin, InventoryItem item);
    }
}
