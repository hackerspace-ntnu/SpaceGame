using Unity.Netcode;
using UnityEngine;

public class PlayerDropService : IItemDropService
{
    public void DropItem(Transform origin, InventoryItem item)
    {
        Network.Execute(
            local: () => Drop(origin.position, origin.forward, item),
            client: () => DropServerRpc(origin.position, origin.forward, item.ID));
        
    }
    
    [Rpc(SendTo.Server)]
    private void DropServerRpc(Vector3 position, Vector3 direction, string itemId)
    {
        var item = Registry<InventoryItem>.Get(itemId);
        Drop(position, direction, item);
    }

    private void Drop(Vector3 position, Vector3 direction, InventoryItem item)
    {
        GameObject obj = Object.Instantiate(
            item.itemPrefab,
            position,
            Quaternion.identity
        );
        
        if (Network.IsNetworked)
        {
            obj.GetComponent<NetworkObject>().Spawn();
        }

        ApplyForce(direction, obj);
    }

    private void ApplyForce(Vector3 direction, GameObject droppedItem)
    {
        Rigidbody rb = droppedItem.GetComponent<Rigidbody>();

        if (rb == null)
            return;

        rb.isKinematic = false;

        Vector3 force =
            direction * 1.5f +
            Vector3.up * 1.0f;

        rb.AddForce(force, ForceMode.Impulse);
    }
}
