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
    
    
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
        playerInventory.OnSlotSelected += OnSlotSelected;
        playerInventory.OnInventoryChanged += OnInventoryChanged;
        InventorySlot slot = playerInventory.GetSelectedSlot();
        if (slot != null && slot.Item)
        {
            Equip(slot.Item);
        }
    }
    
    public override void OnDestroy()
    {
        if (!playerInventory && !IsOwner) return;
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
        EquipServerRpc(item.ItemId);
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
        
        EquipClientRpc(networkObject);
        
    }

    [Rpc(SendTo.Owner)]
    private void EquipClientRpc(NetworkObjectReference itemReference)
    {
        var networkObject = itemReference.TryGet(out NetworkObject itemNetworkObject) ? itemNetworkObject.gameObject : null;
        if (!networkObject) return;
        currentObject = networkObject;
        
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
        
        DropItemPhysics dropItemPhysics = currentObject.GetComponent<DropItemPhysics>();
        if (dropItemPhysics)
        {
            dropItemPhysics.Throw();
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
            UnequipClientRpc();
        }
    }
    
    [Rpc(SendTo.Owner)]
    private void UnequipClientRpc()
    {
        if (currentObject)
        {
            currentObject = null;
        }
    }

    public GameObject getCurrentObject() => currentObject;
}
