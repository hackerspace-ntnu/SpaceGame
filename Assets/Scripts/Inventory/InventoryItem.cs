using UnityEngine;

[CreateAssetMenu(menuName = "Inventory/Item Definition")]
public class InventoryItem : ScriptableObject
{
    [Tooltip("Unique identifier for the item. ")]
    public string itemName = "NewItem";

    [Tooltip("Prefab that will be instantiated and equipped when this item is selected.")]
    public GameObject itemPrefab;

    [Tooltip("Optional icon for UI display.")]
    public Sprite icon;
}