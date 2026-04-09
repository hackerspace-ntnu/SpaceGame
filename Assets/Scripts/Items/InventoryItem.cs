using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// ScriptableObject representing an item that can be stored in the inventory. Contains data about the item such as its name, prefab, and icon.
/// </summary>
[CreateAssetMenu(menuName = "Items/Item")]
public class InventoryItem : ScriptableObject, IRegistryEntry
{
    public string ID { get; set; }
    
    [Tooltip("Display name of the item")]
    public string itemName = "NewItem";

    [Tooltip("Prefab that will be instantiated and equipped when this item is selected.")]
    public GameObject itemPrefab;

    [Tooltip("Optional icon for UI display.")]
    public Sprite icon;
    
#if UNITY_EDITOR
    private void OnValidate()
    {
        string path = AssetDatabase.GetAssetPath(this);
        string guid = AssetDatabase.AssetPathToGUID(path);

        if (ID != guid)
        {
            ID = guid;
            EditorUtility.SetDirty(this);
        }
    }
#endif
}
