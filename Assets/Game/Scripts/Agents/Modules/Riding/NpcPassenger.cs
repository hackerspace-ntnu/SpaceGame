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
//
// Two things about a seated rider are NOT the authority's business, and both used to be treated as
// if they were:
//
//   • How they look. A watching machine is handed the parenting and nothing else, so the riding
//     pose has to be applied from what this machine can see rather than from who it seated —
//     otherwise the caravan rides past every client with its nomads standing bolt upright.
//   • How the world reaches them. This class used to switch the rider's colliders off to stop them
//     shoving the mount, which also took them out of every raycast, overlap and interaction probe
//     in the game: a mounted nomad could not be shot, roped, lassoed or even aimed at. Collision is
//     suspended pairwise now (RiderCollisionIgnore), which stops the shoving and leaves the rider
//     a solid, hittable, ropeable body — which is what they should have been all along.
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class NpcPassenger : MonoBehaviour, ISeatOccupant
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
        private bool riderWasKinematic;
        private Rigidbody riderBody;

        // Held so the death subscription is undone against the very instance it was made against,
        // even once Rider has been cleared. Same reason MountModule keeps its own.
        private HealthComponent riderHealth;

        // Per-machine, and deliberately separate from Rider: this is who this machine is POSING and
        // holding apart from the mount, which on a watching client is somebody it never seated.
        private readonly RiderCollisionIgnore collisions = new RiderCollisionIgnore();
        private MountedRiderPose pose;
        private Transform posedRider;

        private Transform SeatTransform => seatPoint != null ? seatPoint : transform;

        private void Awake() => pose = GetComponent<MountedRiderPose>();

        private void Start()
        {
            if (spawnOnStart) SpawnRider();
            RefreshSeatedRider();
        }

        // A rider can arrive on this machine without this component doing anything at all — netcode
        // reparents the authority's rider straight under the mount — and can leave the same way.
        // Both land here as a change to the mount's children.
        private void OnTransformChildrenChanged() => RefreshSeatedRider();

        private void OnEnable() => RefreshSeatedRider();

        private void OnDisable()
        {
            // Release rather than merely forget: the pose lives on the mount and would otherwise
            // keep writing a rider's bones with nothing left driving the blend.
            PresentRider(null);
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
            SubscribeToRiderDeath(rider);
            RefreshSeatedRider();
        }

        /// <summary>
        /// A rider who is hurt, or killed, gets off.
        ///
        /// <para>
        /// A passenger's brain is switched off — that is what makes them a passenger — so a seated
        /// rider cannot chase, cannot fight, cannot even turn round. Being shootable and unable to
        /// answer is worse than being invulnerable: it reads as a broken enemy rather than a
        /// peaceful one. Getting off is what makes them an NPC again, and every module that decides
        /// what a provoked nomad does about it is already on the prefab and already listening to
        /// the same damage.
        /// </para>
        /// <para>
        /// Death matters separately because <c>HealthReactionModule</c> kills by switching the
        /// GameObject off after a despawn delay: without this the corpse rode on, sitting up, and
        /// then blinked out of the saddle.
        /// </para>
        /// <para>
        /// Authority only, like every other seat change: <see cref="Dismount"/> refuses elsewhere
        /// and the resulting reparent replicates on its own.
        /// </para>
        /// </summary>
        private void SubscribeToRiderDeath(GameObject rider)
        {
            UnsubscribeFromRiderDeath();

            riderHealth = rider.GetComponent<HealthComponent>();
            if (riderHealth == null) return;

            riderHealth.OnDamage += HandleRiderDamaged;
            riderHealth.OnDeath += HandleRiderDied;
        }

        private void UnsubscribeFromRiderDeath()
        {
            if (riderHealth != null)
            {
                riderHealth.OnDamage -= HandleRiderDamaged;
                riderHealth.OnDeath -= HandleRiderDied;
            }

            riderHealth = null;
        }

        private void HandleRiderDamaged(int amount) => Dismount();

        private void HandleRiderDied()
        {
            // A load restoring a rider at zero health raises OnDeath exactly like a bullet does, and
            // dismounting there would scatter the caravan's dead across the desert on every reload.
            // (OnDamage needs no such guard — a restore never raises it.)
            if (riderHealth != null && riderHealth.IsRestoring) return;

            Dismount();
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

            UnsubscribeFromRiderDeath();
            Detach(rider.transform);
            rider.transform.SetPositionAndRotation(beside, Quaternion.LookRotation(transform.forward, Vector3.up));

            Restore(rider);

            Rider = null;
            ownsRider = false;
            RefreshSeatedRider();
            return rider;
        }

        /// <summary>
        /// <see cref="ISeatOccupant"/>: a player is taking this saddle, so the current rider gets
        /// out of it. Same dismount as any other — they land beside the animal as a working NPC,
        /// which for a caravan's nomad means one who has just been thrown off their own mount and
        /// still has an opinion about it.
        /// </summary>
        public void VacateSeat() => Dismount();

        /// <summary>
        /// Get <paramref name="rider"/> out of whatever saddle they are in, and answer whether they
        /// were in one.
        ///
        /// <para>
        /// For anything that takes physical hold of a creature — a lasso, a rope. A seated rider's
        /// transform belongs to the mount carrying them, so hauling on one pulls a body that cannot
        /// move: the rope goes taut and the animal walks on regardless. Unseating them first is
        /// also the obvious reading of what the player just did, and it leaves a creature standing
        /// on its own feet, which every rope in the game already knows how to drag.
        /// </para>
        /// <para>
        /// Authority-only in effect: <see cref="Dismount"/> refuses elsewhere and the reparent
        /// replicates on its own, so a peer calling this gets a harmless false.
        /// </para>
        /// </summary>
        public static bool UnseatRider(GameObject rider)
        {
            if (rider == null || rider.transform.parent == null) return false;

            // Searched from the PARENT, so a mount that happens to be somebody's rider itself is
            // not mistaken for its own passenger.
            NpcPassenger passenger = rider.transform.parent.GetComponentInParent<NpcPassenger>();
            if (passenger == null || passenger.Rider != rider) return false;

            return passenger.Dismount() != null;
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
            UnsubscribeFromRiderDeath();

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
        /// <para>
        /// Deliberately not the whole GameObject: the rider must keep animating, rendering and
        /// being talkable-to while mounted — a trader you cannot speak to until they get down is
        /// not a trader.
        /// </para>
        /// <para>
        /// And deliberately not their colliders, which is what this used to do. A collider is not
        /// only how a body pushes the world about; it is how the world finds the body at all.
        /// Raycasts, overlap sweeps and the interaction probe all pass straight through a disabled
        /// one, so a rider suppressed that way was not merely light on his feet — he could not be
        /// shot, could not be lassoed or roped, could not be aimed at and could not be looked at.
        /// What was actually wanted was for his body not to shove the animal underneath him, and
        /// that is <see cref="RiderCollisionIgnore"/>'s job, applied per machine in
        /// <see cref="PresentRider"/> because physics is local and only the authority gets here.
        /// </para>
        /// </summary>
        private void Suppress(GameObject rider)
        {
            suppressed.Clear();

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
        }

        private void Disable(Behaviour behaviour)
        {
            if (behaviour == null || !behaviour.enabled) return;
            behaviour.enabled = false;
            suppressed.Add(behaviour);
        }

        private void Restore(GameObject rider)
        {
            // A dead rider gets nothing back. HealthReactionModule has already switched the brain
            // off and started the despawn timer by the time a death-triggered dismount reaches
            // here, and handing back a working AgentController stands the corpse up and walks it
            // away. Same rule MountModule applies to a player who dies in the saddle.
            bool dead = rider.TryGetComponent(out HealthComponent health) && !health.Alive;

            if (!dead)
            {
                foreach (Behaviour behaviour in suppressed)
                    if (behaviour != null) behaviour.enabled = true;
            }

            suppressed.Clear();

            if (riderBody != null)
            {
                riderBody.isKinematic = riderWasKinematic;
                riderBody = null;
            }

            if (dead) return;

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

        // ── Presentation: what EVERY machine does, authority or not ──────────────

        /// <summary>
        /// Work out who this machine is carrying, and pose and unhook them accordingly.
        ///
        /// <para>
        /// Not the same question as who this passenger seated. Only the authority seats anybody;
        /// every other machine is handed the finished arrangement by netcode, as a spawned rider
        /// that has quietly become a child of this mount. Both machines still have to sit that
        /// rider in the saddle and keep the two bodies from shoving each other, and driving either
        /// off <see cref="Rider"/> alone is how a caravan comes past a client with its nomads
        /// standing bolt upright on the animals' backs.
        /// </para>
        /// </summary>
        public void RefreshSeatedRider() =>
            PresentRider(Rider != null ? Rider.transform : FindSeatedNpc());

        /// <summary>
        /// Who this machine is posing and holding clear of the mount. On the authority that is
        /// <see cref="Rider"/>; elsewhere it is whoever netcode parented into the saddle.
        /// </summary>
        public Transform PosedRider => posedRider;

        private void PresentRider(Transform rider)
        {
            if (posedRider == rider) return;

            if (pose != null && posedRider != null)
                pose.ReleaseRider(posedRider);

            // Restoring a pair needs both colliders active, and a mount being deactivated or
            // unloaded is on its way to having none. Nothing is leaked by dropping them: the whole
            // hierarchy is going.
            if (gameObject.activeInHierarchy)
                collisions.Restore();
            else
                collisions.Forget();

            posedRider = rider;

            if (posedRider == null) return;

            collisions.Apply(posedRider, transform);

            if (pose != null)
                pose.PoseRider(posedRider);
        }

        /// <summary>
        /// The NPC riding this mount, as seen from a machine that was told nothing.
        ///
        /// <para>
        /// An <see cref="AgentController"/> below this one that is not this one: the mount's own
        /// brain sits on the mount root and is skipped, the saddle markers and the rig's bones
        /// carry no brain at all, and a PLAYER rider has no AgentController — which is what keeps
        /// this from adopting somebody <see cref="MountModule"/> is already posing.
        /// </para>
        /// </summary>
        private Transform FindSeatedNpc()
        {
            foreach (AgentController controller in GetComponentsInChildren<AgentController>(true))
            {
                if (controller == null || controller.gameObject == gameObject) continue;
                return controller.transform;
            }

            return null;
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
