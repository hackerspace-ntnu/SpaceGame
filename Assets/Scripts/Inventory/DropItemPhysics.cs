using UnityEngine;

/// <summary>
///  Handles the physics of dropped items, such as when an item is dropped from the inventory.
/// It will disable physics when it collides with the ground to prevent it from being pushed around.
/// </summary>
public class DropItemPhysics : MonoBehaviour
{
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Collider triggerCollider;
    [SerializeField] private LayerMask groundLayer;
    
    public void Throw()
    {
        triggerCollider.enabled = true;
        rb.isKinematic = false;
    }

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
}
