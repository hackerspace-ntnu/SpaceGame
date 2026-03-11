
using System;
using UnityEngine;

/// <summary>
/// Controller responsible for equipping and unequipping items.
/// It instantiates the item's prefab and attaches it to the player's hand socket when equipped,
/// and destroys it when unequipped.
///
/// Physocs are disabled on equippped items. 
/// </summary>
public class EquipmentController : MonoBehaviour, IEquipHandler
{
    [SerializeField] private Transform handSocket;

    private EquipItemSocket equipmentSocket;
    
    private IPlayerInventory inventory;
    
    private GameObject equippedItemObject;

    private void Awake()
    {
        equipmentSocket = new EquipItemSocket(handSocket);
    }

    private void Start()
    {
        inventory = GetComponent<PlayerController>().PlayerInventory;
        inventory.OnSlotSelected += Equip;
        inventory.OnItemDropped += item => GameServices.ItemDropService.DropItem(handSocket, item);
        
        PlayerInputManager input = GetComponent<PlayerController>().Input;
        input.OnUsePressed += OnUse;
    }

    public void Equip(InventorySlot slot)
    {
        if (slot == null || slot.IsEmpty)
        {
            Unequip(); 
            return;
        }
        
        equippedItemObject = equipmentSocket.Equip(slot.Item.itemPrefab);
        var useableItem = equippedItemObject.GetComponent<UsableItem>();

        if (useableItem)
        {
            useableItem.OnItemDepleted += ItemDepleted;
        }
    }

    private void ItemDepleted(UsableItem item)
    {
        item.OnItemDepleted -= ItemDepleted;
        inventory.TryRemoveItem(inventory.SelectedSlotIndex);
        Unequip();
    }

    public void Unequip()
    {
        equipmentSocket.Unequip();
        equippedItemObject = null;
    }

    private void OnUse()
    {
        if(!equippedItemObject) return;
        UsableItem usable = equippedItemObject.GetComponent<UsableItem>();
        
        if(!usable) return;
        
        usable.TryUse();
    }
}
