using UnityEngine;

public class RegistryLoader : MonoBehaviour
{
    void Awake()
    {
        LoadItems();
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
