
using UnityEngine;
using Unity.Netcode;

public class NetworkEquipmentComponent : NetworkBehaviour, IEquipHandler
{
    [SerializeField] private Transform handSocket;

    private EquipItemSocket equipment;

    private void Awake()
    {
        equipment = new EquipItemSocket(handSocket);
    }

    public void Equip(InventorySlot slot)
    {
        if (!IsOwner) return;

        EquipServerRpc(slot.Item.ID);
    }

    [ServerRpc]
    private void EquipServerRpc(string itemID)
    {
        InventoryItem item = Registry<InventoryItem>.Get(itemID);

        var obj = equipment.Equip(item.itemPrefab);

        if (!obj) return;

        var netObj = obj.GetComponent<NetworkObject>();
        netObj.Spawn(true);
        netObj.TrySetParent(NetworkObject);
    }

    public void Unequip()
    {
        if (!IsOwner) return;

        UnequipServerRpc();
    }

    [ServerRpc]
    private void UnequipServerRpc()
    {
        equipment.Unequip();
    }
}
