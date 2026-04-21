
using UnityEngine;

public class EquipItemSocket
{
    private Transform socket;
    private GameObject currentObject;

    public EquipItemSocket(Transform socket)
    {
        this.socket = socket;
    }

    public GameObject Equip(GameObject prefab)
    {
        Unequip();

        if (!prefab)
            return null;

        currentObject = Object.Instantiate(
            prefab,
            socket.position,
            socket.rotation,
            socket
        );

        Setup(currentObject);

        // If this is a weapon with a Handle1, position the weapon so Handle1 is at the socket
        Weapon weapon = currentObject.GetComponent<Weapon>();
        if (weapon != null && weapon.Handle1 != null)
        {
            // Offset the weapon's root so that Handle1 aligns with the socket position
            Vector3 offsetFromRoot = currentObject.transform.position - weapon.Handle1.position;
            currentObject.transform.position = socket.position + offsetFromRoot;
        }

        return currentObject;
    }

    public void Unequip()
    {
        if (currentObject)
        {
            Object.Destroy(currentObject);
            currentObject = null;
        }
    }

    private void Setup(GameObject obj)
    {
        // Don't modify physics on pickup objects (they have PickupableItem component)
        if (obj.GetComponent<PickupableItem>() != null)
        {
            return;
        }

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        Collider col = obj.GetComponent<Collider>();
        if (col)
            col.enabled = false;
    }
}

