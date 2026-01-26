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
