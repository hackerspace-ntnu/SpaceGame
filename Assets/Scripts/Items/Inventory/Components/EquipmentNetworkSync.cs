using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(EquipmentController))]
public class EquipmentNetworkSync : NetworkBehaviour
{
    private EquipmentController controller;

    private void Awake()
    {
        controller = GetComponent<EquipmentController>();
    }

    private void OnEnable()
    {
        controller.OnEquipRequested += RequestEquip;
    }

    private void OnDisable()
    {
        controller.OnEquipRequested -= RequestEquip;
    }

    private void RequestEquip(InventoryItem item)
    {
        if (!IsOwner) return;

        EquipServerRpc(item.ID);
    }

    [Rpc(SendTo.Server)]
    private void EquipServerRpc(string itemID)
    {
        var item = Registry<InventoryItem>.Get(itemID);
        EquipClientRpc(itemID);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void EquipClientRpc(string itemID)
    {
        var item = Registry<InventoryItem>.Get(itemID);
        
        if (IsOwner) return;

        controller.Equip(item);
    }
}
