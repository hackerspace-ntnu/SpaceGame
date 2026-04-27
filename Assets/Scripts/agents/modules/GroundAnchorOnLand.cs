// Freezes a Rigidbody the first time it touches anything on `groundMask`.
// Used by deployable turrets so they fall, land, and then stay put.
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GroundAnchorOnLand : MonoBehaviour
{
    [Tooltip("Layers that count as 'ground' for anchoring.")]
    [SerializeField] private LayerMask groundMask = ~0;
    [Tooltip("If true, rotate so the local up axis matches the surface normal on landing.")]
    [SerializeField] private bool alignToSurface = false;
    [Tooltip("Safety: anchor anyway after this many seconds even without a contact.")]
    [SerializeField] private float maxFallTime = 5f;

    private Rigidbody rb;
    private bool anchored;
    private float timer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (anchored) return;
        timer += Time.deltaTime;
        if (timer >= maxFallTime)
            Anchor(transform.up);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (anchored) return;
        if (((1 << collision.gameObject.layer) & groundMask.value) == 0) return;

        Vector3 normal = collision.contacts.Length > 0 ? collision.contacts[0].normal : Vector3.up;
        Anchor(normal);
    }

    private void Anchor(Vector3 surfaceNormal)
    {
        anchored = true;

        if (alignToSurface && surfaceNormal.sqrMagnitude > 0.0001f)
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, surfaceNormal);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(Vector3.forward, surfaceNormal);
            transform.rotation = Quaternion.LookRotation(forward.normalized, surfaceNormal);
        }

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
    }
}
