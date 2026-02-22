using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ScriptableObject representing an item that can be stored in the inventory. Contains data about the item such as its name, prefab, and icon.
/// </summary>
[CreateAssetMenu(menuName = "Items/Item")]
public class InventoryItem : ScriptableObject
{
    [Tooltip("Unique identifier for the item. ")]
    public int itemId;
    
    [Tooltip("Display name of the item")]
    public string itemName = "NewItem";

    [Tooltip("Prefab that will be instantiated and equipped when this item is selected.")]
    public GameObject itemPrefab;

    [Tooltip("Optional icon for UI display.")]
    public Sprite icon;
}
