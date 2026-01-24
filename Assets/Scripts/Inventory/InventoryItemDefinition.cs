using UnityEngine;

[CreateAssetMenu(menuName = "Inventory/Item Definition")]
public class InventoryItemDefinition : ScriptableObject
{
    [Tooltip("Unique identifier for the item. ")]
    public string itemId = "NewItem";

    [Tooltip("Prefab that will be instantiated and equipped when this item is selected.")]
    public GameObject equipmentPrefab;

    [Tooltip("Optional icon for UI display.")]
    public Sprite icon;
}
