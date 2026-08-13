// Carries Rigidbodies that are standing on the walker along with it.
//
// The walker moves by writing transform.position/rotation directly (DesertCrawlerLocomotion),
// not through physics. A transform-driven collider imparts NO friction or momentum to a
// Rigidbody resting on it, so a player standing on the deck is simply left behind as the
// deck slides out from under them — nothing to do with the player's own movement code.
//
// Fix: measure the platform's own delta each frame and apply the same delta to every rider
// inside the carry volume, including the rotation about the platform's pivot so riders turn
// with the hull instead of being flung sideways.
//
// Riders are found by asking the carry volume what is inside it, NOT by OnTriggerEnter/Stay.
// Unity delivers trigger messages to the GameObject holding the collider and to the one holding
// that collider's attachedRigidbody, and to nothing else. The volume is a child of the hull, so
// with the messages the carrier only ever heard about a rider when the craft happened to have a
// Rigidbody for the volume to attach to. The DesertCrawler has a kinematic one at its root and
// worked; the DuneFoil has none, so its rider set stayed empty for the entire session and the
// deck sailed out from under anybody standing on it. An overlap query has no such hidden
// requirement, and it drops riders who walk off without needing an exit message either.
//
// Runs after DesertCrawlerLocomotion (order 100) so the platform has already moved.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Vehicles.Crawler;

namespace SpaceGame.Vehicles
{
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
        private readonly HashSet<Rigidbody> claimed = new HashSet<Rigidbody>();
        private Collider[] overlapBuffer = new Collider[32];
        private Vector3 lastPos;
        private Quaternion lastRot;
        private bool primed;

        public int RiderCount => riders.Count;

        /// <summary>
        /// Stop carrying this rider; something else is placing them each frame.
        ///
        /// A station that holds a player in one spot — the dune foiler's helm pins whoever is
        /// steering to the wheel — is already positioning them relative to the hull, so the hull's
        /// motion is baked into where it puts them. Carrying them as well applies that motion
        /// twice, and the second copy accumulates: the helmsman drifts steadily off the stern at
        /// the craft's own speed.
        ///
        /// They stay in <see cref="RiderCount"/>. They are still aboard — the mooring must still
        /// see somebody on the deck — they are simply not this component's to move.
        /// </summary>
        public void ClaimRider(Rigidbody rider)
        {
            if (rider != null) claimed.Add(rider);
        }

        /// <summary>Hand a claimed rider back. Safe to call for one that was never claimed.</summary>
        public void ReleaseRider(Rigidbody rider)
        {
            if (rider != null) claimed.Remove(rider);
        }

        private void Awake()
        {
            if (carryVolume == null) carryVolume = CreateDefaultVolume();
            Prime();
        }

        /// <summary>
        /// Take the platform's current pose as the baseline, so the next carry moves riders by the
        /// motion since now. Call after teleporting the craft, or nobody aboard survives the jump.
        /// </summary>
        public void Prime()
        {
            lastPos = transform.position;
            lastRot = transform.rotation;
            primed = true;
        }

        /// <summary>Point the carrier at its trigger volume. Used by the prefab builders and tests.</summary>
        public void BindCarryVolume(Collider volume) => carryVolume = volume;

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

        /// <summary>
        /// Rebuild the rider set from whatever is standing in the carry volume right now.
        ///
        /// Rebuilt rather than accumulated: it is the same query that adds someone who steps
        /// aboard and drops someone who steps off, so there is no state to get stuck.
        /// </summary>
        private void CollectRiders()
        {
            riders.Clear();
            if (carryVolume == null) return;

            int count = Overlap();
            for (int i = 0; i < count; i++)
            {
                Collider other = overlapBuffer[i];
                if (other == null) continue;

                Rigidbody rb = other.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                if (rb.transform.IsChildOf(transform)) continue;   // never carry our own parts
                riders.Add(rb);
            }
        }

        /// <summary>
        /// Everything inside the volume. Boxes are queried in their own orientation rather than by
        /// world bounds, so a heeled or yawed hull does not sweep up the sand beside it.
        /// </summary>
        private int Overlap()
        {
            while (true)
            {
                int count;
                if (carryVolume is BoxCollider box)
                {
                    Transform t = box.transform;
                    Vector3 centre = t.TransformPoint(box.center);
                    Vector3 scale = t.lossyScale;
                    Vector3 half = new Vector3(Mathf.Abs(box.size.x * scale.x),
                                               Mathf.Abs(box.size.y * scale.y),
                                               Mathf.Abs(box.size.z * scale.z)) * 0.5f;
                    count = Physics.OverlapBoxNonAlloc(centre, half, overlapBuffer, t.rotation,
                                                       ~0, QueryTriggerInteraction.Ignore);
                }
                else
                {
                    Bounds b = carryVolume.bounds;
                    count = Physics.OverlapBoxNonAlloc(b.center, b.extents, overlapBuffer,
                                                       Quaternion.identity, ~0,
                                                       QueryTriggerInteraction.Ignore);
                }

                // A saturated buffer means riders were silently dropped, which reads in game as
                // one person on a crowded deck being left behind. Grow and ask again.
                if (count < overlapBuffer.Length) return count;
                overlapBuffer = new Collider[overlapBuffer.Length * 2];
            }
        }

        private void LateUpdate() => CarryRiders();

        /// <summary>
        /// Move everyone aboard by the platform's motion since the last call. Public and
        /// frame-driven so it can be stepped directly by a test, the way the locomotion is.
        /// </summary>
        public void CarryRiders()
        {
            if (!primed) { Prime(); return; }

            Vector3 deltaPos = transform.position - lastPos;
            Quaternion deltaRot = transform.rotation * Quaternion.Inverse(lastRot);
            lastPos = transform.position;
            lastRot = transform.rotation;

            // Refreshed every frame, whether or not the craft moved: RiderCount is what tells the
            // rest of the game somebody is aboard, and a parked craft must still report its crew.
            CollectRiders();
            if (riders.Count == 0) return;

            bool moved = deltaPos.sqrMagnitude > 1e-10f || Quaternion.Angle(deltaRot, Quaternion.identity) > 1e-4f;
            if (!moved) return;

            foreach (Rigidbody rb in riders)
            {
                if (claimed.Contains(rb)) continue;
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
}
