using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Core.Persistence;

namespace SpaceGame.Core
{
    public class RegistryLoader : MonoBehaviour
    {
        void Awake()
        {
            LoadItems();

            // After the items, never before: half the saveable-prefab table is derived from them.
            SaveablePrefabRegistry.LoadAll();
        }

        void LoadItems()
        {
            InventoryItem[] items = Resources.LoadAll<InventoryItem>("Items");

            foreach (var item in items)
            {
                Registry<InventoryItem>.Register(item);
            }

            Debug.Log($"Registered {items.Length} items.");
        }
    }
}
