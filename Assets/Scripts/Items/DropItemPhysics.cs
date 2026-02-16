using UnityEngine;
using System.Collections;

public class DropItemPhysics : MonoBehaviour
{
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Collider triggerCollider;
    private bool physicsEnabled = false;
    [SerializeField] private LayerMask groundLayer;

    private void OnTriggerEnter(Collider other)
    {
        if ((groundLayer.value & (1 << other.gameObject.layer)) == 0) return;
        
        triggerCollider.enabled = false;
        
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        
        RaycastHit hit;
        if (Physics.Raycast(transform.position, Vector3.down, out hit, 0.5f, groundLayer))
            transform.position = hit.point;
    }

    private void DisablePhysics()
    {
        Debug.Log("Disabling Physics");
        physicsEnabled = false;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
    }
}
