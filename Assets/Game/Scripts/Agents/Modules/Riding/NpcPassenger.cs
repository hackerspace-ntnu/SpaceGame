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
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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
            if (spawnOnStart && riderPrefab != null && Rider == null)
                SpawnRider();
        }

        /// <summary>Instantiate <see cref="riderPrefab"/> and seat it.</summary>
        public GameObject SpawnRider()
        {
            if (riderPrefab == null) return null;

            GameObject rider = Instantiate(riderPrefab, SeatTransform.position, SeatTransform.rotation);
            ownsRider = true;
            SeatInternal(rider);
            return rider;
        }

        /// <summary>Put an NPC that already exists into the saddle.</summary>
        public void Seat(GameObject rider)
        {
            ownsRider = false;
            SeatInternal(rider);
        }

        private void SeatInternal(GameObject rider)
        {
            if (rider == null || Rider != null) return;

            Rider = rider;

            Suppress(rider);

            rider.transform.SetParent(SeatTransform, worldPositionStays: false);
            rider.transform.localPosition = seatOffset;
            rider.transform.localRotation = Quaternion.Euler(seatEuler);
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

            GameObject rider = Rider;

            if (!gameObject.activeInHierarchy)
            {
                // Nothing safe to do. Leave them seated; whatever is destroying the mount takes the
                // rider with it, which is the correct outcome for a mount being unloaded.
                return null;
            }

            Vector3 beside = transform.position
                             + transform.right * dismountSideOffset
                             + Vector3.up * 0.2f;

            if (NavMesh.SamplePosition(beside, out NavMeshHit hit, dismountSampleDistance, NavMesh.AllAreas))
                beside = hit.position;

            rider.transform.SetParent(null, worldPositionStays: true);
            rider.transform.SetPositionAndRotation(beside, Quaternion.LookRotation(transform.forward, Vector3.up));

            Restore(rider);

            Rider = null;
            ownsRider = false;
            return rider;
        }

        private void OnDestroy()
        {
            // A rider this passenger created is its responsibility. One it was handed is not — that
            // NPC belongs to whoever seated it and may well be meant to outlive the animal.
            if (ownsRider && Rider != null)
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
