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
// the saddle with its own MOVEMENT switched off, along for the journey. Half the agents, no
// arbitration between a rider's AI and its mount's, and the rider is still a full NPC the moment it
// gets off.
//
// Movement, and only movement. A passenger keeps its eyes and its trigger finger: it acquires its
// own targets and fires its own gun from the saddle (AgentController.RidesAsPassenger). That split
// is the whole reason a mount is not a weapon — an outrider's horse carries and an outrider's
// Clanker shoots, and neither does the other's job.
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

        [Tooltip("Animator bool on the RIDER held true while seated, so a rig MountedRiderPose " +
                 "cannot pose -- anything imported Generic -- can play a sitting clip instead of " +
                 "standing to attention in the saddle. Skipped when the rider's controller has no " +
                 "such parameter; empty to never touch the rider's animator.")]
        [SerializeField] private string seatedAnimatorBool = "IsSeated";

        [Header("Dismount")]
        [Tooltip("How far to the side of the mount the rider is placed when getting off.")]
        [SerializeField] private float dismountSideOffset = 1.6f;

        [SerializeField] private float dismountSampleDistance = 6f;

        public GameObject Rider { get; private set; }
        public bool HasRider => Rider != null;

        /// <summary>Set when this passenger created the rider, so it knows to destroy it.</summary>
        private bool ownsRider;

        // What was switched off to make them a passenger, so exactly that much can be switched back
        // on. Shared with every other NPC carrier; see NpcSeating.
        private readonly NpcSeating seating = new NpcSeating();
        private readonly List<Transform> seatedNpcs = new();

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

            GameObject rider = NpcSpawn.Create(riderPrefab, position, rotation, this,
                                               spawned => GroupMembership.StampRider(gameObject, spawned));
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

            seating.Suppress(rider);
            NpcSeating.Attach(rider.transform, GetComponentInParent<NetworkObject>(), SeatTransform, seatOffset, seatEuler);
            SubscribeToRiderDeath(rider);
            RefreshSeatedRider();
        }

        /// <summary>
        /// A rider who is hurt, or killed, gets off.
        ///
        /// <para>
        /// A passenger can shoot back, but it cannot move: where it goes is the animal's decision,
        /// and an animal has its own reasons to be somewhere. So a rider under fire is a rider that
        /// cannot take cover, cannot close and cannot break off — it can only sit in the saddle and
        /// trade. Getting off is what gives it those verbs back, and every module that decides what
        /// a provoked nomad does with them is already on the prefab and already listening to the
        /// same damage.
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

        /// <summary>The seat pose, in <paramref name="space"/> or in world space when that is null.</summary>
        private (Vector3 position, Quaternion rotation) SeatPose(Transform space) =>
            NpcSeating.SeatPoseIn(space, SeatTransform, seatOffset, seatEuler);

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
            NpcSeating.Detach(rider.transform);
            rider.transform.SetPositionAndRotation(beside, Quaternion.LookRotation(transform.forward, Vector3.up));

            seating.Restore(rider, dismountSampleDistance);

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

        /// <summary>
        /// Tell the rider's own animator it is sitting. The brain is off, so nothing else will.
        /// Presentation, so it runs on every machine with the pose and the collision pairing.
        /// Checked against the controller's parameter list rather than fired blind: SetBool on a
        /// parameter the controller lacks is a warning per call, and most riders are humanoids
        /// posed by <see cref="MountedRiderPose"/> with no such flag.
        /// </summary>
        private void SetSeatedFlag(GameObject rider, bool seated)
        {
            if (string.IsNullOrEmpty(seatedAnimatorBool) || rider == null) return;

            foreach (Animator animator in rider.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController == null) continue;
                foreach (AnimatorControllerParameter parameter in animator.parameters)
                {
                    if (parameter.type != AnimatorControllerParameterType.Bool ||
                        parameter.name != seatedAnimatorBool) continue;
                    animator.SetBool(seatedAnimatorBool, seated);
                    break;
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

            if (posedRider != null)
            {
                if (pose != null) pose.ReleaseRider(posedRider);
                SetSeatedFlag(posedRider.gameObject, false);
            }

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
            SetSeatedFlag(posedRider.gameObject, true);
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
            NpcSeating.CollectSeatedNpcs(transform, seatedNpcs);
            return seatedNpcs.Count > 0 ? seatedNpcs[0] : null;
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
