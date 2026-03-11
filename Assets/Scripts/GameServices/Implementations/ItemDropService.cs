using UnityEngine;

public class PlayerDropService : IItemDropService
{
    public void DropItem(Transform origin, InventoryItem item)
    {
        Vector3 dropPos =
            origin.position +
            origin.forward * 1.2f +
            Vector3.up * 0.5f;

        GameObject obj = Object.Instantiate(
            item.itemPrefab,
            dropPos,
            Quaternion.identity
        );

        ApplyForce(origin, obj);
    }

    private void ApplyForce(Transform origin, GameObject droppedItem)
    {
        Rigidbody rb = droppedItem.GetComponent<Rigidbody>();

        if (rb == null)
            return;

        rb.isKinematic = false;

        Vector3 force =
            origin.forward * 1.5f +
            Vector3.up * 1.0f;

        rb.AddForce(force, ForceMode.Impulse);
    }
}
