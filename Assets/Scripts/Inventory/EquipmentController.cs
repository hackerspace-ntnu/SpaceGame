using UnityEngine;

/// <summary>
/// Controller responsible for equipping and unequipping items.
/// It instantiates the item's prefab and attaches it to the player's hand socket when equipped,
/// and destroys it when unequipped.
///
/// Physocs are disabled on equippped items. 
/// </summary>
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

    public GameObject getCurrentObject() => currentObject;
}
