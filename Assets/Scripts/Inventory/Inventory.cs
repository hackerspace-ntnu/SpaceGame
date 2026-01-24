using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight inventory that knows how to spawn and swap Equipment prefabs.
/// Attach to the player (or any entity that can hold items).
/// </summary>
public class Inventory : MonoBehaviour
{
    [SerializeField] private Transform equipmentSocket;
    [SerializeField] private int maxSlots = 4;
    
    [Header("Starting Items")]
    [SerializeField] private List<InventoryItemDefinition> startingItems = new();

    private readonly List<InventoryItemDefinition> _items = new();
    private GameObject _equippedRoot;
    private InventoryItemDefinition _equippedDefinition;

    public event Action<InventoryItemDefinition> ItemAdded;
    public event Action<InventoryItemDefinition> ItemRemoved;
    public event Action<InventoryItemDefinition> Equipped;
    public event Action<InventoryItemDefinition> Unequipped;

    public IReadOnlyList<InventoryItemDefinition> Items => _items;
    public InventoryItemDefinition EquippedDefinition => _equippedDefinition;

    private void Awake()
    {
        
        if (startingItems != null)
        {
            for (int i = 0; i < startingItems.Count; i++)
            {
                var def = startingItems[i];
                if (!def) continue;
                TryAddItem(def, out _, autoEquip: false);
            }
        }
    }

    public bool TryAddItem(InventoryItemDefinition definition, out int slot, bool autoEquip = true)
    {
        slot = -1;
        if (!definition)
        {
            Debug.LogWarning($"[{nameof(Inventory)}] Tried to add a null definition to {name}.");
            return false;
        }

        if (maxSlots > 0 && _items.Count >= maxSlots)
        {
            Debug.Log($"[{nameof(Inventory)}] {name} inventory is full.");
            return false;
        }

        _items.Add(definition);
        slot = _items.Count - 1;
        ItemAdded?.Invoke(definition);

        return true;
    }
    
    
    
}
