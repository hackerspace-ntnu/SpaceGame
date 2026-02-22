using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// ScriptableObject representing an item that can be stored in the inventory. Contains data about the item such as its name, prefab, and icon.
/// </summary>
[CreateAssetMenu(menuName = "Items/Item")]
public class InventoryItem : ScriptableObject
{
    [Tooltip("Unique identifier for the item. ")]
    [SerializeField, HideInInspector] private string itemId;
    public string ItemId => itemId;
    
    [Tooltip("Display name of the item")]
    public string itemName = "NewItem";

    [Tooltip("Prefab that will be instantiated and equipped when this item is selected.")]
    public GameObject itemPrefab;

    [Tooltip("Optional icon for UI display.")]
    public Sprite icon;
    
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(itemId))
        {
            string path = AssetDatabase.GetAssetPath(this);
            itemId = AssetDatabase.AssetPathToGUID(path);
            EditorUtility.SetDirty(this);
        }
    }
#endif
}
