// Carries Rigidbodies that are standing on the walker along with it.
//
// The walker moves by writing transform.position/rotation directly (SpiderWalkerLocomotion),
// not through physics. A transform-driven collider imparts NO friction or momentum to a
// Rigidbody resting on it, so a player standing on the deck is simply left behind as the
// deck slides out from under them — nothing to do with the player's own movement code.
//
// Fix: measure the platform's own delta each frame and apply the same delta to every rider
// inside the carry volume, including the rotation about the platform's pivot so riders turn
// with the hull instead of being flung sideways.
//
// Runs after SpiderWalkerLocomotion (order 100) so the platform has already moved.
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(200)]
public class WalkerPlatformCarrier : MonoBehaviour
{
    [Header("Carry volume")]
    [Tooltip("Trigger collider covering the walkable areas. Anything with a Rigidbody inside " +
             "is carried. Auto-created to cover the hull if left empty.")]
    [SerializeField] private Collider carryVolume;

    [Header("Behaviour")]
    [Tooltip("Also rotate riders about the hull pivot, so they turn with the walker.")]
    [SerializeField] private bool carryRotation = true;
    [Tooltip("Turn the rider's own facing with the hull too. Off feels better in first person, " +
             "where having the view yanked around is disorienting.")]
    [SerializeField] private bool rotateRiderFacing;
    [Tooltip("Ignore riders that are moving away fast, so a jump off the deck is not fought.")]
    [SerializeField] private float maxCarrySpeed = 25f;

    private readonly HashSet<Rigidbody> riders = new HashSet<Rigidbody>();
    private Vector3 lastPos;
    private Quaternion lastRot;
    private bool primed;

    public int RiderCount => riders.Count;

    private void Awake()
    {
        if (carryVolume == null) carryVolume = CreateDefaultVolume();
        lastPos = transform.position;
        lastRot = transform.rotation;
        primed = true;
    }

    // Covers the superstructure: main deck, forward apron and roof terrace. Sized from the
    // renderers so it keeps working if the hull is re-authored.
    private Collider CreateDefaultVolume()
    {
        GameObject go = new GameObject("COL_CarryVolume");
        go.transform.SetParent(transform, false);

        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool any = false;
        foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }

        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        if (any)
        {
            Vector3 localCentre = transform.InverseTransformPoint(b.center);
            Vector3 localSize = transform.InverseTransformVector(b.size);
            localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
            // only the upper half matters; nobody stands on the legs
            bc.center = new Vector3(localCentre.x, localCentre.y + localSize.y * 0.22f, localCentre.z);
            bc.size = new Vector3(localSize.x * 1.05f, localSize.y * 0.62f, localSize.z * 1.05f);
        }
        else
        {
            bc.center = Vector3.zero;
            bc.size = new Vector3(30f, 12f, 26f);
        }
        return bc;
    }

    private void OnTriggerEnter(Collider other) => TryAdd(other);
    private void OnTriggerStay(Collider other) => TryAdd(other);

    private void OnTriggerExit(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb != null) riders.Remove(rb);
    }

    private void TryAdd(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null || rb.isKinematic) return;
        if (rb.transform.IsChildOf(transform)) return;   // never carry our own parts
        riders.Add(rb);
    }

    private void LateUpdate()
    {
        if (!primed) { lastPos = transform.position; lastRot = transform.rotation; primed = true; return; }

        Vector3 deltaPos = transform.position - lastPos;
        Quaternion deltaRot = transform.rotation * Quaternion.Inverse(lastRot);
        lastPos = transform.position;
        lastRot = transform.rotation;

        if (riders.Count == 0) return;
        bool moved = deltaPos.sqrMagnitude > 1e-10f || Quaternion.Angle(deltaRot, Quaternion.identity) > 1e-4f;
        if (!moved) return;

        riders.RemoveWhere(r => r == null);
        foreach (Rigidbody rb in riders)
        {
            if (rb.linearVelocity.sqrMagnitude > maxCarrySpeed * maxCarrySpeed) continue;

            Vector3 target = rb.position + deltaPos;
            if (carryRotation)
            {
                // swing the rider around the hull pivot by this frame's rotation
                Vector3 offset = rb.position - transform.position;
                target = transform.position + (deltaRot * offset) + deltaPos;
            }

            rb.position = target;
            if (rotateRiderFacing) rb.MoveRotation(deltaRot * rb.rotation);
        }
    }
}
