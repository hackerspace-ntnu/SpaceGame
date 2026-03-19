using System;
using UnityEngine;

public class EquipmentController : MonoBehaviour
{
    [SerializeField] private Transform handSocket;

    private EquipItemSocket equipmentSocket;
    private IPlayerInventory inventory;
    
    private GameObject equippedItemObject;

    public event Action<InventoryItem> OnEquipRequested;

    private void Awake()
    {
        equipmentSocket = new EquipItemSocket(handSocket);
    }

    private void Start()
    {
        var player = GetComponent<PlayerController>();
        inventory = player.PlayerInventory;

        inventory.OnSlotSelected += HandleEquip;
        inventory.OnSlotChanged += OnSlotChanged;
        inventory.OnItemDropped += OnItemDropped;

        player.Input.OnUsePressed += OnUse;
    }

    private void OnSlotChanged(int index, InventorySlot slot)
    {
        if (inventory.SelectedSlotIndex != index) return;
        HandleEquip(slot);
    }

    private void HandleEquip(InventorySlot slot)
    {
        if (slot == null || slot.IsEmpty)
        {
            Unequip();
            return;
        }
        
        OnEquipRequested?.Invoke(slot.Item);
        Equip(slot.Item);
    }


    public void Equip(InventoryItem item)
    {
        Unequip();

        equippedItemObject = equipmentSocket.Equip(item.itemPrefab);

        var usableItem = equippedItemObject.GetComponent<UsableItem>();
        if (usableItem)
        {
            usableItem.OnItemDepleted += ItemDepleted;
        }
    }

    private void Unequip()
    {
        if (equippedItemObject)
        {
            var usable = equippedItemObject.GetComponent<UsableItem>();
            if (usable)
                usable.OnItemDepleted -= ItemDepleted;
        }

        equipmentSocket.Unequip();
        equippedItemObject = null;
    }

    private void ItemDepleted(UsableItem item)
    {
        item.OnItemDepleted -= ItemDepleted;
        inventory.TryRemoveItem(inventory.SelectedSlotIndex);
        Unequip();
    }

    private void OnUse()
    {
        if (!equippedItemObject) return;

        var usable = equippedItemObject.GetComponent<UsableItem>();
        if (!usable) return;
        
        var player = gameObject;
        
        usable.TryUse(player);
    }

    private void OnItemDropped(InventoryItem item)
    {
        GameServices.ItemDropService.DropItem(handSocket, item);
    }
}
