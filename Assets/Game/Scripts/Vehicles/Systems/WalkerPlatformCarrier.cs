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
// The carry itself runs on the physics clock (see FixedUpdate below), so it is not racing the
// locomotion for the platform's pose the way a render-loop carry would. The execution order still
// matters, but for the rider rather than the craft: PlayerMovement writes the player's velocity at
// the default order 0, and the carry has to be the last word on where that body is going this step.
//
// In a session the deck is a shared surface, and the carry has to obey the same rule every other
// system in this project obeys about somebody else's body: their machine moves it and the result
// replicates here. See CarryRiders for what that costs and CollectRiders for the census that must
// NOT follow the same rule.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
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
        ///
        /// This is a CENSUS, not a work list. <see cref="RiderCount"/> is what tells the rest of
        /// the game somebody is aboard — DuneFoilMooring holds the craft at its berth until it sees
        /// a crew — so it has to count everyone standing on the planks, including the people this
        /// machine is not allowed to move. Filtering those out here is what made the craft
        /// unsailable for a client: NetworkRigidbody makes every remote player's body kinematic, so
        /// a client who boarded a moored craft was invisible to the host's census, the host went on
        /// holding the hull still, and the boat simply refused to sail for anybody but the player
        /// whose machine happened to own it.
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
                if (rb == null) continue;
                if (rb.transform.IsChildOf(transform)) continue;   // never carry our own parts

                // Kinematic means furniture — a parked vehicle, a scenery prop, something bolted
                // down — and furniture is not crew. Unless it is not ours: a body this machine does
                // not own is kinematic here precisely BECAUSE it is somebody else's to simulate,
                // which is the definition of another player standing on the deck.
                if (rb.isKinematic && Network.Owns(rb)) continue;

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

        // On the physics clock, not the render clock, even though the platform moves on the render
        // clock. Riders are dynamic Rigidbodies, and a Rigidbody may only be posed from FixedUpdate:
        // driving this from LateUpdate wrote the rider's pose several times per physics step, each
        // write discarding the interpolation that smooths a 50 Hz simulation over a 240 Hz display.
        //
        // Reading the platform's pose a frame late costs nothing, because the carry is a delta.
        // Whatever the craft moved since the previous physics step is what gets applied at this one,
        // so nothing is dropped and nothing accumulates.
        private void FixedUpdate() => CarryRiders();

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

                // Somebody else's body. Their machine is carrying them on its own copy of this
                // deck and publishing the result, so carrying them here would be the second half
                // of a fight this machine cannot win: their NetworkTransform is owner-authoritative
                // and overwrites whatever is written here within a tick. Network.Owns answers true
                // offline and for anything unnetworked — a crate, a barrel, a test rig — so the
                // single-player carry is untouched.
                if (!Network.Owns(rb)) continue;

                if (rb.linearVelocity.sqrMagnitude > maxCarrySpeed * maxCarrySpeed) continue;

                Vector3 target = rb.position + deltaPos;
                if (carryRotation)
                {
                    // swing the rider around the hull pivot by this frame's rotation
                    Vector3 offset = rb.position - transform.position;
                    target = transform.position + (deltaRot * offset) + deltaPos;
                }

                // Still a direct pose write, and still the remaining half of the vibration. It is
                // NOT fixed by switching to MovePosition: that only defers to the physics step for
                // KINEMATIC bodies. On a dynamic one -- which every rider is, by the filter in
                // CollectRiders -- MovePosition applies immediately, exactly like this line, and
                // discards the interpolation just the same. Measured, not assumed.
                //
                // The fix is to express the carry as velocity and let the solver integrate it. That
                // needs somewhere to keep each rider's own motion apart from the carry, since the
                // carrier has no way to tell "the player's velocity was rewritten this step" from
                // "this crate kept the velocity we gave it" without it. See the ignored test in
                // WalkerPlatformCarrierTests for the contract that work has to satisfy.
                rb.position = target;
                if (rotateRiderFacing) rb.MoveRotation(deltaRot * rb.rotation);
            }
        }
    }
}
