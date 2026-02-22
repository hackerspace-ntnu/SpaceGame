using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Controller responsible for equipping and unequipping items.
/// It instantiates the item's prefab and attaches it to the player's hand socket when equipped,
/// and destroys it when unequipped.
///
/// Physocs are disabled on equippped items. 
/// </summary>
public class EquipmentController : NetworkBehaviour
{
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private Transform handSocket;
    private GameObject currentObject;

    private void Awake()
    {
        playerInventory.OnSlotSelected += OnSlotSelected;
        playerInventory.OnInventoryChanged += OnInventoryChanged;
    }
    
    public override void OnDestroy()
    {
        if (!playerInventory) return;
        playerInventory.OnSlotSelected -= OnSlotSelected;
        playerInventory.OnInventoryChanged -= OnInventoryChanged;
    }
    
    private void OnSlotSelected(int index)
    {
        if(!IsOwner) return;
        InventorySlot slot = playerInventory.GetSlot(index);
        if (slot == null || !slot.Item)
        {
            Unequip();
            return;
        }
        
        Equip(slot.Item);
    }
    
    private void OnInventoryChanged()
    {
        if(!IsOwner) return;
        InventorySlot slot = playerInventory.GetSelectedSlot();
        if (slot == null || !slot.Item)
        {
            Unequip();
            return;
        }
        
        Equip(slot.Item);
    }

    public void Equip(InventoryItem item)
    {
      EquipServerRpc(item.itemId);
    }
    
    [ServerRpc]
    private void EquipServerRpc(string itemId)
    {
        InventoryItem item = GameManager.Instance.GetItem(itemId);
        Unequip();

        if (!item.itemPrefab)
            return;
        
        currentObject = Instantiate(item.itemPrefab,
            handSocket.position,
            handSocket.rotation,
            handSocket
        );
        
        var networkObject = currentObject.GetComponent<NetworkObject>();
        networkObject.Spawn(true);
        networkObject.TrySetParent(NetworkObject);
        
        Rigidbody rb = currentObject.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        
        Collider itemCollider = currentObject.GetComponent<Collider>();
        if (itemCollider)
        {
            itemCollider.enabled = false;
        }
    }

    public void Unequip()
    {
        UnequipServerRpc();
    }
    
    [ServerRpc]
    private void UnequipServerRpc()
    {
        if (currentObject)
        {
            var objectNetworkObject = currentObject.GetComponent<NetworkObject>();
            objectNetworkObject.Despawn();
            currentObject = null;
        }
    }

    public GameObject getCurrentObject() => currentObject;
}
