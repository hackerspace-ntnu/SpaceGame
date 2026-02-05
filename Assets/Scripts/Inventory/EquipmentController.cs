using UnityEngine;

public class EquipmentController : MonoBehaviour
{
    [SerializeField] private Transform handSocket;
    private GameObject currentObject;

    public void Equip(InventoryItem item)
    {
        Unequip();

        if (item.itemPrefab == null)
            return;
        
        currentObject = Instantiate(item.itemPrefab,
            handSocket.position,
            handSocket.rotation,
            handSocket
        );
        
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
        if (currentObject != null)
        {
            Destroy(currentObject);
            currentObject = null;
        }
    }
}
