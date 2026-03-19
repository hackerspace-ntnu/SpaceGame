
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

