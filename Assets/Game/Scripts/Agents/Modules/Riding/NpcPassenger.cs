// Puts an NPC on the back of a mount, without going anywhere near MountModule.
//
// MountModule cannot do this. Its rider contract is PlayerMovement — it takes a camera, suppresses
// the mount's AI so a human can steer, and hands NetworkObject ownership to the rider's client. All
// three are exactly wrong for a caravan animal carrying a trader across the map, and generalising
// it would mean reworking a large, netcode-sensitive, save-persisted class with known teardown
// traps for a case that shares none of its requirements.
//
// So this is the other model: a mounted NPC is ONE agent, not two. The mount is the agent — it has
// the motor, the NavMeshAgent, the formation slot, the task. The rider is a passenger: parented to
// the saddle with its own drivers switched off, along for the journey. Half the agents, no
// arbitration between a rider's AI and its mount's, and the rider is still a full NPC the moment it
// gets off.
//
// Online, only the authority seats anyone. Netcode then carries the whole arrangement by itself:
// it replicates the rider's spawn, it replicates the parenting, and NetAuthority switches the
// arriving copy's brain off on the machines that are only watching — which is the same suppression
// this class applies by hand where it is in charge. So there is no message to send and nothing for
// a late joiner to miss, and the one rule that has to hold is that a client never seats anybody.
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class NpcPassenger : MonoBehaviour
    {
        [Header("Rider")]
        [Tooltip("Who rides this. Spawned at start when spawnOnStart is on; otherwise call " +
                 "Seat() with an NPC that already exists.")]
        [SerializeField] private GameObject riderPrefab;

        [Tooltip("Where they sit. Falls back to this transform if empty, which will look wrong — " +
                 "assign a saddle child.")]
        [SerializeField] private Transform seatPoint;

        [Tooltip("Where the rider sits relative to the seat point, in the seat point's local space. " +
                 "Push this DOWN by roughly the rider's leg length: a character's origin is between " +
                 "its feet, so seating them at the saddle's origin leaves them standing on it.")]
        [SerializeField] private Vector3 seatOffset = new Vector3(0f, -0.85f, 0f);

        [SerializeField] private Vector3 seatEuler = Vector3.zero;

        [SerializeField] private bool spawnOnStart = true;

        [Header("Dismount")]
        [Tooltip("How far to the side of the mount the rider is placed when getting off.")]
        [SerializeField] private float dismountSideOffset = 1.6f;

        [SerializeField] private float dismountSampleDistance = 6f;

        public GameObject Rider { get; private set; }
        public bool HasRider => Rider != null;

        /// <summary>Set when this passenger created the rider, so it knows to destroy it.</summary>
        private bool ownsRider;

        // What was switched off to make them a passenger, so exactly that much can be switched back
        // on. Recording rather than re-deriving matters: a rider whose AgentController was already
        // disabled by something else must not be handed a working one by dismounting.
        private readonly List<Behaviour> suppressed = new();
        private readonly List<Collider> disabledColliders = new();
        private bool riderWasKinematic;
        private Rigidbody riderBody;

        private Transform SeatTransform => seatPoint != null ? seatPoint : transform;

        private void Start()
        {
            if (spawnOnStart) SpawnRider();
        }

        /// <summary>
        /// Instantiate <see cref="riderPrefab"/>, spawn it for every peer, and seat it.
        ///
        /// Authority only, and answers null elsewhere: a client that made its own rider would be
        /// the only machine that could see it, sitting in a saddle every other player sees filled
        /// by the real one.
        /// </summary>
        public GameObject SpawnRider()
        {
            if (riderPrefab == null || Rider != null) return null;
            if (!Network.Simulates(this)) return null;

            (Vector3 position, Quaternion rotation) = SeatPose(null);

            GameObject rider = NpcSpawn.Create(riderPrefab, position, rotation, this);
            ownsRider = true;
            SeatInternal(rider);
            return rider;
        }

        /// <summary>Put an NPC that already exists into the saddle. Authority only, as above.</summary>
        public void Seat(GameObject rider)
        {
            if (!Network.Simulates(this)) return;

            ownsRider = false;
            SeatInternal(rider);
        }

        private void SeatInternal(GameObject rider)
        {
            if (rider == null || Rider != null) return;

            Rider = rider;

            Suppress(rider);
            Attach(rider.transform);
        }

        /// <summary>
        /// Park the rider in the saddle so the mount carries them.
        ///
        /// <para>
        /// Netcode will not let a spawned <see cref="NetworkObject"/> sit under a plain transform,
        /// and <see cref="seatPoint"/> is a bare child marker — so the networked path parents to
        /// the mount's own NetworkObject, the only legal parent, and folds the marker's offset into
        /// its local space instead. That is the same fold <c>MountModule.ParentRiderToMount</c>
        /// does for a human rider on this very saddle; the two share no code because a player rider
        /// and an NPC passenger share no other requirement.
        /// </para>
        /// </summary>
        private void Attach(Transform rider)
        {
            NetworkObject riderNetObj = rider.GetComponent<NetworkObject>();
            NetworkObject mountNetObj = GetComponentInParent<NetworkObject>();

            if (riderNetObj != null && riderNetObj.IsSpawned &&
                mountNetObj != null && mountNetObj.IsSpawned &&
                riderNetObj.TrySetParent(mountNetObj, worldPositionStays: true))
            {
                (Vector3 position, Quaternion rotation) = SeatPose(mountNetObj.transform);
                rider.SetLocalPositionAndRotation(position, rotation);
                return;
            }

            // Nothing here is spawned, so netcode has no arrangement to replicate and its parenting
            // rules are in the way rather than protecting anything: an unspawned NetworkObject
            // refuses a reparent outright and silently puts the parent back, which left the rider
            // standing in the air at the spot where the mount was born while the mount walked off.
            // Clearing the flag is how you say this object's parenting is not netcode's business.
            if (riderNetObj != null) riderNetObj.AutoObjectParentSync = false;

            rider.SetParent(SeatTransform, worldPositionStays: false);
            rider.SetLocalPositionAndRotation(seatOffset, Quaternion.Euler(seatEuler));
        }

        /// <summary>The seat pose, in <paramref name="space"/> or in world space when that is null.</summary>
        private (Vector3 position, Quaternion rotation) SeatPose(Transform space) =>
            SeatPoseIn(space, SeatTransform, seatOffset, seatEuler);

        /// <summary>
        /// Where a rider sits: <paramref name="offset"/> from <paramref name="seat"/>, read in
        /// <paramref name="space"/> — the mount's root for the netcode path, world space (null) for
        /// everything else.
        ///
        /// The two answers describe the same point in the world. Getting that fold wrong is how a
        /// mounted rider ends up floating above the saddle on every machine but one.
        /// </summary>
        public static (Vector3 position, Quaternion rotation) SeatPoseIn(
            Transform space, Transform seat, Vector3 offset, Vector3 euler)
        {
            Vector3 position = seat.TransformPoint(offset);
            Quaternion rotation = seat.rotation * Quaternion.Euler(euler);

            return space == null
                ? (position, rotation)
                : (space.InverseTransformPoint(position), Quaternion.Inverse(space.rotation) * rotation);
        }

        /// <summary>
        /// Put the rider back on the ground beside the mount as a working NPC again.
        ///
        /// <para>
        /// Refuses while this object is being torn down. Unity will not reparent into or out of a
        /// GameObject that is not active in the hierarchy, and a teardown-time dismount therefore
        /// leaves the rider parented to something that is about to be destroyed — taking the rider
        /// with it. This is the same trap MountModule.OnDisable already documents, and it is worth
        /// repeating rather than inheriting because the two classes share no code.
        /// </para>
        /// </summary>
        public GameObject Dismount()
        {
            if (Rider == null) return null;
            if (!Network.Simulates(this)) return null;

            GameObject rider = Rider;

            if (!gameObject.activeInHierarchy)
            {
                // Nothing safe to do. Leave them seated; OnDestroy takes the rider down with the
                // mount, which is the correct outcome for a mount being unloaded.
                return null;
            }

            Vector3 beside = transform.position
                             + transform.right * dismountSideOffset
                             + Vector3.up * 0.2f;

            if (NavMesh.SamplePosition(beside, out NavMeshHit hit, dismountSampleDistance, NavMesh.AllAreas))
                beside = hit.position;

            Detach(rider.transform);
            rider.transform.SetPositionAndRotation(beside, Quaternion.LookRotation(transform.forward, Vector3.up));

            Restore(rider);

            Rider = null;
            ownsRider = false;
            return rider;
        }

        // Mirror of Attach: a spawned NetworkObject is detached through netcode so the change
        // reaches everyone, rather than by a raw SetParent(null) that only happens here.
        private static void Detach(Transform rider)
        {
            NetworkObject riderNetObj = rider.GetComponent<NetworkObject>();
            if (riderNetObj != null && riderNetObj.IsSpawned && riderNetObj.TryRemoveParent(true))
                return;

            rider.SetParent(null, worldPositionStays: true);
        }

        private void OnDestroy()
        {
            // A rider this passenger created is its responsibility. One it was handed is not — that
            // NPC belongs to whoever seated it and may well be meant to outlive the animal.
            if (!ownsRider || Rider == null) return;

            // Netcode lifts a child NetworkObject up to the scene root when the parent it is under
            // despawns, so by the time this runs the rider is no longer destroyed along with the
            // mount — and a spawned one has to be despawned rather than destroyed, or every client
            // is left with its own copy standing in the desert.
            if (Network.Server && Rider.TryGetComponent(out NetworkObject riderNetObj) && riderNetObj.IsSpawned)
            {
                riderNetObj.Despawn(destroy: true);
                return;
            }

            Destroy(Rider);
        }

        // ── Suppression ──────────────────────────────────────────────────────────

        /// <summary>
        /// Switch off everything that would make the rider try to move under its own power.
        ///
        /// Deliberately not the whole GameObject: the rider must keep animating, rendering and
        /// being talkable-to while mounted — a trader you cannot speak to until they get down is
        /// not a trader.
        /// </summary>
        private void Suppress(GameObject rider)
        {
            suppressed.Clear();
            disabledColliders.Clear();

            foreach (AgentController controller in rider.GetComponentsInChildren<AgentController>(true))
                Disable(controller);

            foreach (NavMeshAgent agent in rider.GetComponentsInChildren<NavMeshAgent>(true))
                Disable(agent);

            // Motors drive the body directly and would fight the saddle for the transform.
            foreach (MonoBehaviour behaviour in rider.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is IMovementMotor) Disable(behaviour);

            riderBody = rider.GetComponent<Rigidbody>();
            if (riderBody != null)
            {
                riderWasKinematic = riderBody.isKinematic;
                riderBody.isKinematic = true;
            }

            // Non-trigger colliders only. The trigger volume is usually what makes the rider
            // interactable, and disabling it is how a mounted trader becomes impossible to talk to.
            foreach (Collider collider in rider.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || !collider.enabled) continue;
                collider.enabled = false;
                disabledColliders.Add(collider);
            }
        }

        private void Disable(Behaviour behaviour)
        {
            if (behaviour == null || !behaviour.enabled) return;
            behaviour.enabled = false;
            suppressed.Add(behaviour);
        }

        private void Restore(GameObject rider)
        {
            foreach (Behaviour behaviour in suppressed)
                if (behaviour != null) behaviour.enabled = true;

            suppressed.Clear();

            foreach (Collider collider in disabledColliders)
                if (collider != null) collider.enabled = true;

            disabledColliders.Clear();

            if (riderBody != null)
            {
                riderBody.isKinematic = riderWasKinematic;
                riderBody = null;
            }

            // A NavMeshAgent switched on away from the mesh is inert and logs nothing. Warping it
            // is what makes the rider actually able to walk after getting off.
            if (rider.TryGetComponent(out NavMeshAgent agent) && agent.enabled && !agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(rider.transform.position, out NavMeshHit hit,
                                           dismountSampleDistance, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                }
            }
        }

        private void OnValidate()
        {
            dismountSideOffset = Mathf.Max(0.2f, dismountSideOffset);
            dismountSampleDistance = Mathf.Max(0.5f, dismountSampleDistance);
        }

        private void OnDrawGizmosSelected()
        {
            Transform seat = SeatTransform;
            if (seat == null) return;

            Gizmos.color = new Color(1f, 0.8f, 0.3f);
            Gizmos.DrawWireSphere(seat.TransformPoint(seatOffset), 0.25f);
        }
    }
}
